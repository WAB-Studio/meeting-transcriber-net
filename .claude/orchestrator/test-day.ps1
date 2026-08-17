#requires -Version 5.1
<#
  The probe over the day's engine. It runs in CI and spends no session: everything checked here is
  the part that needs no `claude` running - pulling the contract out of what a session emitted,
  reading a result off a stream, and what each rule fires on.

  What it cannot cover is a real `claude -p`, a real merge and a real board. Those are exercised by
  running a day and reading report.md afterwards.

    .\test-day.ps1        # exits 0 if everything passes, 1 if anything fails
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "day.psm1") -Force

$script:Failed = 0
$script:Ran = 0

function Check([string]$Name, [scriptblock]$Body) {
  $script:Ran++
  try {
    $msg = & $Body
    if ([string]::IsNullOrEmpty($msg)) {
      Write-Host ("  ok    " + $Name)
    } else {
      Write-Host ("  FAIL  " + $Name + " -- " + $msg) -ForegroundColor Red
      $script:Failed++
    }
  } catch {
    Write-Host ("  FAIL  " + $Name + " -- threw: " + $_.Exception.Message) -ForegroundColor Red
    $script:Failed++
  }
}

function New-Sandbox {
  $p = Join-Path ([System.IO.Path]::GetTempPath()) ("day-test-" + [guid]::NewGuid().ToString("n").Substring(0, 8))
  New-Item -ItemType Directory -Force $p | Out-Null
  return $p
}

# A status object with nothing firing. Each rule test changes only the field it is about, so a new
# field on the real status does not silently make one of these vacuous.
function New-QuietStatus {
  return [pscustomobject]@{
    Running = $false; QuietForMinutes = $null; Cycle = 1; Role = "worker"
    Denials = 0; DenialsByTool = @(); CycleCosts = @(); LastCycleCost = $null
    Killed = $false; Unreadable = ""; RateLimit = $null; Ended = $false; EndReason = ""
    IdleForMinutes = $null; Past = @(); Anomalies = @()
    Waiting = $false; Questions = @(); Answered = @(); Recovered = @()
  }
}

# What the executor actually does with a status, asked of the same function the executor asks, so
# the two cannot drift. The question here is always "would this have stopped the day", never "is
# the label right" -- a label the loop ignores is what the review caught the first time round.
function Test-WouldHalt {
  param($Status)
  return @(Get-HaltingAnomalies -Status $Status).Count -gt 0
}

$HandoffKeys = @("outcome","task_id","pr_number","isc_closed","probes",
                 "decisions_deferred","left_out","skipped","blocked_reason","head_sha")

$HandoffJson = @'
{
  "outcome": "pr_opened",
  "task_id": "86ajzq801",
  "pr_number": 36,
  "isc_closed": ["ISC-127"],
  "probes": [{ "command": "dotnet test", "passed": true }],
  "decisions_deferred": [],
  "left_out": [],
  "skipped": [],
  "blocked_reason": "",
  "head_sha": "d3ba908c81528549033c8c36f15929a016d4f9b5"
}
'@

$Fence = '```json'

Write-Host ""
Write-Host "  the contract comes out of what the session emitted"

Check "a bare handoff reads" {
  $c = Get-ContractFromText $HandoffJson
  if ($null -eq $c) { return "did not read it" }
  if ($c.pr_number -ne 36) { return "pr_number came out $($c.pr_number)" }
  ""
}

Check "a handoff inside a fenced block reads" {
  $t = "Done. Here is the handoff:`n`n$Fence`n$HandoffJson`n```````n"
  $c = Get-ContractFromText $t
  if ($null -eq $c) { return "did not read it" }
  if ($c.task_id -ne "86ajzq801") { return "task_id came out $($c.task_id)" }
  ""
}

Check "prose before and after changes nothing" {
  $t = "I finished the work.`n$HandoffJson`nThat is all."
  $c = Get-ContractFromText $t
  if ($null -eq $c) { return "did not read it" }
  ""
}

Check "the last block wins when there is more than one" {
  $first = '{"outcome":"blocked","task_id":"old"}'
  $t = "First I thought this:`n$Fence`n$first`n```````n`nAnd in the end this:`n$Fence`n$HandoffJson`n```````n"
  $c = Get-ContractFromText $t
  if ($c.task_id -ne "86ajzq801") { return "kept the first one: $($c.task_id)" }
  ""
}

Check "no JSON at all gives null rather than an invention" {
  $c = Get-ContractFromText "I could not do anything and I wrote no JSON."
  if ($null -ne $c) { return "returned something" }
  ""
}

Write-Host ""
Write-Host "  the contract is validated by value"

Check "a complete contract passes" {
  $c = Get-ContractFromText $HandoffJson
  $e = Test-DayContract -Contract $c -Required $HandoffKeys -Field "outcome" -Allowed @("pr_opened","blocked","no_tasks")
  if ($e -ne "") { return $e }
  ""
}

Check "a missing field is named" {
  $c = '{"outcome":"pr_opened","task_id":"x"}' | ConvertFrom-Json
  $e = Test-DayContract -Contract $c -Required $HandoffKeys -Field "outcome" -Allowed @("pr_opened")
  if ($e -notmatch "missing fields") { return "did not name it: $e" }
  if ($e -notmatch "head_sha") { return "did not say which: $e" }
  ""
}

Check "an outcome outside the contract is refused" {
  $c = ($HandoffJson | ConvertFrom-Json)
  $c.outcome = "almost_done"
  $e = Test-DayContract -Contract $c -Required $HandoffKeys -Field "outcome" -Allowed @("pr_opened","blocked","no_tasks")
  if ($e -eq "") { return "it was accepted" }
  ""
}

Check "a hold cannot pass for a pass" {
  $c = '{"verdict":"hold","reasons":[],"unreported_decisions":[],"isc_unproved":[],"followups_created":[],"actions_taken":[],"audited_head_sha":"abc"}' | ConvertFrom-Json
  $e = Test-DayContract -Contract $c -Required @("verdict","audited_head_sha") -Field "verdict" -Allowed @("pass","pass_with_followup")
  if ($e -eq "") { return "a hold passed a list that does not include it" }
  ""
}

Write-Host ""
Write-Host "  the result is the last line of the stream, not the file"

Check "the result line is found among all the others" {
  $box = New-Sandbox
  try {
    $s = Join-Path $box "worker-1.stream.jsonl"
    $lines = @(
      '{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","resetsAt":1786926600}}'
      '{"type":"system","subtype":"init"}'
      '{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"dotnet test"}}]}}'
      '{"type":"result","subtype":"success","is_error":false,"num_turns":7,"total_cost_usd":1.25,"result":"done","permission_denials":[]}'
    )
    [System.IO.File]::WriteAllLines($s, $lines)
    $r = Get-SessionResult $s
    if ($null -eq $r) { return "did not find the result" }
    if ($r.num_turns -ne 7) { return "turns came out $($r.num_turns)" }
    if ([math]::Abs($r.total_cost_usd - 1.25) -gt 0.001) { return "cost came out $($r.total_cost_usd)" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

Check "the LAST result line wins, not the first one that looks like it" {
  $box = New-Sandbox
  try {
    $s = Join-Path $box "worker-1.stream.jsonl"
    # A session that reads another run's stream quotes a result line back inside a tool_result, and
    # that line is earlier in the file than its own. Taking the first match reads the wrong session.
    $lines = @(
      '{"type":"user","message":{"content":[{"type":"tool_result","content":"{\"type\":\"result\",\"num_turns\":999,\"total_cost_usd\":99.0}"}]}}'
      '{"type":"result","subtype":"success","is_error":false,"num_turns":7,"total_cost_usd":1.25,"result":"done"}'
    )
    [System.IO.File]::WriteAllLines($s, $lines)
    $r = Get-SessionResult $s
    if ($null -eq $r) { return "found no result" }
    if ($r.num_turns -ne 7) { return "took the quoted one: turns came out $($r.num_turns)" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

Check "a stream with no result line gives null" {
  $box = New-Sandbox
  try {
    $s = Join-Path $box "worker-1.stream.jsonl"
    [System.IO.File]::WriteAllLines($s, @('{"type":"system"}', '{"type":"assistant","message":{"content":[]}}'))
    if ($null -ne (Get-SessionResult $s)) { return "returned something" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

Check "the live stream attributes a denial to the tool that asked" {
  $box = New-Sandbox
  try {
    $s = Join-Path $box "worker-1.stream.jsonl"
    $lines = @(
      '{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"PowerShell","input":{"command":"python clickup.py lists"}}]}}'
      '{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","is_error":true,"content":"Claude requested permissions to use PowerShell, but you haven''t granted it yet."}]}}'
    )
    [System.IO.File]::WriteAllLines($s, $lines)
    $a = Get-SessionActivity $s
    if ($a.Denials -ne 1) { return "counted $($a.Denials)" }
    if ($a.DenialDetail[0].tool -ne "PowerShell") { return "attributed it to $($a.DenialDetail[0].tool)" }
    if ($a.DenialDetail[0].command -notmatch "clickup") { return "lost the command: $($a.DenialDetail[0].command)" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host ""
Write-Host "  a file somebody else still has open"

<#
  The state every file here is read in: `claude` holds its stream open for the whole cycle, writing,
  sharing it for reading only. A reader that asks for the default share meets an IOException on the
  open -- which is what every status taken during a session did until 2026-08-16, reporting zeros
  for a session it had not managed to read at all.
#>
function Open-Writer([string]$Path, [string[]]$Lines) {
  $fs = New-Object System.IO.FileStream(
    $Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
  $bytes = [System.Text.Encoding]::UTF8.GetBytes((($Lines -join "`n") + "`n"))
  $fs.Write($bytes, 0, $bytes.Length)
  $fs.Flush()
  return $fs
}

Check "the event stream reads while the executor still holds it" {
  $box = New-Sandbox
  $w = $null
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    $w = Open-Writer (Get-EventsPath $box) @(
      '{"ts":"2026-08-17T00:00:00Z","kind":"day_started","pid":4188}'
      '{"ts":"2026-08-17T00:01:00Z","kind":"session_started","cycle":1,"role":"worker","stream":"worker-1.stream.jsonl"}')
    $ev = @(Read-DayEvents $box)
    if ($ev.Count -ne 2) { return "read $($ev.Count) events off a file that is still open" }
    ""
  } finally {
    if ($w) { $w.Dispose() }
    Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue
  }
}

Check "the result line reads off a stream the session still has open" {
  $box = New-Sandbox
  $w = $null
  try {
    $s = Join-Path $box "worker-1.stream.jsonl"
    $w = Open-Writer $s @(
      '{"type":"system","subtype":"init"}'
      '{"type":"result","subtype":"success","is_error":false,"num_turns":7,"total_cost_usd":1.25,"result":"done"}')
    $r = Get-SessionResult $s
    if ($null -eq $r) { return "found no result while the file was open" }
    if ($r.num_turns -ne 7) { return "turns came out $($r.num_turns)" }
    ""
  } finally {
    if ($w) { $w.Dispose() }
    Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue
  }
}

Check "a denial is seen while the session is still holding the file" {
  $box = New-Sandbox
  $w = $null
  try {
    $s = Join-Path $box "worker-1.stream.jsonl"
    # The whole point of the live read: a denial only shows here until the session ends, and while
    # it runs is exactly when somebody can still be told.
    $w = Open-Writer $s @(
      '{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"PowerShell","input":{"command":"python clickup.py lists"}}]}}'
      '{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","is_error":true,"content":"Claude requested permissions to use PowerShell, but you haven''t granted it yet."}]}}')
    $a = Get-SessionActivity $s
    if ($a.Denials -ne 1) { return "counted $($a.Denials) over an open file" }
    if ($a.ToolCalls -ne 1) { return "counted $($a.ToolCalls) tool calls" }
    ""
  } finally {
    if ($w) { $w.Dispose() }
    Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue
  }
}

Check "the status of a live session carries its denials rather than zeros" {
  $box = New-Sandbox
  $w = $null
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x"; pid = $PID } | Out-Null
    New-DayEvent -LogDir $box -Kind "session_started" -Data @{ cycle = 1; role = "worker"; stream = "worker-1.stream.jsonl" } | Out-Null
    $w = Open-Writer (Join-Path $box "worker-1.stream.jsonl") @(
      '{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"PowerShell","input":{"command":"python clickup.py lists"}}]}}'
      '{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","is_error":true,"content":"Claude requested permissions to use PowerShell, but you haven''t granted it yet."}]}}')

    $st = Get-DayStatus -LogDir $box
    if ($st.Unreadable) { return "could not read the live stream: $($st.Unreadable)" }
    if ($st.Denials -ne 1) { return "the status counted $($st.Denials) denials on a running session" }
    if ($st.ToolCalls -ne 1) { return "the status counted $($st.ToolCalls) tool calls" }
    if (-not @($st.Anomalies | Where-Object { $_.code -eq "denials" }).Count) { return "nothing fired over it" }
    ""
  } finally {
    if ($w) { $w.Dispose() }
    Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue
  }
}

Check "a stream that cannot be read is said, not reported as nothing" {
  $st = New-QuietStatus
  $st.Running = $true
  $st.Unreadable = "worker-1.stream.jsonl: could not read it"
  $an = @(Get-DayAnomalies -Status $st | Where-Object { $_.code -eq "unreadable" })
  if ($an.Count -ne 1) { return "an unreadable stream fired $($an.Count) anomalies" }
  if ($an[0].level -ne "warn") { return "it came out at level $($an[0].level)" }
  if (Test-WouldHalt -Status $st) { return "one unreadable poll stopped a paid day" }
  ""
}

Write-Host ""
Write-Host "  permission denials"

Check "the ones on the result come off it counted and grouped" {
  $r = '{"permission_denials":[
    {"tool_name":"PowerShell","tool_input":{"command":"$env:X = 1; python a.py"}},
    {"tool_name":"PowerShell","tool_input":{"command":"python a.py --another-way"}},
    {"tool_name":"Bash","tool_input":{"command":"cat ~/.codex/config"}}]}' | ConvertFrom-Json
  $d = Get-ResultDenials $r
  if ($d.Count -ne 3) { return "counted $($d.Count)" }
  $g = Group-Denials $d
  if ($g.Count -ne 2) { return "grouped into $($g.Count): $(($g | ForEach-Object { $_.tool }) -join ' / ')" }
  $ps = @($g | Where-Object { $_.tool -eq "PowerShell python" })
  if ($ps.Count -ne 1) { return "the two python spellings did not land together" }
  if ($ps[0].count -ne 2) { return "python came out with $($ps[0].count)" }
  ""
}

Check "two attempts at one tool stop the day, one only warns" {
  $one = New-QuietStatus
  $one.Denials = 1
  $one.DenialsByTool = @([pscustomobject]@{ tool = "Bash"; count = 1; commands = @("cat x") })
  $a = Get-DayAnomalies -Status $one
  $den = @($a | Where-Object { $_.code -eq "denials" })
  if ($den.Count -ne 1) { return "one denial fired $($den.Count) rules" }
  if ($den[0].level -ne "warn") { return "a single denial came out as $($den[0].level)" }

  $groping = New-QuietStatus
  $groping.Denials = 3
  $groping.DenialsByTool = @([pscustomobject]@{ tool = "PowerShell"; count = 3; commands = @("python a.py") })
  $a2 = Get-DayAnomalies -Status $groping
  $den2 = @($a2 | Where-Object { $_.code -eq "denials" })
  if ($den2[0].level -ne "stop") { return "three attempts at one tool came out as $($den2[0].level)" }
  ""
}

Check "no denial fires no denial rule" {
  $a = Get-DayAnomalies -Status (New-QuietStatus)
  if (@($a | Where-Object { $_.code -eq "denials" }).Count) { return "fired with none" }
  ""
}

Check "the same program refused twice groups as one, however it was spelled" {
  $d = @(
    (New-Denial -Tool "PowerShell" -Command '$env:PYTHONIOENCODING = "utf-8"; python clickup.py lists'),
    (New-Denial -Tool "PowerShell" -Command 'python "C:\x\clickup.py" tasks'),
    (New-Denial -Tool "Bash" -Command 'cd /c/repo && python clickup.py lists'))
  $g = Group-Denials $d
  $ps = @($g | Where-Object { $_.tool -eq "PowerShell python" })
  if ($ps.Count -ne 1) { return "the two PowerShell spellings did not group: $(($g | ForEach-Object { $_.tool }) -join ' / ')" }
  if ($ps[0].count -ne 2) { return "grouped $($ps[0].count) of them" }
  if (-not @($g | Where-Object { $_.tool -eq "Bash python" }).Count) { return "the Bash one lost its program" }
  ""
}

Check "two different programs refused once each is not groping" {
  $d = @(
    (New-Denial -Tool "PowerShell" -Command 'python clickup.py lists'),
    (New-Denial -Tool "PowerShell" -Command 'codex exec --skip-git-repo-check "review"'))
  $g = Group-Denials $d
  if ($g.Count -ne 2) { return "grouped two unrelated rules into $($g.Count)" }

  $st = New-QuietStatus
  $st.Denials = 2; $st.DenialsByTool = $g
  $st.Anomalies = Get-DayAnomalies -Status $st
  if (Test-WouldHalt $st) { return "two unrelated denials stopped the day" }
  ""
}

Check "groping does not just get labelled -- it stops the day" {
  $st = New-QuietStatus
  $st.Denials = 2
  $st.DenialsByTool = @([pscustomobject]@{ tool = "PowerShell python"; count = 2; commands = @("python clickup.py lists") })
  $st.Anomalies = Get-DayAnomalies -Status $st
  if (-not (Test-WouldHalt $st)) { return "the executor would have carried on to the merge" }
  ""
}

Check "a single denial is loud but does not stop the day" {
  $st = New-QuietStatus
  $st.Denials = 1
  $st.DenialsByTool = @([pscustomobject]@{ tool = "Bash cat"; count = 1; commands = @("cat x") })
  $st.Anomalies = Get-DayAnomalies -Status $st
  if (-not @($st.Anomalies | Where-Object { $_.code -eq "denials" }).Count) { return "it was not even reported" }
  if (Test-WouldHalt $st) { return "one denial stopped the day" }
  ""
}

Check "a closed window stops the day, and so does a killed session" {
  foreach ($case in @("window", "killed")) {
    $st = New-QuietStatus
    if ($case -eq "window") { $st.RateLimit = [pscustomobject]@{ status = "rejected"; resetsAt = 1786926600 } }
    else { $st.Killed = $true }
    $st.Anomalies = Get-DayAnomalies -Status $st
    if (-not (Test-WouldHalt $st)) { return "$case did not stop the day" }
  }
  ""
}

Check "a probe that says the string false is red, not green" {
  if (Test-JsonTrue "false") { return "the string false read as true" }
  if (Test-JsonTrue $null) { return "null read as true" }
  if (-not (Test-JsonTrue $true)) { return "true read as false" }
  if (-not (Test-JsonTrue "true")) { return "the string true read as false" }
  ""
}

Write-Host ""
Write-Host "  the other rules"

Check "a session quiet too long fires silence" {
  $st = New-QuietStatus
  $st.Running = $true; $st.QuietForMinutes = 40; $st.Cycle = 2
  $a = Get-DayAnomalies -Status $st
  if (-not (@($a | Where-Object { $_.code -eq "silence" }).Count)) { return "did not fire" }
  ""
}

Check "a live session that is talking fires nothing" {
  $st = New-QuietStatus
  $st.Running = $true; $st.QuietForMinutes = 0.5
  $a = Get-DayAnomalies -Status $st
  if ($a.Count -ne 0) { return "fired $($a.Count): $(($a | ForEach-Object { $_.code }) -join ',')" }
  ""
}

Check "a cycle spending three times the median fires cost" {
  $st = New-QuietStatus
  $st.Cycle = 4; $st.CycleCosts = @(2.0, 2.0, 2.0, 30.0); $st.LastCycleCost = 30.0
  $a = Get-DayAnomalies -Status $st
  if (-not (@($a | Where-Object { $_.code -eq "cost" }).Count)) { return "did not fire" }
  ""
}

Check "without enough samples cost does not fire" {
  $st = New-QuietStatus
  $st.CycleCosts = @(26.0); $st.LastCycleCost = 26.0
  $a = Get-DayAnomalies -Status $st
  if (@($a | Where-Object { $_.code -eq "cost" }).Count) { return "fired on a single cycle" }
  ""
}

Check "a closed usage window stops the day" {
  $st = New-QuietStatus
  $st.RateLimit = [pscustomobject]@{ status = "rejected"; resetsAt = 1786926600 }
  $a = Get-DayAnomalies -Status $st
  $v = @($a | Where-Object { $_.code -eq "window" })
  if (-not $v.Count) { return "did not fire" }
  if ($v[0].level -ne "stop") { return "came out as $($v[0].level)" }
  ""
}

Check "an open window fires nothing" {
  $st = New-QuietStatus
  $st.RateLimit = [pscustomobject]@{ status = "allowed"; resetsAt = 1786926600 }
  $a = Get-DayAnomalies -Status $st
  if ($a.Count -ne 0) { return "fired $(($a | ForEach-Object { $_.code }) -join ',')" }
  ""
}

Check "a killed session stops the day" {
  $st = New-QuietStatus
  $st.Killed = $true
  $a = Get-DayAnomalies -Status $st
  $k = @($a | Where-Object { $_.code -eq "killed" })
  if (-not $k.Count) { return "did not fire" }
  if ($k[0].level -ne "stop") { return "came out as $($k[0].level)" }
  ""
}

Check "ending on no_tasks is not an anomaly" {
  $st = New-QuietStatus
  $st.Ended = $true; $st.EndReason = "no_tasks"
  $a = Get-DayAnomalies -Status $st
  if ($a.Count -ne 0) { return "fired $(($a | ForEach-Object { $_.code }) -join ',')" }
  ""
}

Check "ending on anything else is" {
  $st = New-QuietStatus
  $st.Ended = $true; $st.EndReason = "invalid handoff: missing fields: head_sha"
  $a = Get-DayAnomalies -Status $st
  $p = @($a | Where-Object { $_.code -eq "stopped" })
  if (-not $p.Count) { return "did not fire" }
  if ($p[0].level -ne "stop") { return "came out as $($p[0].level)" }
  ""
}

Write-Host ""
Write-Host "  a whole run's stream"

Check "the state comes off the stream and the report off the state" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    New-DayEvent -LogDir $box -Kind "preflight_ok" -Data @{ cycle = 1 } | Out-Null
    New-DayEvent -LogDir $box -Kind "session_started" -Data @{ cycle = 1; role = "worker"; stream = "worker-1.stream.jsonl" } | Out-Null
    New-DayEvent -LogDir $box -Kind "session_ended" -Data @{ cycle = 1; role = "worker"; cost = 4.5; denials = 0; denial_detail = @() } | Out-Null
    New-DayEvent -LogDir $box -Kind "handoff" -Data @{ cycle = 1; outcome = "pr_opened"; task_id = "T1"; pr_number = 36 } | Out-Null
    New-DayEvent -LogDir $box -Kind "session_ended" -Data @{ cycle = 1; role = "audit"; cost = 1.5; denials = 0; denial_detail = @() } | Out-Null
    New-DayEvent -LogDir $box -Kind "verdict" -Data @{ cycle = 1; verdict = "pass" } | Out-Null
    New-DayEvent -LogDir $box -Kind "merged" -Data @{ cycle = 1; pr_number = 36 } | Out-Null
    New-DayEvent -LogDir $box -Kind "day_ended" -Data @{ reason = "no_tasks" } | Out-Null

    $st = Get-DayStatus -LogDir $box
    if (-not $st.Ended) { return "did not see it ended" }
    if ($st.EndReason -ne "no_tasks") { return "the reason came out '$($st.EndReason)'" }
    if ([math]::Abs($st.Cost - 6.0) -gt 0.001) { return "the cost came out $($st.Cost)" }
    if ($st.Cycles.Count -ne 1) { return "counted $($st.Cycles.Count) cycles" }
    if ($st.Cycles[0].verdict -ne "pass") { return "the verdict came out $($st.Cycles[0].verdict)" }
    if ($st.Merged.Count -ne 1) { return "merged came out $($st.Merged.Count)" }
    if ($st.Anomalies.Count -ne 0) { return "a clean day fired $($st.Anomalies.Count)" }

    $p = Write-DayReport -LogDir $box
    if (-not (Test-Path $p)) { return "wrote no report" }
    $md = [System.IO.File]::ReadAllText($p)
    if ($md -notmatch "#36") { return "the report does not name the PR" }
    if ($md -notmatch "6[.,]00 USD") { return "the report does not carry the total" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

Check "a session's denials reach the report from the event" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    New-DayEvent -LogDir $box -Kind "session_ended" -Data @{
      cycle = 1; role = "worker"; cost = 2.0; denials = 2
      denial_detail = @(
        @{ tool = "PowerShell"; command = "python clickup.py lists" },
        @{ tool = "PowerShell"; command = "python clickup.py tasks" })
    } | Out-Null

    $st = Get-DayStatus -LogDir $box
    if ($st.Denials -ne 2) { return "counted $($st.Denials)" }
    $stop = @($st.Anomalies | Where-Object { $_.code -eq "denials" -and $_.level -eq "stop" })
    if (-not $stop.Count) { return "two attempts at one tool did not stop the day" }

    $md = [System.IO.File]::ReadAllText((Write-DayReport -LogDir $box))
    if ($md -notmatch "Permissions denied") { return "the report has no denials section" }
    if ($md -notmatch "clickup") { return "the report does not say what was denied" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

Check "an anomaly that fired survives the state that produced it" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    # Cycle 4 costs triple the median and the rule fires. Cycle 5 is cheap, which moves the median
    # and makes cycle 4 look ordinary -- so without the written event the morning report loses it.
    New-DayEvent -LogDir $box -Kind "anomaly" -Data @{ level = "warn"; code = "cost"; text = "cycle 4 spent 30.00 USD against a median of 2.00" } | Out-Null
    New-DayEvent -LogDir $box -Kind "session_ended" -Data @{ cycle = 5; role = "worker"; cost = 2.0; denials = 0 } | Out-Null
    New-DayEvent -LogDir $box -Kind "day_ended" -Data @{ reason = "no_tasks" } | Out-Null

    $st = Get-DayStatus -LogDir $box
    if (-not @($st.Anomalies | Where-Object { $_.code -eq "cost" }).Count) { return "the anomaly was lost" }
    $md = [System.IO.File]::ReadAllText((Write-DayReport -LogDir $box))
    if ($md -notmatch "cycle 4 spent") { return "the report does not carry it" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

Check "a session that died still reports what it collected" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    New-DayEvent -LogDir $box -Kind "session_started" -Data @{ cycle = 1; role = "worker"; stream = "worker-1.stream.jsonl" } | Out-Null
    New-DayEvent -LogDir $box -Kind "session_killed" -Data @{
      cycle = 1; role = "worker"; reason = "passed 90 min"; denials = 2
      denial_detail = @(
        @{ tool = "PowerShell"; command = "python clickup.py lists" },
        @{ tool = "PowerShell"; command = "python clickup.py tasks" })
    } | Out-Null
    New-DayEvent -LogDir $box -Kind "day_ended" -Data @{ reason = "the worker left no result" } | Out-Null

    $st = Get-DayStatus -LogDir $box
    if ($st.Denials -ne 2) { return "a killed session's denials were thrown away: counted $($st.Denials)" }
    $md = [System.IO.File]::ReadAllText((Write-DayReport -LogDir $box))
    if ($md -notmatch "clickup") { return "the report lost them" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

# Nothing lives as long as a run any more, so the only thing that can tell a day being worked from
# one abandoned half way is how long ago anything happened at all.
Check "a run left half way with nothing happening says so" {
  $box = New-Sandbox
  try {
    $old = (Get-Date).ToUniversalTime().AddMinutes(-45).ToString("o")
    Add-Utf8Line -Path (Get-EventsPath $box) -Text ('{"ts":"' + $old + '","kind":"day_started","repo":"x"}')
    Add-Utf8Line -Path (Get-EventsPath $box) -Text ('{"ts":"' + $old + '","kind":"session_ended","cycle":1,"role":"worker","cost":1.0}')

    $st = Get-DayStatus -LogDir $box
    if ($st.Running) { return "it still reads as running" }
    if ($null -eq $st.IdleForMinutes -or $st.IdleForMinutes -lt 40) { return "idle came out as $($st.IdleForMinutes)" }
    $v = @($st.Anomalies | Where-Object { $_.code -eq "abandoned" })
    if (-not $v.Count) { return "no anomaly for a day left without an ending" }
    if ($v[0].level -ne "stop") { return "came out as $($v[0].level)" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

Check "a run that just did something is not abandoned" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    New-DayEvent -LogDir $box -Kind "session_ended" -Data @{ cycle = 1; role = "worker"; cost = 1.0 } | Out-Null
    $st = Get-DayStatus -LogDir $box
    if (@($st.Anomalies | Where-Object { $_.code -eq "abandoned" }).Count) { return "called a live day abandoned" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

# A long session is not an idle day: the stream goes quiet for as long as the session runs, and what
# is watching it there is the silence rule over the session's own file.
Check "a session that has been running a while is not read as abandoned" {
  $box = New-Sandbox
  try {
    $old = (Get-Date).ToUniversalTime().AddMinutes(-45).ToString("o")
    $s = Join-Path $box "worker-1.stream.jsonl"
    [System.IO.File]::WriteAllLines($s, @('{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"dotnet test"}}]}}'))
    Add-Utf8Line -Path (Get-EventsPath $box) -Text ('{"ts":"' + $old + '","kind":"day_started","repo":"x"}')
    Add-Utf8Line -Path (Get-EventsPath $box) -Text ('{"ts":"' + $old + '","kind":"session_started","cycle":1,"role":"worker","stream":"worker-1.stream.jsonl"}')

    $st = Get-DayStatus -LogDir $box
    if (-not $st.Running) { return "a live session reads as dead" }
    if (@($st.Anomalies | Where-Object { $_.code -eq "abandoned" }).Count) { return "a working session was called abandoned" }
    if ($st.LastTool -notmatch "dotnet test") { return "it cannot say what the session is doing: '$($st.LastTool)'" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

Check "a list of PRs survives the round trip through the event" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    $prs = @()
    $prs += [pscustomobject]@{ number = 36; title = "Ancla estable"; branch = "feat/ancla" }
    $prs += [pscustomobject]@{ number = 37; title = "Otra cosa"; branch = "feat/otra" }
    New-DayEvent -LogDir $box -Kind "open_prs" -Data @{ prs = $prs } | Out-Null
    New-DayEvent -LogDir $box -Kind "day_ended" -Data @{ reason = "no_tasks" } | Out-Null

    $md = [System.IO.File]::ReadAllText((Write-DayReport -LogDir $box))
    if ($md -notmatch "#36 Ancla estable") { return "the PR lost its number or its title" }
    if ($md -notmatch "#37") { return "only the first one came back" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

Check "a half-written line is skipped instead of breaking the read" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    Add-Utf8Line -Path (Get-EventsPath $box) -Text '{"ts":"2026-08-16T22:00:0'
    New-DayEvent -LogDir $box -Kind "day_ended" -Data @{ reason = "no_tasks" } | Out-Null
    $ev = Read-DayEvents $box
    if ($ev.Count -ne 2) { return "read $($ev.Count) events instead of 2" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

Check "the stream does not start with a BOM" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    $bytes = [System.IO.File]::ReadAllBytes((Get-EventsPath $box))
    if ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { return "starts with a BOM" }
    if ([char]$bytes[0] -ne '{') { return "starts with '$([char]$bytes[0])'" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host ""
Write-Host "  the day asks rather than ending"

# One well-formed question, reused: two options, exactly one of which confirms.
$QJson = @'
[{ "id": "q1", "question": "Which way should a position be counted?", "why": "the other way takes the diff down",
   "options": [{ "label": "Keep what was built", "effect": "confirm" },
               { "label": "Open the device natively", "effect": "reject" }] }]
'@
$Questions = @($QJson | ConvertFrom-Json)

function New-Answers([string]$Body) { return @($Body | ConvertFrom-Json) }

Check "answers hold when every question comes back naming an option that was offered" {
  $a = New-Answers '[{"id":"q1","label":"Keep what was built","notes":"yes"}]'
  $err = Test-DayAnswers -Answers $a -Questions $Questions
  if ($err -ne "") { return "refused a good answer: $err" }
  ""
}

# The hole three independent reviewers found on 2026-08-17. While an answer carried its own effect,
# one mistyped field merged the diff the person had just turned down, and nothing could tell.
Check "an answer cannot carry a meaning the option it names does not have" {
  $a = New-Answers '[{"id":"q1","label":"Open the device natively","effect":"confirm"}]'
  $err = Test-DayAnswers -Answers $a -Questions $Questions
  if ($err -ne "") { return "refused a good answer: $err" }
  $e = Get-AnswerEffect -Question $Questions[0] -Label "Open the device natively"
  if ($e -ne "reject") { return "the effect came out '$e' -- a stray confirm in the answer was obeyed" }
  ""
}

Check "an option that was never offered is refused" {
  $a = New-Answers '[{"id":"q1","label":"Do something else"}]'
  if ((Test-DayAnswers -Answers $a -Questions $Questions) -eq "") { return "took a label nobody offered" }
  ""
}

# Answering the first question and stopping would otherwise merge on a decision nobody was asked.
Check "a question left unanswered is refused rather than taken as agreement" {
  $two = @($Questions[0], ('{"id":"q2","question":"And the scope?","options":[{"label":"Keep","effect":"confirm"},{"label":"Cut","effect":"reject"}]}' | ConvertFrom-Json))
  $a = New-Answers '[{"id":"q1","label":"Keep what was built"}]'
  $err = Test-DayAnswers -Answers $a -Questions $two
  if ($err -eq "") { return "took a partial set" }
  if ($err -notmatch "q2") { return "did not name the missing one: $err" }
  ""
}

Check "an answer to a question nobody asked, or two answers to one, are refused" {
  $a = New-Answers '[{"id":"q1","label":"Keep what was built"},{"id":"q9","label":"x"}]'
  if ((Test-DayAnswers -Answers $a -Questions $Questions) -eq "") { return "took an answer to q9" }
  $b = New-Answers '[{"id":"q1","label":"Keep what was built"},{"id":"q1","label":"Open the device natively"}]'
  if ((Test-DayAnswers -Answers $b -Questions $Questions) -eq "") { return "took two answers to q1" }
  ""
}

Check "an answer naming no option, and nothing at all, are refused" {
  $a = New-Answers '[{"id":"q1","label":""}]'
  if ((Test-DayAnswers -Answers $a -Questions $Questions) -eq "") { return "took an answer with no label" }
  if ((Test-DayAnswers -Answers $null -Questions $Questions) -eq "") { return "took nothing as an answer" }
  ""
}

Write-Host ""
Write-Host "  a question the day cannot put to anybody is not waited on"

Check "a well-formed question passes and an empty set does not" {
  if ((Test-DayQuestions $Questions) -ne "") { return "refused a good question" }
  if ((Test-DayQuestions @()) -eq "") { return "took a verdict that asks nothing" }
  ""
}

# Each of these used to reach the wait loop, where nobody could answer it and nothing timed out.
Check "a question with no text, no id, one option or five is refused" {
  $cases = @(
    '[{"id":"q1","question":"","options":[{"label":"a","effect":"confirm"},{"label":"b","effect":"reject"}]}]',
    '[{"id":"","question":"why","options":[{"label":"a","effect":"confirm"},{"label":"b","effect":"reject"}]}]',
    '[{"id":"q1","question":"why","options":[{"label":"a","effect":"confirm"}]}]',
    '[{"id":"q1","question":"why","options":[{"label":"a","effect":"confirm"},{"label":"b","effect":"reject"},{"label":"c","effect":"reject"},{"label":"d","effect":"reject"},{"label":"e","effect":"reject"}]}]'
  )
  foreach ($c in $cases) {
    if ((Test-DayQuestions (@($c | ConvertFrom-Json))) -eq "") { return "took: $c" }
  }
  ""
}

Check "two questions sharing an id are refused" {
  $q = @('[{"id":"q1","question":"a","options":[{"label":"x","effect":"confirm"},{"label":"y","effect":"reject"}]},
          {"id":"q1","question":"b","options":[{"label":"x","effect":"confirm"},{"label":"y","effect":"reject"}]}]' | ConvertFrom-Json)
  if ((Test-DayQuestions $q) -eq "") { return "took two questions called q1" }
  ""
}

# None and two are both unanswerable: with none the PR can never merge whatever is picked, and with
# two the script would have to weigh which agreement counted, which is a judgement it does not make.
Check "a question with no confirming option, or two, is refused" {
  $none = @('[{"id":"q1","question":"a","options":[{"label":"x","effect":"reject"},{"label":"y","effect":"reject"}]}]' | ConvertFrom-Json)
  if ((Test-DayQuestions $none) -eq "") { return "took a question nothing could confirm" }
  $two = @('[{"id":"q1","question":"a","options":[{"label":"x","effect":"confirm"},{"label":"y","effect":"confirm"}]}]' | ConvertFrom-Json)
  if ((Test-DayQuestions $two) -eq "") { return "took a question with two confirms" }
  ""
}

Check "an option effect outside the two the script acts on is refused" {
  $q = @('[{"id":"q1","question":"a","options":[{"label":"x","effect":"confirm"},{"label":"y","effect":"maybe"}]}]' | ConvertFrom-Json)
  if ((Test-DayQuestions $q) -eq "") { return "took effect 'maybe'" }
  ""
}

Write-Host ""
Write-Host "  what the executor does about a verdict"

Check "ask is a verdict the contract allows and something else still is not" {
  $v = '{"verdict":"ask","questions":[]}' | ConvertFrom-Json
  $err = Test-DayContract -Contract $v -Required @("verdict") -Field "verdict" -Allowed @("pass","pass_with_followup","ask","hold")
  if ($err -ne "") { return "refused ask: $err" }
  $bad = '{"verdict":"maybe"}' | ConvertFrom-Json
  if ((Test-DayContract -Contract $bad -Required @("verdict") -Field "verdict" -Allowed @("pass","pass_with_followup","ask","hold")) -eq "") {
    return "took a verdict outside the contract"
  }
  ""
}

# The decisive routing, asked of the same function the loop asks. Nothing here may come out `merge`
# unless the audit said the diff holds up and had nothing left to ask.
Check "a green verdict merges and a hold puts it back, without ending anything" {
  foreach ($name in @("pass", "pass_with_followup")) {
    $v = "{`"verdict`":`"$name`",`"questions`":[]}" | ConvertFrom-Json
    $a = Resolve-Verdict $v
    if ($a.action -ne "merge") { return "$name came out '$($a.action)'" }
  }
  $h = '{"verdict":"hold","questions":[]}' | ConvertFrom-Json
  if ((Resolve-Verdict $h).action -ne "recover") { return "hold did not recover" }
  ""
}

Check "an ask with usable questions waits, and one without them recovers instead" {
  $ok = [pscustomobject]@{ verdict = "ask"; questions = $Questions }
  if ((Resolve-Verdict $ok).action -ne "ask") { return "a good ask did not ask" }
  foreach ($bad in @('{"verdict":"ask","questions":[]}', '{"verdict":"ask","questions":[{"id":"q1"}]}')) {
    $a = Resolve-Verdict ($bad | ConvertFrom-Json)
    if ($a.action -ne "recover") { return "$bad came out '$($a.action)' rather than recovering" }
  }
  ""
}

# An audit that says `pass` and attaches a question has said two things. Merging on the field and
# dropping the question would put a diff its own reader was still asking about into main.
Check "a verdict that contradicts itself does not merge" {
  $v = [pscustomobject]@{ verdict = "pass"; questions = $Questions }
  $a = Resolve-Verdict $v
  if ($a.action -eq "merge") { return "merged a pass that was still asking" }
  if ($a.action -ne "recover") { return "came out '$($a.action)'" }
  ""
}

Write-Host ""
Write-Host "  where a card goes when its PR is not merged"

Check "the first time back is the pool and the second is a person" {
  $none = @()
  if ((Get-CardDestination -Recovered $none -TaskId "T1") -ne "Open") { return "the first went to pending" }
  $once = @([pscustomobject]@{ task = "T1"; moved = $true })
  if ((Get-CardDestination -Recovered $once -TaskId "T1") -ne "pending") { return "the second went back to the pool" }
  if ((Get-CardDestination -Recovered $once -TaskId "T2") -ne "Open") { return "another card was charged T1's strike" }
  ""
}

# A board CLI that could not be reached left the card where it was. Counting that as a strike would
# send the next attempt to `pending` over a failure of the tool rather than of the work.
Check "a recovery that never reached the board is not counted against the card" {
  $failed = @([pscustomobject]@{ task = "T1"; moved = $false })
  if ((Get-CardDestination -Recovered $failed -TaskId "T1") -ne "Open") { return "a failed move counted as a strike" }
  ""
}

Check "a question asked leaves the day waiting and its answer clears it" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    New-DayEvent -LogDir $box -Kind "question_asked" -Data @{
      cycle = 1; id = "q1"; question = "Which way should a position be counted?"; why = "the other way takes the diff down"
      options = @(@{ label = "Keep"; effect = "confirm" }); pr_number = 40; task_id = "T1"
    } | Out-Null

    $st = Get-DayStatus -LogDir $box
    if (-not $st.Waiting) { return "did not read as waiting" }
    if ($st.Questions.Count -ne 1) { return "held $($st.Questions.Count) questions" }

    New-DayEvent -LogDir $box -Kind "answered" -Data @{
      cycle = 1; id = "q1"; question = "Which way should a position be counted?"
      label = "Keep"; effect = "confirm"; notes = ""
    } | Out-Null

    $st2 = Get-DayStatus -LogDir $box
    if ($st2.Waiting) { return "still waiting after the answer" }
    if ($st2.Answered.Count -ne 1) { return "kept $($st2.Answered.Count) answers" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

# The whole point of the change. Waiting is loud and it is not a fault: halting on it would undo
# the recovery it exists for, and a level nobody obeys is what made `stop` decoration before.
Check "waiting is reported and does not stop the day" {
  $st = New-QuietStatus
  $st.Questions = @([pscustomobject]@{ id = "q1"; cycle = 2; question = "Cut the scope?" })
  $st.Waiting = $true
  $an = @(Get-DayAnomalies -Status $st)
  if (-not @($an | Where-Object { $_.code -eq "waiting" }).Count) { return "waiting did not fire" }
  if (Test-WouldHalt $st) { return "a question stopped the day" }
  ""
}

# A question nobody came back to is the shape of a day left half way, and both facts are worth
# having: the question is still open, and nothing is going to close it on its own.
Check "a question nobody answered leaves the day both waiting and abandoned" {
  $box = New-Sandbox
  try {
    $old = (Get-Date).ToUniversalTime().AddMinutes(-45).ToString("o")
    Add-Utf8Line -Path (Get-EventsPath $box) -Text ('{"ts":"' + $old + '","kind":"day_started","repo":"x"}')
    Add-Utf8Line -Path (Get-EventsPath $box) -Text ('{"ts":"' + $old + '","kind":"question_asked","cycle":1,"id":"q1","question":"Cut the scope?","why":"w","options":[]}')

    $st = Get-DayStatus -LogDir $box
    if (-not $st.Waiting) { return "the question stopped being open" }
    if (-not @($st.Anomalies | Where-Object { $_.code -eq "abandoned" }).Count) { return "abandoned did not fire" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

Check "what a person decided and what came back unmerged reach the report" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    New-DayEvent -LogDir $box -Kind "verdict" -Data @{ cycle = 1; verdict = "ask" } | Out-Null
    New-DayEvent -LogDir $box -Kind "answered" -Data @{
      cycle = 1; id = "q1"; question = "Which way should a position be counted?"
      label = "Open the device natively"; effect = "reject"; notes = "the counter is not worth keeping"
    } | Out-Null
    New-DayEvent -LogDir $box -Kind "recovered" -Data @{
      cycle = 1; task_id = "86ak1byrr"; pr_number = 40; to = "Open"; reason = "you turned down the decision this PR rests on"
    } | Out-Null
    New-DayEvent -LogDir $box -Kind "day_ended" -Data @{ reason = "no_tasks" } | Out-Null

    $md = [System.IO.File]::ReadAllText((Write-DayReport -LogDir $box))
    if ($md -notmatch "What you decided") { return "the report has no decisions section" }
    if ($md -notmatch "Open the device natively") { return "the report does not say what was picked" }
    if ($md -notmatch "the counter is not worth keeping") { return "the report drops what they typed" }
    if ($md -notmatch "Put back rather than merged") { return "the report has no recovery section" }
    if ($md -notmatch "86ak1byrr") { return "the report does not name the card that went back" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

# The executor releases the lock in `finally`, so a day that ended is not sitting on anything. Left
# standing, this sent a supervisor off to write an answers file no process would ever read.
Check "a day that ended is not still asking for an answer" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    New-DayEvent -LogDir $box -Kind "question_asked" -Data @{
      cycle = 1; id = "q1"; question = "Cut the scope?"; why = "w"; options = @()
    } | Out-Null
    New-DayEvent -LogDir $box -Kind "day_ended" -Data @{ reason = "the executor threw" } | Out-Null

    $st = Get-DayStatus -LogDir $box
    if ($st.Waiting) { return "an ended day still says it is waiting" }
    if ($st.Questions.Count -ne 0) { return "it still holds $($st.Questions.Count) question(s)" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

# The card is still in `in review`, where no worker looks for it. Reporting that as a recovery is
# how the mechanism written to rescue the work would have quietly abandoned it.
Check "a recovery whose card never moved reads as needing a hand" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    New-DayEvent -LogDir $box -Kind "recovered" -Data @{
      cycle = 1; task_id = "T1"; pr_number = 40; to = "Open"; reason = "the audit held it"; moved = $false
    } | Out-Null
    New-DayEvent -LogDir $box -Kind "day_ended" -Data @{ reason = "no_tasks" } | Out-Null

    $md = [System.IO.File]::ReadAllText((Write-DayReport -LogDir $box))
    if ($md -notmatch "did not move") { return "the report calls a failed move a recovery" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

Check "a day still waiting says so at the top of its own report" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    New-DayEvent -LogDir $box -Kind "question_asked" -Data @{
      cycle = 1; id = "q1"; question = "Cut the scope?"; why = "w"; options = @(); task_id = "T1"
    } | Out-Null

    $md = [System.IO.File]::ReadAllText((Write-DayReport -LogDir $box))
    if ($md -notmatch "Still waiting on you") { return "the report does not say it is waiting" }
    if ($md -notmatch "Cut the scope") { return "the report does not carry the question" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host ""
Write-Host "  a question with no PR behind it"

# A worker asks before it has built anything, so its options say what to build and cannot carry an
# effect. Holding them to the audit's shape would refuse every one of them.
Check "a question with no effects is well formed, and the audit's shape still is not" {
  $plain = @('[{"id":"q1","question":"Which one?","why":"w","options":[{"label":"a"},{"label":"b"}]}]' | ConvertFrom-Json)
  if ((Test-DayAsk $plain) -ne "") { return "refused a question that asks before building" }
  if ((Test-DayQuestions $plain) -eq "") { return "took a verdict question with nothing that confirms" }
  ""
}

Check "the shape rules hold for a question with no effects too" {
  $cases = @(
    '[{"id":"q1","question":"","options":[{"label":"a"},{"label":"b"}]}]',
    '[{"id":"","question":"why","options":[{"label":"a"},{"label":"b"}]}]',
    '[{"id":"q1","question":"why","options":[{"label":"a"}]}]',
    '[{"id":"q1","question":"why","options":[{"label":"a"},{"label":"a"}]}]'
  )
  foreach ($c in $cases) {
    if ((Test-DayAsk (@($c | ConvertFrom-Json))) -eq "") { return "took: $c" }
  }
  ""
}

Write-Host ""
Write-Host "  the journal"

function New-Repo {
  $p = New-Sandbox
  New-Item -ItemType Directory -Force (Join-Path $p ".scratch") | Out-Null
  return $p
}

Check "a fresh journal has its headings and counts as empty" {
  $repo = New-Repo
  try {
    Reset-Journal -Repo $repo | Out-Null
    $p = Get-JournalPath $repo
    if (-not (Test-Path $p)) { return "nothing was laid down" }
    if ((Get-Content $p -Raw) -notmatch "Tried and discarded") { return "the headings are not there" }
    if ((Test-JournalBody $p) -eq "") { return "an empty journal passed as written" }
    ""
  } finally { Remove-Item $repo -Recurse -Force -ErrorAction SilentlyContinue }
}

Check "a journal with prose under a heading counts as written" {
  $repo = New-Repo
  try {
    Reset-Journal -Repo $repo | Out-Null
    $p = Get-JournalPath $repo
    Add-Utf8Line -Path $p -Text "Tried the resampler first and it drifted."
    if ((Test-JournalBody $p) -ne "") { return "prose under a heading did not count" }
    ""
  } finally { Remove-Item $repo -Recurse -Force -ErrorAction SilentlyContinue }
}

# Only a merge archives. Everything else -- a hold, a question, a session that died -- falls through
# to parked, which is what makes a cycle that never closed come out right with nothing written for it.
Check "a merge archives and anything else parks" {
  $repo = New-Repo
  try {
    Reset-Journal -Repo $repo | Out-Null
    Add-Utf8Line -Path (Get-JournalPath $repo) -Text "what I threw away"
    $filed = Complete-Journal -Repo $repo -Merged -TaskId "T1"
    if ($filed -notmatch "archive") { return "a merge did not archive: $filed" }
    if (Test-Path (Get-JournalPath $repo)) { return "the journal was left where it was" }

    Reset-Journal -Repo $repo | Out-Null
    Add-Utf8Line -Path (Get-JournalPath $repo) -Text "how far I got"
    $parked = Complete-Journal -Repo $repo -TaskId "T2"
    if ($parked -notmatch "parked") { return "the default was not parked: $parked" }
    if ($parked -notmatch "T2") { return "it was not filed under its card" }
    ""
  } finally { Remove-Item $repo -Recurse -Force -ErrorAction SilentlyContinue }
}

Check "the card a journal belongs to is read off it when nobody says" {
  $repo = New-Repo
  try {
    Reset-Journal -Repo $repo | Out-Null
    $p = Get-JournalPath $repo
    [System.IO.File]::WriteAllText($p, "# 86ak1byrr - something`n`n## Where I got to`n`nhalf way`n")
    if ((Get-JournalTask $p) -ne "86ak1byrr") { return "read '$(Get-JournalTask $p)'" }
    if ((Complete-Journal -Repo $repo) -notmatch "86ak1byrr") { return "it was not filed under the card it names" }
    ""
  } finally { Remove-Item $repo -Recurse -Force -ErrorAction SilentlyContinue }
}

# A folder of empty files is how a real one stops being noticed.
Check "an empty journal is not filed anywhere" {
  $repo = New-Repo
  try {
    Reset-Journal -Repo $repo | Out-Null
    if ((Complete-Journal -Repo $repo -TaskId "T3") -ne "") { return "an empty journal was filed" }
    if (Test-Path (Join-Path $repo ".scratch\parked\T3.md")) { return "an empty file reached parked" }
    ""
  } finally { Remove-Item $repo -Recurse -Force -ErrorAction SilentlyContinue }
}

# The case this exists for: a cycle that died between its session and its close left a journal
# nothing filed, and the next worker's fresh one would have written straight over it.
Check "laying down a fresh journal parks whatever a dead cycle left" {
  $repo = New-Repo
  try {
    Reset-Journal -Repo $repo | Out-Null
    [System.IO.File]::WriteAllText((Get-JournalPath $repo), "# T9 - x`n`n## Where I got to`n`nnearly there`n")
    $parked = Reset-Journal -Repo $repo
    if ($parked -notmatch "T9") { return "the old one was overwritten instead of parked" }
    if ((Get-Content $parked -Raw) -notmatch "nearly there") { return "what it said was lost" }
    if ((Test-JournalBody (Get-JournalPath $repo)) -eq "") { return "the new one is not empty" }
    ""
  } finally { Remove-Item $repo -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host ""
Write-Host "  one day at a time"

Check "a day open elsewhere keeps a second one out, and its own atoms in" {
  $orch = New-Sandbox
  try {
    $mine = Join-Path $orch "log\2026-08-17_090000"
    New-Item -ItemType Directory -Force $mine | Out-Null
    if ((Enter-DayLock -OrchestratorDir $orch -LogDir $mine) -ne "") { return "could not take a free lock" }
    if ((Enter-DayLock -OrchestratorDir $orch -LogDir $mine) -ne "") { return "the day that holds it was locked out of itself" }

    $other = Join-Path $orch "log\2026-08-17_120000"
    New-Item -ItemType Directory -Force $other | Out-Null
    if ((Enter-DayLock -OrchestratorDir $orch -LogDir $other) -eq "") { return "a second day took a held lock" }
    ""
  } finally { Remove-Item $orch -Recurse -Force -ErrorAction SilentlyContinue }
}

# Nothing releases the lock when a day is killed outright, so a claim nobody has touched in longer
# than a session can legally take has to stop being a claim on its own.
Check "a lock nobody has touched for hours is not a lock" {
  $orch = New-Sandbox
  try {
    $stale = [pscustomobject]@{ run = "2026-08-17_000000"; dir = "$orch\log\2026-08-17_000000"
                                ts = (Get-Date).ToUniversalTime().AddMinutes(-400).ToString("o") }
    [System.IO.File]::WriteAllText((Get-LockPath $orch), ($stale | ConvertTo-Json -Compress))
    $mine = Join-Path $orch "log\2026-08-17_090000"
    New-Item -ItemType Directory -Force $mine | Out-Null
    if ((Enter-DayLock -OrchestratorDir $orch -LogDir $mine) -ne "") { return "a stale claim kept the day out" }
    ""
  } finally { Remove-Item $orch -Recurse -Force -ErrorAction SilentlyContinue }
}

# What lets every atom take no arguments at all.
Check "the run and the cycle are read off the lock and the stream" {
  $orch = New-Sandbox
  try {
    $mine = Join-Path $orch "log\2026-08-17_090000"
    New-Item -ItemType Directory -Force $mine | Out-Null
    Enter-DayLock -OrchestratorDir $orch -LogDir $mine | Out-Null
    if ((Get-CurrentRun -OrchestratorDir $orch) -ne $mine) { return "the open day could not be found" }

    if ((Get-CurrentCycle -LogDir $mine) -ne 0) { return "a day with no session is not on cycle 0" }
    New-DayEvent -LogDir $mine -Kind "session_started" -Data @{ cycle = 1; role = "worker" } | Out-Null
    New-DayEvent -LogDir $mine -Kind "session_started" -Data @{ cycle = 1; role = "audit" } | Out-Null
    if ((Get-CurrentCycle -LogDir $mine) -ne 1) { return "the audit opened a cycle of its own" }
    New-DayEvent -LogDir $mine -Kind "session_started" -Data @{ cycle = 2; role = "worker" } | Out-Null
    if ((Get-CurrentCycle -LogDir $mine) -ne 2) { return "the second worker did not open cycle 2" }

    Exit-DayLock -OrchestratorDir $orch
    if ((Get-CurrentRun -OrchestratorDir $orch) -ne "") { return "a released lock still names a day" }
    ""
  } finally { Remove-Item $orch -Recurse -Force -ErrorAction SilentlyContinue }
}

Check "the lock is taken atomically, and released only by whoever holds it" {
  $orch = New-Sandbox
  try {
    $a = Join-Path $orch "log\2026-08-17_090000"
    $b = Join-Path $orch "log\2026-08-17_090001"
    New-Item -ItemType Directory -Force $a | Out-Null
    New-Item -ItemType Directory -Force $b | Out-Null
    Enter-DayLock -OrchestratorDir $orch -LogDir $a | Out-Null

    # B never took it, so B may not drop it. Unconditional release let a day that had just lost a
    # race take the lock away from the one that legitimately held it.
    Exit-DayLock -OrchestratorDir $orch -LogDir $b
    if (-not (Test-Path (Get-LockPath $orch))) { return "a stranger released the lock" }
    if ((Enter-DayLock -OrchestratorDir $orch -LogDir $b) -eq "") { return "and then took it" }

    Exit-DayLock -OrchestratorDir $orch -LogDir $a
    if (Test-Path (Get-LockPath $orch)) { return "the holder could not release it" }
    ""
  } finally { Remove-Item $orch -Recurse -Force -ErrorAction SilentlyContinue }
}

# A day stopped to ask is not idle, however long it has been: nothing runs while it waits, so its
# claim ages exactly like an abandoned one and the checkout would be taken out from under it.
Check "a day waiting on a person keeps the lock however stale it looks" {
  $orch = New-Sandbox
  try {
    $held = Join-Path $orch "log\2026-08-17_000000"
    New-Item -ItemType Directory -Force $held | Out-Null
    New-DayEvent -LogDir $held -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    New-DayEvent -LogDir $held -Kind "question_asked" -Data @{
      cycle = 1; id = "q1"; question = "Cut the scope?"; why = "w"; options = @()
    } | Out-Null
    $stale = [pscustomobject]@{ run = "2026-08-17_000000"; dir = $held
                                ts = (Get-Date).ToUniversalTime().AddMinutes(-400).ToString("o") }
    [System.IO.File]::WriteAllText((Get-LockPath $orch), ($stale | ConvertTo-Json -Compress))

    $mine = Join-Path $orch "log\2026-08-17_090000"
    New-Item -ItemType Directory -Force $mine | Out-Null
    $refused = Enter-DayLock -OrchestratorDir $orch -LogDir $mine
    if ($refused -eq "") { return "the checkout was taken from a day still waiting for an answer" }
    if ($refused -notmatch "waiting") { return "refused for the wrong reason: $refused" }
    ""
  } finally { Remove-Item $orch -Recurse -Force -ErrorAction SilentlyContinue }
}

# What sequences the atoms is a model, so an atom run twice is not a hypothetical. Closing a cycle
# twice recorded two recoveries, and a card's destination is counted off those -- so the duplicate
# sent it to `pending` as though two sessions had failed to land it.
Check "a cycle already acted on is recognised rather than acted on again" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    if (Test-CycleEvent -LogDir $box -Cycle 1 -Kinds @("merged","recovered")) { return "an untouched cycle read as closed" }
    New-DayEvent -LogDir $box -Kind "recovered" -Data @{ cycle = 1; task_id = "T1"; to = "Open"; moved = $true } | Out-Null
    if (-not (Test-CycleEvent -LogDir $box -Cycle 1 -Kinds @("merged","recovered"))) { return "a closed cycle read as open" }
    if (Test-CycleEvent -LogDir $box -Cycle 2 -Kinds @("merged","recovered")) { return "cycle 2 was closed by cycle 1's event" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

# An atom killed mid-session left the stream saying a session started and nothing saying it stopped,
# so the day read as working for as long as anybody looked -- and `abandoned`, which only fires when
# nothing is running, never got its turn.
Check "a session whose atom is gone is not running, and the day reads as abandoned" {
  $box = New-Sandbox
  try {
    $old = (Get-Date).ToUniversalTime().AddMinutes(-45).ToString("o")
    $s = Join-Path $box "worker-1.stream.jsonl"
    [System.IO.File]::WriteAllLines($s, @('{"type":"assistant","message":{"content":[]}}'))
    Add-Utf8Line -Path (Get-EventsPath $box) -Text ('{"ts":"' + $old + '","kind":"day_started","repo":"x"}')
    # A PID that cannot be running: Windows never assigns this one.
    Add-Utf8Line -Path (Get-EventsPath $box) -Text ('{"ts":"' + $old + '","kind":"session_started","cycle":1,"role":"worker","stream":"worker-1.stream.jsonl","pid":999999}')

    $st = Get-DayStatus -LogDir $box
    if ($st.Running) { return "a session whose atom is gone still reads as running" }
    if (-not @($st.Anomalies | Where-Object { $_.code -eq "abandoned" }).Count) { return "abandoned did not fire" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

Check "a live atom's session still reads as running" {
  $box = New-Sandbox
  try {
    $s = Join-Path $box "worker-1.stream.jsonl"
    [System.IO.File]::WriteAllLines($s, @('{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"dotnet test"}}]}}'))
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    New-DayEvent -LogDir $box -Kind "session_started" -Data @{ cycle = 1; role = "worker"; stream = "worker-1.stream.jsonl"; pid = $PID } | Out-Null
    $st = Get-DayStatus -LogDir $box
    if (-not $st.Running) { return "a live session reads as dead" }
    if ($st.LastTool -notmatch "dotnet test") { return "it cannot say what the session is doing" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

# A card can come back more than once, and each attempt knows something the last did not.
Check "parking a card twice keeps both attempts" {
  $repo = New-Repo
  try {
    Reset-Journal -Repo $repo | Out-Null
    [System.IO.File]::WriteAllText((Get-JournalPath $repo), "# T7 - x`n`n## Where I got to`n`nfirst attempt`n")
    Complete-Journal -Repo $repo | Out-Null

    Reset-Journal -Repo $repo | Out-Null
    [System.IO.File]::WriteAllText((Get-JournalPath $repo), "# T7 - x`n`n## Where I got to`n`nsecond attempt`n")
    $parked = Complete-Journal -Repo $repo

    $body = Get-Content $parked -Raw
    if ($body -notmatch "first attempt") { return "the earlier attempt was overwritten" }
    if ($body -notmatch "second attempt") { return "the later attempt was not kept" }
    ""
  } finally { Remove-Item $repo -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host ""
if ($script:Failed -eq 0) {
  Write-Host ("  $($script:Ran) checks, all green") -ForegroundColor Green
  Write-Host ""
  exit 0
}
Write-Host ("  $($script:Ran) checks, $($script:Failed) red") -ForegroundColor Red
Write-Host ""
exit 1
