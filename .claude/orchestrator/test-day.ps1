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
  }
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

Check "the ones on the result group by tool" {
  $r = '{"permission_denials":[
    {"tool_name":"PowerShell","tool_input":{"command":"$env:X = 1; python a.py"}},
    {"tool_name":"PowerShell","tool_input":{"command":"python a.py --another-way"}},
    {"tool_name":"Bash","tool_input":{"command":"cat ~/.codex/config"}}]}' | ConvertFrom-Json
  $d = Get-ResultDenials $r
  if ($d.Count -ne 3) { return "counted $($d.Count)" }
  $g = Group-Denials $d
  if ($g.Count -ne 2) { return "grouped into $($g.Count) tools" }
  $ps = $g | Where-Object { $_.tool -eq "PowerShell" }
  if ($ps.count -ne 2) { return "PowerShell came out with $($ps.count)" }
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
