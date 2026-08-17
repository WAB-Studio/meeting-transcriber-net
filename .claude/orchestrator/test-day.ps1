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
# atom.psm1, which re-exports day.psm1 whole: importing both leaves the second `-Force` pulling the
# first out from under this script, and the lease this probes belongs to the atom side.
Import-Module (Join-Path $PSScriptRoot "atom.psm1") -Force -DisableNameChecking

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
    Owed = @(); Recovered = @()
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


# One well-formed question, reused: two options, exactly one of which confirms.
$OwedJson = @'
[{ "what": "Which way should a position be counted?", "why": "the other way takes the diff down",
   "options": ["Keep what was built", "Open the device natively"] }]
'@
$Owed = @($OwedJson | ConvertFrom-Json)

Write-Host ""
Write-Host "  a decision nobody here can make"

Check "a decision that names itself is readable, and an empty set is not" {
  if ((Test-DecisionsOwed $Owed) -ne "") { return "refused a usable decision" }
  if ((Test-DecisionsOwed @()) -eq "") { return "took a verdict that owes nothing" }
  ""
}

# Each of these reaches a card and is read cold by whoever grills it. One that does not say what it
# is sends them back to the diff, which is the rediscovery this exists to prevent.
Check "a decision with no `what`, or one lone option, is refused" {
  $cases = @(
    '[{"why":"w","options":["a","b"]}]',
    '[{"what":"","options":["a","b"]}]',
    '[{"what":"which way?","options":["only one"]}]',
    '[{"what":"which way?","options":["a",""]}]'
  )
  foreach ($c in $cases) {
    if ((Test-DecisionsOwed (@($c | ConvertFrom-Json))) -eq "") { return "took: $c" }
  }
  ""
}

# The audit knows more than the worker and says more, but neither is obliged past `what`.
Check "a decision with nothing but what it is still passes" {
  if ((Test-DecisionsOwed (@('[{"what":"which way should a position be counted?"}]' | ConvertFrom-Json))) -ne "") {
    return "refused a worker that named the fork and no options"
  }
  ""
}

Write-Host ""
Write-Host "  what the day does about a verdict"

Check "ask is a verdict the contract allows and something else still is not" {
  $v = '{"verdict":"ask","decisions_owed":[]}' | ConvertFrom-Json
  $err = Test-DayContract -Contract $v -Required @("verdict") -Field "verdict" -Allowed @("pass","pass_with_followup","ask","hold")
  if ($err -ne "") { return "refused ask: $err" }
  $bad = '{"verdict":"maybe"}' | ConvertFrom-Json
  if ((Test-DayContract -Contract $bad -Required @("verdict") -Field "verdict" -Allowed @("pass","pass_with_followup","ask","hold")) -eq "") {
    return "took a verdict outside the contract"
  }
  ""
}

# Nothing here waits for anybody, which is the whole change: an `ask` parks the card in `pending`
# and the day takes the next task.
Check "a green verdict merges, a hold puts it back, and an ask parks it" {
  $pass = [pscustomobject]@{ verdict = "pass"; decisions_owed = @() }
  if ((Resolve-Verdict $pass).action -ne "merge") { return "a pass did not merge" }

  $hold = [pscustomobject]@{ verdict = "hold"; decisions_owed = @() }
  $h = Resolve-Verdict $hold
  if ($h.action -ne "recover") { return "a hold did not recover" }
  if ($h.to -ne "") { return "a hold picked a destination instead of counting the card's strikes" }

  $ask = [pscustomobject]@{ verdict = "ask"; decisions_owed = $Owed }
  $a = Resolve-Verdict $ask
  if ($a.action -ne "recover") { return "an ask did not put the card anywhere" }
  if ($a.to -ne "pending") { return "an ask sent the card to '$($a.to)' rather than pending" }
  ""
}

# `Open` is where a worker looks, and the one thing that must not happen to this card is another
# session building on the decision nobody has made.
Check "an ask never sends the card back to the pool" {
  foreach ($n in 1..3) {
    $a = Resolve-Verdict ([pscustomobject]@{ verdict = "ask"; decisions_owed = $Owed })
    if ($a.to -eq "Open") { return "an ask reached the pool" }
  }
  ""
}

Check "an ask nobody could act on recovers instead of parking" {
  foreach ($bad in @('{"verdict":"ask","decisions_owed":[]}', '{"verdict":"ask","decisions_owed":[{"why":"w"}]}')) {
    $r = Resolve-Verdict ($bad | ConvertFrom-Json)
    if ($r.action -ne "recover") { return "$bad did not recover" }
    if ($r.to -eq "pending") { return "$bad parked a card carrying nothing anybody could read" }
  }
  ""
}

Check "a verdict that contradicts itself does not merge" {
  $v = [pscustomobject]@{ verdict = "pass"; decisions_owed = $Owed }
  $r = Resolve-Verdict $v
  if ($r.action -eq "merge") { return "a pass owing a decision merged" }
  ""
}

# @($null).Count is 1 in PowerShell, so a verdict that simply left the field out read as owing
# exactly one decision, and a clean pass never merged.
Check "a verdict with no decisions_owed field at all still merges" {
  $v = '{"verdict":"pass"}' | ConvertFrom-Json
  if ((Resolve-Verdict $v).action -ne "merge") { return "an absent field counted as a decision owed" }
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




Check "what came back unmerged reaches the report" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    New-DayEvent -LogDir $box -Kind "verdict" -Data @{ cycle = 1; verdict = "hold" } | Out-Null
    New-DayEvent -LogDir $box -Kind "recovered" -Data @{
      cycle = 1; task_id = "86ak1byrr"; pr_number = 40; to = "Open"
      reason = "the audit held it"; moved = $true
    } | Out-Null
    New-DayEvent -LogDir $box -Kind "day_ended" -Data @{ reason = "no_tasks" } | Out-Null

    $md = [System.IO.File]::ReadAllText((Write-DayReport -LogDir $box))
    if ($md -notmatch "Put back rather than merged") { return "the report has no recovery section" }
    if ($md -notmatch "86ak1byrr") { return "the report does not name the card that went back" }
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


Check "a decision owed reaches the report with the card and the PR" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    New-DayEvent -LogDir $box -Kind "verdict" -Data @{ cycle = 1; verdict = "ask" } | Out-Null
    New-DayEvent -LogDir $box -Kind "decision_owed" -Data @{
      cycle = 1; task_id = "T1"; pr_number = 40; moved = $true
      decisions = @(@{ what = "Which way should a position be counted?"; why = "the other way takes the diff down" })
    } | Out-Null
    New-DayEvent -LogDir $box -Kind "day_ended" -Data @{ reason = "no_tasks" } | Out-Null

    $st = Get-DayStatus -LogDir $box
    if ($st.Owed.Count -ne 1) { return "the state holds $($st.Owed.Count) parked cards" }

    $md = [System.IO.File]::ReadAllText((Write-DayReport -LogDir $box))
    if ($md -notmatch "Parked on a decision") { return "the report does not name them" }
    if ($md -notmatch "Which way should a position be counted") { return "the report lost the decision" }
    if ($md -notmatch "#40") { return "the report lost the PR that is held up" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

# The one line in that section somebody has to act on: the day meant to park the card and could not,
# so it is still in `in review` and the PR looks abandoned rather than owed.
Check "a parked card that never moved reads as needing a hand" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    # The comment landed and the retag did not, which is the state that hides best: the card looks
    # handled and no worker or grill will ever find it.
    New-DayEvent -LogDir $box -Kind "decision_owed" -Data @{
      cycle = 1; task_id = "T1"; pr_number = 40; said = $true; retagged = $false; moved = $false
      decisions = @(@{ what = "Which way?" })
    } | Out-Null
    New-DayEvent -LogDir $box -Kind "day_ended" -Data @{ reason = "no_tasks" } | Out-Null
    $md = [System.IO.File]::ReadAllText((Write-DayReport -LogDir $box))
    if ($md -notmatch "was not parked") { return "the report calls a failed park a park" }
    if ($md -notmatch "still reads as grilled") { return "the report does not say which step failed" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
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
# Two contenders that both saw the same stale claim both overwrote it and both reported success.
# Taking one over goes through the same atomic create as a free lock now, so exactly one wins.
Check "only one day can take over a stale lock" {
  $orch = New-Sandbox
  try {
    $dead = Join-Path $orch "log\2026-08-17_000000"
    $stale = [pscustomobject]@{ run = "2026-08-17_000000"; dir = $dead
                                ts = (Get-Date).ToUniversalTime().AddMinutes(-400).ToString("o") }
    [System.IO.File]::WriteAllText((Get-LockPath $orch), ($stale | ConvertTo-Json -Compress))

    $a = Join-Path $orch "log\2026-08-17_090000"
    $b = Join-Path $orch "log\2026-08-17_090001"
    New-Item -ItemType Directory -Force $a | Out-Null
    New-Item -ItemType Directory -Force $b | Out-Null

    if ((Enter-DayLock -OrchestratorDir $orch -LogDir $a) -ne "") { return "the first contender could not take a stale lock" }
    if ((Enter-DayLock -OrchestratorDir $orch -LogDir $b) -eq "") { return "both contenders took the same stale lock" }
    ""
  } finally { Remove-Item $orch -Recurse -Force -ErrorAction SilentlyContinue }
}

# A contender reading while somebody else writes sees an unreadable file, and reading that as an
# empty lock is the same collision arriving by another route.
Check "a lock that cannot be read is not treated as free" {
  $orch = New-Sandbox
  try {
    [System.IO.File]::WriteAllText((Get-LockPath $orch), "{ this is not json")
    $mine = Join-Path $orch "log\2026-08-17_090000"
    New-Item -ItemType Directory -Force $mine | Out-Null
    if ((Enter-DayLock -OrchestratorDir $orch -LogDir $mine) -eq "") { return "an unreadable lock read as free" }
    ""
  } finally { Remove-Item $orch -Recurse -Force -ErrorAction SilentlyContinue }
}

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
# The day lock serialises one day against another. Nothing serialised an atom against another atom
# of the same day, and two workers reading the stream in the same second both saw cycle N, both
# chose N + 1, and shared a checkout, a stream and a card from there. A handle rather than a claim,
# because an atom is one process for its whole life and the OS releases it however that ends.
Check "only one atom of a day runs at a time" {
  $orch = New-Sandbox
  try {
    if ((Enter-AtomLease -OrchestratorDir $orch) -ne "") { return "a free lease could not be taken" }
    $probe = "try { [System.IO.File]::Open('" + (Join-Path $orch "atom.lock") + "', 'OpenOrCreate', 'Write', 'None').Dispose(); 'free' } catch { 'busy' }"
    $second = & powershell.exe -NoProfile -Command $probe
    if ($second -ne "busy") { return "a second atom took the lease while the first held it" }
    ""
  } finally { Remove-Item $orch -Recurse -Force -ErrorAction SilentlyContinue }
}

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


# What sequences the atoms is a model, so an atom run twice is not a hypothetical. Closing a cycle
# twice recorded two recoveries, and a card's destination is counted off those -- so the duplicate
# sent it to `pending` as though two sessions had failed to land it.
# A cycle parked on a decision is finished with, the same as one merged or one put back. Leaving it
# out of the terminal set made the next worker refuse forever with "cycle N is still open" -- the
# no-waiting arrangement failing on its own central case.
Check "a cycle parked on a decision counts as closed" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    New-DayEvent -LogDir $box -Kind "decision_owed" -Data @{ cycle = 1; task_id = "T1"; moved = $true } | Out-Null
    if (-not (Test-CycleClosed -LogDir $box -Cycle 1)) { return "a parked cycle still reads as open" }
    if (Test-CycleClosed -LogDir $box -Cycle 2) { return "it closed the wrong cycle" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

Check "a cycle already acted on is recognised rather than acted on again" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    if (Test-CycleClosed -LogDir $box -Cycle 1) { return "an untouched cycle read as closed" }
    New-DayEvent -LogDir $box -Kind "recovered" -Data @{ cycle = 1; task_id = "T1"; to = "Open"; moved = $true } | Out-Null
    if (-not (Test-CycleClosed -LogDir $box -Cycle 1)) { return "a closed cycle read as open" }
    if (Test-CycleClosed -LogDir $box -Cycle 2) { return "cycle 2 was closed by cycle 1's event" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

# Running the atom again is the only repair a half-landed close has. A guard that read the event
# rather than what the event says happened answered `already closed` over a card still sitting in
# `in review`, and the cycle was finished with while the board had never been told.
Check "a close that never reached the board leaves the cycle open" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x" } | Out-Null
    New-DayEvent -LogDir $box -Kind "recovered" -Data @{ cycle = 1; task_id = "T1"; to = "Open"; said = $true; moved = $false } | Out-Null
    if (Test-CycleClosed -LogDir $box -Cycle 1) { return "a card that never moved closed its cycle" }

    New-DayEvent -LogDir $box -Kind "decision_owed" -Data @{ cycle = 2; task_id = "T2"; said = $true; retagged = $true; moved = $false } | Out-Null
    if (Test-CycleClosed -LogDir $box -Cycle 2) { return "a park that never reached pending closed its cycle" }

    # The retry lands, and now it is closed.
    New-DayEvent -LogDir $box -Kind "recovered" -Data @{ cycle = 1; task_id = "T1"; to = "Open"; said = $true; moved = $true } | Out-Null
    if (-not (Test-CycleClosed -LogDir $box -Cycle 1)) { return "the repair did not close it" }

    # A merge has no board write between deciding it and it being true.
    New-DayEvent -LogDir $box -Kind "merged" -Data @{ cycle = 3; pr_number = 7 } | Out-Null
    if (-not (Test-CycleClosed -LogDir $box -Cycle 3)) { return "a merge did not close its cycle" }
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
Write-Host "  what a recorded step says it did"

# Everything that guards a card on the board reads this one boolean, and it could not be false: what
# a command prints on stdout is part of what a PowerShell function returns, so a board CLI that
# printed why it failed came back as @("error ...", $false) -- two elements, and therefore true.
Check "a step that printed and failed still comes back false" {
  $box = New-Sandbox
  try {
    $day = [pscustomobject]@{ LogDir = $box; Repo = $box }
    $ok = Invoke-BestEffort $day "a step that works" { cmd /c "echo done" }
    if ($ok -isnot [bool]) { return "a step that worked returned $($ok.GetType().Name), not a boolean" }
    if (-not $ok) { return "a step that worked came back false" }

    $bad = Invoke-BestEffort $day "a step that fails loudly" { cmd /c "echo something broke & exit 3" }
    if ($bad -isnot [bool]) { return "a failed step returned $($bad.GetType().Name), not a boolean" }
    if ($bad) { return "a step that failed came back true" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

# One shape for the three ways a cycle closes. Two of them used to answer with a bare string, and
# the comparison that goes with it -- `-ne ""` -- calls anything else a failure.
Check "a close says whether it landed in one shape" {
  $r = New-CloseResult
  if ($r.Lost -ne "") { return "an untroubled close came back with something lost" }
  if ($r.To -ne "")   { return "it invented a destination" }
  $l = New-CloseResult -To "pending" -Lost "the board said no"
  if ($l.Lost -ne "the board said no" -or $l.To -ne "pending") { return "it did not carry what it was given" }
  ""
}

Write-Host ""
Write-Host "  the atoms call things that exist"

<#
  The one check here that reads the code rather than running it, because running it is exactly what
  does not happen: PowerShell resolves a command when the line executes, so a function deleted out
  from under its callers is not a load error, not a parse error, and not visible to any probe that
  does not reach that branch. `Invoke-Merge` and `Invoke-Recover` were deleted with their three
  callers left standing and their names left in the export list; every check stayed green, and what
  it cost was the merge and both recoveries -- two of the three ways a cycle can end.

  Names are taken off the syntax tree rather than a regular expression: a call is a CommandAst and
  nothing else is, so a name inside a string or a comment cannot count as one. Only Verb-Noun names
  are judged, which is what leaves `git`, `gh`, `python` and `dotnet` out of it.
#>
Check "no atom calls a function that does not exist" {
  # Resolved the way the atom will resolve it at run time, not against every name in the folder: an
  # atom sees its own functions, whatever `atom.psm1` exports, and the shell's cmdlets. A helper
  # that lives in a file nobody imports is not in scope, and pooling the whole directory would call
  # that a definition and pass over the same hole this exists to find.
  $mod = Import-Module (Join-Path $PSScriptRoot "atom.psm1") -Force -DisableNameChecking -PassThru
  $exported = @{}
  foreach ($k in $mod.ExportedFunctions.Keys) { $exported[$k] = $true }

  $bad = @()
  foreach ($f in (Get-ChildItem $PSScriptRoot -File | Where-Object { $_.Extension -in @(".ps1", ".psm1") })) {
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$null, [ref]$errors)
    if ($errors -and $errors.Count -gt 0) { return "$($f.Name) does not parse: $($errors[0].Message)" }

    $local = @{}
    foreach ($d in $ast.FindAll({ $args[0] -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)) {
      $local[$d.Name] = $true
    }
    # The modules are each other's scope: day.psm1 is imported whole by atom.psm1, and a name
    # exported from there is what an atom gets. For the two modules the pair is the scope.
    if ($f.Extension -eq ".psm1") {
      $peer = Join-Path $PSScriptRoot $(if ($f.Name -eq "atom.psm1") { "day.psm1" } else { "atom.psm1" })
      $peerAst = [System.Management.Automation.Language.Parser]::ParseFile($peer, [ref]$null, [ref]$null)
      foreach ($d in $peerAst.FindAll({ $args[0] -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)) {
        $local[$d.Name] = $true
      }
    }

    foreach ($c in $ast.FindAll({ $args[0] -is [System.Management.Automation.Language.CommandAst] }, $true)) {
      $name = $c.GetCommandName()
      if (-not $name -or $name -notmatch '^[A-Za-z]+-[A-Za-z]+$') { continue }   # leaves git, gh, python out
      if ($local.ContainsKey($name) -or $exported.ContainsKey($name)) { continue }
      # Cmdlets and aliases only. A function some module on this machine happens to export would
      # make the check pass here and fail on a runner that does not have it.
      if (Get-Command $name -CommandType Cmdlet, Alias -ErrorAction SilentlyContinue) { continue }
      $bad += "$($f.Name) calls $name"
    }
  }
  if ($bad.Count -gt 0) { return ($bad | Sort-Object -Unique) -join "; " }
  ""
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
