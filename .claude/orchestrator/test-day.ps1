#requires -Version 5.1
<#
  The probe over the day's engine. It runs in CI and spends no session: everything checked here is
  the part that needs no `claude` running - pulling the contract out of what a session emitted,
  reading a result off a stream, and what each rule fires on.

  What is not covered here is covered by running the day, and docs/orchestrator.md says so.

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
    Killed = $false; RateLimit = $null; Ended = $false; EndReason = ""
    ProcessGone = $false; ExecutorPid = $null; Past = @(); Anomalies = @()
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

Check "a run whose process is gone is not still running" {
  $box = New-Sandbox
  try {
    # A PID that cannot exist: Windows PIDs are multiples of 4 and this is the reserved idle slot's
    # neighbour, never assigned.
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x"; pid = 999999 } | Out-Null
    New-DayEvent -LogDir $box -Kind "session_started" -Data @{ cycle = 1; role = "worker"; stream = "worker-1.stream.jsonl" } | Out-Null

    $st = Get-DayStatus -LogDir $box
    if ($st.Running) { return "it still reads as running" }
    if (-not $st.ProcessGone) { return "it did not notice the process is gone" }
    $v = @($st.Anomalies | Where-Object { $_.code -eq "vanished" })
    if (-not $v.Count) { return "no anomaly for a day that died without an ending" }
    if ($v[0].level -ne "stop") { return "came out as $($v[0].level)" }
    ""
  } finally { Remove-Item $box -Recurse -Force -ErrorAction SilentlyContinue }
}

Check "a live run with its process alive still reads as running" {
  $box = New-Sandbox
  try {
    New-DayEvent -LogDir $box -Kind "day_started" -Data @{ repo = "x"; pid = $PID } | Out-Null
    $s = Join-Path $box "worker-1.stream.jsonl"
    [System.IO.File]::WriteAllLines($s, @('{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"dotnet test"}}]}}'))
    New-DayEvent -LogDir $box -Kind "session_started" -Data @{ cycle = 1; role = "worker"; stream = "worker-1.stream.jsonl" } | Out-Null

    $st = Get-DayStatus -LogDir $box
    if (-not $st.Running) { return "a live day reads as dead" }
    if ($st.ProcessGone) { return "it thinks the process is gone" }
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
if ($script:Failed -eq 0) {
  Write-Host ("  $($script:Ran) checks, all green") -ForegroundColor Green
  Write-Host ""
  exit 0
}
Write-Host ("  $($script:Ran) checks, $($script:Failed) red") -ForegroundColor Red
Write-Host ""
exit 1
