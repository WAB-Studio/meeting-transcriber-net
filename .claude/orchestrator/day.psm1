#requires -Version 5.1
<#
  The engine behind the day: the event stream, the contracts sessions speak in, and the rules that
  decide what is abnormal. `run-day.ps1` writes through it, `day-status.ps1` reads through it, and
  `test-day.ps1` proves the parts that do not need a session to run.

  One source of truth per run: `events.jsonl`. `day.log` and `report.md` are renders of it, never
  the original, so a reader added later cannot tell a different story than the one the log tells.

  Nothing here launches a session or judges code. The thresholds are thresholds: what they mean,
  and whether one is worth interrupting somebody over, belongs to the skill that reads them.

  ASCII only, and no accented text. Windows PowerShell reads a .ps1 without a BOM as ANSI, so a
  dash or an accent in a comment is a parse error on the machine this actually runs on.
#>

# No StrictMode on purpose: objects out of ConvertFrom-Json do not carry the properties an older
# event never had, and reading an absent one has to give $null rather than throw.

$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# ---------------------------------------------------------------------------------------------
# The stream
# ---------------------------------------------------------------------------------------------

# Add-Content -Encoding utf8 on 5.1 puts a BOM on the file it creates, and a BOM on the first line
# of an NDJSON file breaks every reader that is not PowerShell.
function Add-Utf8Line {
  param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Text)
  [System.IO.File]::AppendAllText($Path, $Text + "`n", $script:Utf8NoBom)
}

function Get-EventsPath { param([Parameter(Mandatory)][string]$LogDir) return (Join-Path $LogDir "events.jsonl") }

<#
  One event per transition. `kind` is the only field a reader interprets; everything else travels
  as it comes, so adding a field does not oblige anybody to change this side.
#>
function New-DayEvent {
  param(
    [Parameter(Mandatory)][string]$LogDir,
    [Parameter(Mandatory)][string]$Kind,
    [hashtable]$Data
  )
  $o = [ordered]@{ ts = (Get-Date).ToUniversalTime().ToString("o"); kind = $Kind }
  if ($Data) { foreach ($k in $Data.Keys) { $o[$k] = $Data[$k] } }
  $line = [pscustomobject]$o | ConvertTo-Json -Depth 8 -Compress
  Add-Utf8Line -Path (Get-EventsPath $LogDir) -Text $line
  return $line
}

<#
  A half-written line is skipped rather than thrown over: the reader runs while the executor
  writes, and a torn read cannot be an error of the reader's.
#>
function Read-DayEvents {
  param([Parameter(Mandatory)][string]$LogDir)
  $p = Get-EventsPath $LogDir
  if (-not (Test-Path $p)) { return @() }
  $out = New-Object System.Collections.ArrayList
  foreach ($line in [System.IO.File]::ReadAllLines($p)) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    try { $null = $out.Add(($line | ConvertFrom-Json)) } catch { }
  }
  return $out.ToArray()
}

# ---------------------------------------------------------------------------------------------
# What a session emitted
# ---------------------------------------------------------------------------------------------

<#
  With `--output-format stream-json` the result is one LINE of the file, not the file. Parsing the
  whole content throws, and the day would stop on every successful cycle -- that is the trap this
  function exists for. Scanning from the end is only because that is where the line is; what makes
  it the right line is its own `type`, not its position, so a result quoted inside somebody else's
  tool_result is not mistaken for this session's.
#>
function Get-SessionResult {
  param([Parameter(Mandatory)][string]$StreamPath)
  if (-not (Test-Path $StreamPath)) { return $null }
  $lines = [System.IO.File]::ReadAllLines($StreamPath)
  for ($i = $lines.Count - 1; $i -ge 0; $i--) {
    $l = $lines[$i]
    if ([string]::IsNullOrWhiteSpace($l)) { continue }
    if ($l.IndexOf('"type":"result"') -lt 0) { continue }
    try { $o = $l | ConvertFrom-Json } catch { continue }
    if ($o.type -eq "result") { return $o }
  }
  return $null
}

<#
  The handoff and the verdict are derived from what the session emitted, never from a file the
  session has to remember to write. On 2026-08-16 a worker said the whole handoff as its last
  message, did not write it, and the day died with the work done and the PR open.

  Three attempts, most specific first: the last ```json block, the whole text, and first { to
  last }.
#>
function Get-ContractFromText {
  param([string]$Text)
  if ([string]::IsNullOrWhiteSpace($Text)) { return $null }

  $candidates = New-Object System.Collections.ArrayList

  $fence = $Text.LastIndexOf('```json')
  if ($fence -ge 0) {
    $start = $fence + 7
    $end = $Text.IndexOf('```', $start)
    if ($end -gt $start) { $null = $candidates.Add($Text.Substring($start, $end - $start)) }
  }

  $null = $candidates.Add($Text)

  $first = $Text.IndexOf('{')
  $last = $Text.LastIndexOf('}')
  if ($first -ge 0 -and $last -gt $first) { $null = $candidates.Add($Text.Substring($first, $last - $first + 1)) }

  foreach ($c in $candidates) {
    $t = $c.Trim()
    if (-not $t.StartsWith('{')) { continue }
    try {
      $o = $t | ConvertFrom-Json
      if ($o -is [pscustomobject]) { return $o }
    } catch { }
  }
  return $null
}

<#
  Presence is not enough: in PowerShell the string "false" is truthy, so a verdict that said "hold"
  alongside a lying continue flag would have carried the day on. What the script obeys is validated
  by value.
#>
function Test-DayContract {
  param(
    $Contract,
    [Parameter(Mandatory)][string[]]$Required,
    [Parameter(Mandatory)][string]$Field,
    [Parameter(Mandatory)][string[]]$Allowed
  )
  if ($null -eq $Contract) { return "the session emitted no JSON that can be read" }

  $missing = @()
  foreach ($k in $Required) { if ($null -eq $Contract.$k) { $missing += $k } }
  if ($missing.Count -gt 0) { return "missing fields: " + ($missing -join ", ") }

  if ($Allowed -notcontains [string]$Contract.$Field) {
    return "$Field='$($Contract.$Field)' is not a value in the contract"
  }
  return ""
}

<#
  A denial is counted per tool and per what it tried, never as a number: the number says something
  happened, the list says which permission rule is missing. It is the difference between "there
  were 27" and "the board CLI was denied all session".
#>
function New-Denial {
  param([string]$Tool, [string]$Command)
  $cmd = ([string]$Command -replace '\s+', ' ').Trim()
  if ($cmd.Length -gt 120) { $cmd = $cmd.Substring(0, 120) + "..." }
  return [pscustomobject]@{ tool = $Tool; command = $cmd }
}

<#
  A finished session's denials come counted, with their input, in the result. Nothing has to be
  reconstructed from the stream.
#>
function Get-ResultDenials {
  param($Result)
  $out = New-Object System.Collections.ArrayList
  foreach ($d in @($Result.permission_denials)) {
    if (-not $d) { continue }
    $arg = @($d.tool_input.command, $d.tool_input.file_path, $d.tool_input.description) |
           Where-Object { $_ } | Select-Object -First 1
    $null = $out.Add((New-Denial -Tool ([string]$d.tool_name) -Command ([string]$arg)))
  }
  return $out.ToArray()
}

<#
  Grouped by tool: three denied attempts at the same tool are not three errors, they are a model
  groping for a way around a permission it does not have.
#>
function Group-Denials {
  param($Denials)
  $g = @{}
  foreach ($d in @($Denials)) {
    if (-not $d) { continue }
    $k = [string]$d.tool
    if (-not $g.ContainsKey($k)) { $g[$k] = New-Object System.Collections.ArrayList }
    $null = $g[$k].Add($d.command)
  }
  $out = New-Object System.Collections.ArrayList
  foreach ($k in @($g.Keys | Sort-Object)) {
    $null = $out.Add([pscustomobject]@{
      tool     = $k
      count    = $g[$k].Count
      commands = @($g[$k] | Select-Object -Unique)
    })
  }
  return $out.ToArray()
}

<#
  What a session is doing right now. Silence is measured from the file's timestamp rather than one
  parsed out of it: every arriving line touches the file, so nothing has to be parsed to know that
  nothing has arrived for twenty minutes.
#>
function Get-SessionActivity {
  param([Parameter(Mandatory)][string]$StreamPath)

  $a = [pscustomobject]@{
    Exists       = $false
    LastWrite    = $null
    QuietFor     = $null
    ToolCalls    = 0
    LastTool     = ""
    LastText     = ""
    Denials      = 0
    DenialDetail = @()
    RateLimit    = $null
  }
  if (-not (Test-Path $StreamPath)) { return $a }

  $a.Exists = $true
  $a.LastWrite = (Get-Item $StreamPath).LastWriteTime
  $a.QuietFor = (Get-Date) - $a.LastWrite

  # A tool_result only says it failed; what was attempted is in the tool_use that asked for it,
  # several lines above and tied to it by id.
  $asked = @{}

  foreach ($line in [System.IO.File]::ReadAllLines($StreamPath)) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    try { $o = $line | ConvertFrom-Json } catch { continue }

    if ($o.type -eq "rate_limit_event" -and $o.rate_limit_info) { $a.RateLimit = $o.rate_limit_info }

    if ($o.type -eq "assistant" -and $o.message -and $o.message.content) {
      foreach ($b in @($o.message.content)) {
        if ($b.type -eq "tool_use") {
          $a.ToolCalls++
          $arg = @($b.input.command, $b.input.file_path, $b.input.pattern, $b.input.description) |
                 Where-Object { $_ } | Select-Object -First 1
          $a.LastTool = ("{0} {1}" -f $b.name, ([string]$arg -replace '\s+', ' ')).Trim()
          if ($b.id) { $asked[[string]$b.id] = [pscustomobject]@{ tool = [string]$b.name; arg = [string]$arg } }
        } elseif ($b.type -eq "text" -and -not [string]::IsNullOrWhiteSpace($b.text)) {
          $a.LastText = (([string]$b.text) -split "`n" | Where-Object { $_.Trim() } | Select-Object -First 1)
        }
      }
    }

    # Under -p a missing permission does not prompt: it denies, and the session carries on as if
    # the tool did not exist. The damage is not the denial, it is what it does next, which is the
    # same job by a worse route with nobody told. While it runs this is the only place it shows;
    # once it ends, the result carries them counted.
    if ($o.type -eq "user" -and $o.message -and $o.message.content) {
      foreach ($b in @($o.message.content)) {
        if ($b.type -eq "tool_result" -and $b.is_error) {
          $c = [string]$b.content
          if ($c -match 'requested permissions|haven''t granted|permission to use|not allowed to use') {
            $a.Denials++
            $src = $null
            if ($b.tool_use_id -and $asked.ContainsKey([string]$b.tool_use_id)) { $src = $asked[[string]$b.tool_use_id] }
            if ($src) { $a.DenialDetail += (New-Denial -Tool $src.tool -Command $src.arg) }
            else      { $a.DenialDetail += (New-Denial -Tool "?" -Command $c) }
          }
        }
      }
    }
  }
  return $a
}

# ---------------------------------------------------------------------------------------------
# The rules
# ---------------------------------------------------------------------------------------------

$script:DefaultRules = @{
  SilenceMinutes = 15    # a session emitting nothing for this long is stuck, not thinking
  DenialGrope    = 2      # two denied attempts at one tool is already groping for a way around
  CostFactor     = 3.0   # a cycle spending three times the median is in a loop
  CostSamples    = 3     # with fewer cycles there is no median worth having
}

function Get-DayRules { return $script:DefaultRules.Clone() }

function New-Anomaly {
  param([string]$Level, [string]$Code, [string]$Text)
  return [pscustomobject]@{ level = $Level; code = $Code; text = $Text }
}

<#
  Thresholds, not judgement. Says what is out of range; what it means, and whether it is worth
  interrupting somebody over, is decided by whoever reads this.
#>
function Get-DayAnomalies {
  param(
    [Parameter(Mandatory)]$Status,
    [hashtable]$Rules
  )
  if (-not $Rules) { $Rules = Get-DayRules }
  $out = New-Object System.Collections.ArrayList

  if ($Status.Running -and $null -ne $Status.QuietForMinutes -and
      $Status.QuietForMinutes -ge $Rules.SilenceMinutes) {
    $null = $out.Add((New-Anomaly "warn" "silence" (
      "the {0} session of cycle {1} has emitted nothing for {2:N0} min" -f $Status.Role, $Status.Cycle, $Status.QuietForMinutes)))
  }

  # The loudest rule here, deliberately. A denial under -p does not stop the session: it sends it
  # to do the same job another way, worse, and tells nobody. On 2026-08-16 there were 27 in one
  # cycle, the board CLI among them, and the cross-model review the repo requires never ran.
  if ($Status.Denials -ge 1) {
    $groping = @($Status.DenialsByTool | Where-Object { $_.count -ge $Rules.DenialGrope })
    if ($groping.Count -gt 0) {
      foreach ($g in $groping) {
        $null = $out.Add((New-Anomaly "stop" "denials" (
          "{0} denied attempts at {1}: it is not missing a tool, it is groping for a way around a permission, and whatever it does instead will be worse and unseen. The rule that is missing has to cover: {2}" -f `
            $g.count, $g.tool, ($g.commands | Select-Object -First 1))))
      }
    } else {
      foreach ($g in @($Status.DenialsByTool)) {
        $null = $out.Add((New-Anomaly "warn" "denials" (
          "{0} denied; under -p that does not prompt, it denies, and the session carries on as if the tool did not exist: {1}" -f `
            $g.tool, ($g.commands | Select-Object -First 1))))
      }
    }
  }

  if ($Status.CycleCosts.Count -ge $Rules.CostSamples -and $null -ne $Status.LastCycleCost) {
    $med = Get-Median $Status.CycleCosts
    if ($med -gt 0 -and $Status.LastCycleCost -gt ($med * $Rules.CostFactor)) {
      $null = $out.Add((New-Anomaly "warn" "cost" (
        "cycle {0} spent {1:N2} USD against a median of {2:N2}" -f $Status.Cycle, $Status.LastCycleCost, $med)))
    }
  }

  if ($Status.Killed) {
    $null = $out.Add((New-Anomaly "stop" "killed" "a session passed the executor's clock and was killed"))
  }

  if ($Status.RateLimit -and $Status.RateLimit.status -and $Status.RateLimit.status -ne "allowed") {
    $reset = ""
    if ($Status.RateLimit.resetsAt) {
      $reset = " (back at " + ([DateTimeOffset]::FromUnixTimeSeconds([int64]$Status.RateLimit.resetsAt)).LocalDateTime.ToString("HH:mm") + ")"
    }
    $null = $out.Add((New-Anomaly "stop" "window" ("the usage window says '{0}'{1}" -f $Status.RateLimit.status, $reset)))
  }

  if ($Status.Ended -and $Status.EndReason -and $Status.EndReason -ne "no_tasks") {
    $null = $out.Add((New-Anomaly "stop" "stopped" ("the day ended on: {0}" -f $Status.EndReason)))
  }

  return $out.ToArray()
}

function Get-Median {
  param([double[]]$Values)
  if (-not $Values -or $Values.Count -eq 0) { return 0 }
  $s = @($Values | Sort-Object)
  $n = $s.Count
  if ($n % 2 -eq 1) { return [double]$s[[int](($n - 1) / 2)] }
  return ([double]$s[$n / 2 - 1] + [double]$s[$n / 2]) / 2
}

# ---------------------------------------------------------------------------------------------
# The state
# ---------------------------------------------------------------------------------------------

function Find-LatestRun {
  param([Parameter(Mandatory)][string]$OrchestratorDir)
  $logs = Join-Path $OrchestratorDir "log"
  if (-not (Test-Path $logs)) { return $null }
  $d = Get-ChildItem $logs -Directory -ErrorAction SilentlyContinue |
       Where-Object { Test-Path (Get-EventsPath $_.FullName) } |
       Sort-Object Name | Select-Object -Last 1
  if ($d) { return $d.FullName }
  return $null
}

<#
  What the day is doing, read off the stream and nothing else. It works the same over a finished
  run: what changes is that `Running` is false and `EndReason` says why.
#>
function Get-DayStatus {
  param(
    [Parameter(Mandatory)][string]$LogDir,
    [hashtable]$Rules
  )
  $ev = Read-DayEvents $LogDir

  $st = [pscustomobject]@{
    LogDir          = $LogDir
    Started         = $null
    Ended           = $false
    EndReason       = ""
    Cycle           = 0
    Role            = ""
    Running         = $false
    RunningSince    = $null
    QuietForMinutes = $null
    LastTool        = ""
    LastText        = ""
    ToolCalls       = 0
    Denials         = 0
    DenialDetail    = @()
    DenialsByTool   = @()
    Killed          = $false
    RateLimit       = $null
    Cost            = 0.0
    LastCycleCost   = $null
    CycleCosts      = @()
    Cycles          = @()
    Merged          = @()
    Anomalies       = @()
  }

  $cycles = @{}
  $openStream = $null

  foreach ($e in $ev) {
    switch ($e.kind) {
      "day_started" { $st.Started = $e.ts }
      "day_ended"   { $st.Ended = $true; $st.EndReason = [string]$e.reason }
      "session_started" {
        $st.Cycle = [int]$e.cycle
        $st.Role = [string]$e.role
        $st.Running = $true
        $st.RunningSince = $e.ts
        $openStream = [string]$e.stream
      }
      "session_ended" {
        $st.Running = $false
        $openStream = $null
        if ($null -ne $e.cost) { $st.Cost += [double]$e.cost }
        $c = [int]$e.cycle
        if (-not $cycles.ContainsKey($c)) { $cycles[$c] = [ordered]@{ cycle = $c; cost = 0.0 } }
        $cycles[$c].cost += [double]$e.cost
      }
      "session_killed" { $st.Killed = $true; $st.Running = $false; $openStream = $null }
      "session_failed" { $st.Running = $false; $openStream = $null }
      "handoff" {
        $c = [int]$e.cycle
        if (-not $cycles.ContainsKey($c)) { $cycles[$c] = [ordered]@{ cycle = $c; cost = 0.0 } }
        $cycles[$c].outcome = [string]$e.outcome
        $cycles[$c].task = [string]$e.task_id
        $cycles[$c].pr = $e.pr_number
      }
      "verdict" {
        $c = [int]$e.cycle
        if (-not $cycles.ContainsKey($c)) { $cycles[$c] = [ordered]@{ cycle = $c; cost = 0.0 } }
        $cycles[$c].verdict = [string]$e.verdict
      }
      "merged" { $st.Merged += $e.pr_number }
    }
  }

  # What only shows by looking at the live session's own file.
  if ($st.Running -and $openStream) {
    $path = $openStream
    if (-not [System.IO.Path]::IsPathRooted($path)) { $path = Join-Path $LogDir $openStream }
    $a = Get-SessionActivity $path
    if ($a.Exists) {
      $st.QuietForMinutes = [math]::Round($a.QuietFor.TotalMinutes, 1)
      $st.LastTool = $a.LastTool
      $st.LastText = $a.LastText
      $st.ToolCalls = $a.ToolCalls
      $st.Denials += $a.Denials
      $st.DenialDetail += @($a.DenialDetail)
      if ($a.RateLimit) { $st.RateLimit = $a.RateLimit }
    }
  }

  # A finished session's denials arrive counted and with the command it tried, so the executor
  # leaves them on the event and nothing has to be rebuilt from the stream here.
  foreach ($e in $ev) {
    if ($e.kind -ne "session_ended") { continue }
    if ($null -ne $e.denials) { $st.Denials += [int]$e.denials }
    if ($e.denial_detail) { $st.DenialDetail += @($e.denial_detail) }
    if ($e.rate_limit) { $st.RateLimit = $e.rate_limit }
  }

  $st.DenialsByTool = Group-Denials $st.DenialDetail

  $ordered = @($cycles.Keys | Sort-Object)
  $st.Cycles = @($ordered | ForEach-Object { [pscustomobject]$cycles[$_] })
  $st.CycleCosts = @($st.Cycles | Where-Object { $_.cost -gt 0 } | ForEach-Object { [double]$_.cost })
  if ($st.Cycles.Count -gt 0) { $st.LastCycleCost = [double]($st.Cycles[-1].cost) }

  $st.Anomalies = Get-DayAnomalies -Status $st -Rules $Rules
  return $st
}

# ---------------------------------------------------------------------------------------------
# The renders
# ---------------------------------------------------------------------------------------------

<#
  What gets read in the morning when nobody was watching. It comes off the same stream as
  everything else, so it cannot tell a different story than the reader told live.
#>
function Write-DayReport {
  param([Parameter(Mandatory)][string]$LogDir)

  $st = Get-DayStatus -LogDir $LogDir
  $ev = Read-DayEvents $LogDir
  $L = New-Object System.Collections.ArrayList

  $null = $L.Add("# The day " + (Split-Path -Leaf $LogDir))
  $null = $L.Add("")
  $state = "running"
  if ($st.Ended) { $state = "ended - " + $st.EndReason }
  $null = $L.Add(("{0} cycle(s) | {1:N2} USD | {2}" -f $st.Cycles.Count, $st.Cost, $state))
  $null = $L.Add("")

  if ($st.Cycles.Count -gt 0) {
    $null = $L.Add("| Cycle | Task | PR | Handoff | Verdict | USD |")
    $null = $L.Add("| --- | --- | --- | --- | --- | --- |")
    foreach ($c in $st.Cycles) {
      $task = "-"; if ($c.task) { $task = $c.task }
      $pr = "-"; if ($c.pr) { $pr = "#" + $c.pr }
      $outcome = "-"; if ($c.outcome) { $outcome = $c.outcome }
      $verdict = "-"; if ($c.verdict) { $verdict = $c.verdict }
      $null = $L.Add(("| {0} | {1} | {2} | {3} | {4} | {5:N2} |" -f $c.cycle, $task, $pr, $outcome, $verdict, $c.cost))
    }
    $null = $L.Add("")
  }

  if ($st.Merged.Count -gt 0) {
    $null = $L.Add("Merged into main: " + (($st.Merged | ForEach-Object { "#$_" }) -join ", "))
    $null = $L.Add("")
  }

  # This section comes before everything else that went wrong because it is the only part of this
  # file anybody acts on by editing one line: each row is a permission rule that is missing.
  if ($st.DenialsByTool.Count -gt 0) {
    $null = $L.Add("## Permissions denied")
    $null = $L.Add("")
    $null = $L.Add("Under ``-p`` a denial does not prompt: it denies, and the session tries something worse without saying so.")
    $null = $L.Add("")
    $null = $L.Add("| Tool | Times | What it tried |")
    $null = $L.Add("| --- | --- | --- |")
    foreach ($g in $st.DenialsByTool) {
      foreach ($c in $g.commands) {
        $null = $L.Add(("| {0} | {1} | ``{2}`` |" -f $g.tool, $g.count, ($c -replace '\|', ' ')))
      }
    }
    $null = $L.Add("")
  }

  $null = $L.Add("## Anomalies")
  $null = $L.Add("")
  if ($st.Anomalies.Count -eq 0) {
    $null = $L.Add("No rule fired.")
  } else {
    foreach ($a in $st.Anomalies) { $null = $L.Add(("- **{0}** ({1}) - {2}" -f $a.code, $a.level, $a.text)) }
  }
  $null = $L.Add("")

  $bad = @($ev | Where-Object { $_.kind -match 'invalid|failed|killed' })
  if ($bad.Count -gt 0) {
    $null = $L.Add("## What went wrong")
    $null = $L.Add("")
    foreach ($f in $bad) { $null = $L.Add(("- ``{0}`` - {1}" -f $f.kind, ([string]$f.reason))) }
    $null = $L.Add("")
  }

  $path = Join-Path $LogDir "report.md"
  [System.IO.File]::WriteAllText($path, ($L -join "`n") + "`n", $script:Utf8NoBom)
  return $path
}

Export-ModuleMember -Function Add-Utf8Line, Get-EventsPath, New-DayEvent, Read-DayEvents,
  Get-SessionResult, Get-ContractFromText, Test-DayContract, Get-SessionActivity,
  New-Denial, Get-ResultDenials, Group-Denials,
  Get-DayRules, Get-DayAnomalies, Get-Median, Find-LatestRun, Get-DayStatus, Write-DayReport
