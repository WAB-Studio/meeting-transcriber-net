#requires -Version 5.1
<#
  What every atom does the same way: find the day it belongs to, refresh the claim on it, launch a
  session, and say one thing on the way out.

  An atom takes no arguments. Which run and which cycle are read off the lock and the stream, so a
  caller never spells a path -- the layer that decides permissions splits a command on characters
  that live in paths, and a command with one in it is a command that gets refused on the day nobody
  can be asked.

  `day.psm1` is the stream and the rules and launches nothing. This is the launching.

  ASCII only, and no accented text. Windows PowerShell reads a .ps1 without a BOM as ANSI, so a
  dash or an accent in a comment is a parse error on the machine this actually runs on.
#>

<#
  Imported here and re-exported below, so an atom imports this one file and has the whole vocabulary.
  Importing both from the script does not work: `-Force` removes the module before reloading it, and
  the nested reload pulls day.psm1 out from under whoever imported it first -- which came out as
  `New-DayEvent is not recognized` from a script whose second line had just imported it.
#>
$script:DayModule = Import-Module (Join-Path $PSScriptRoot "day.psm1") -Force -DisableNameChecking -PassThru

$script:SessionTimeout = [TimeSpan]::FromMinutes(90)

# The board CLI. An atom acts on the board only for something already decided -- recording a
# decision a person made, putting a card back -- and asks it one thing: whether there is anything
# for a session to do at all. Which card, and whether that card is ready, stays the worker's.
$script:ClickUp = Join-Path $env:USERPROFILE ".claude\skills\clickup\clickup.py"

# The space this repo's board lives in. `docs/layout.md` and arquitectura.md 13 say what the lists
# are; the only thing needed here is which board to ask.
$script:Space = "MeetingTranscriber"

function Get-Repo { return (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) }

<#
  A new day. The only atom that creates anything: the run's folder, the claim on it, and the first
  line of its stream.
#>
function New-DayRun {
  $orch = $PSScriptRoot
  $dir = Join-Path $orch ("log\" + (Get-Date -Format "yyyy-MM-dd_HHmmss"))
  New-Item -ItemType Directory -Force $dir | Out-Null

  $taken = Enter-DayLock -OrchestratorDir $orch -LogDir $dir
  if ($taken -ne "") { return [pscustomobject]@{ LogDir = $dir; Error = $taken } }

  Set-Location (Get-Repo)
  return [pscustomobject]@{ Repo = (Get-Repo); Orch = $orch; LogDir = $dir; Cycle = 0; Error = "" }
}

<#
  Only one atom of a run at a time, held for the atom's whole life by the OS rather than by a
  timestamp. The day lock serialises one day against another; nothing serialised an atom against
  another atom of the same day, and two workers that read the stream in the same second both saw
  cycle N, both chose N + 1, and shared a checkout, a stream and a card from there.

  A handle rather than a claim because here it can be: an atom is one process for its whole
  duration, so the OS releases this however the process dies.
#>
$script:AtomHandle = $null

function Enter-AtomLease {
  param([Parameter(Mandatory)][string]$OrchestratorDir)
  try {
    $script:AtomHandle = [System.IO.File]::Open(
      (Join-Path $OrchestratorDir "atom.lock"), [System.IO.FileMode]::OpenOrCreate,
      [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    return ""
  } catch {
    return "another atom of this day is still running"
  }
}

<#
  The day already open, or nothing. Every atom but the first begins here, and one that finds no day
  refuses rather than starting one: a run nobody opened is a run nothing is watching.
#>
function Open-Day {
  $orch = $PSScriptRoot
  $dir = Get-CurrentRun -OrchestratorDir $orch
  if ($dir -eq "") { return [pscustomobject]@{ Error = "no day is open -- start-day.ps1 opens one" } }
  $busy = Enter-AtomLease -OrchestratorDir $orch
  if ($busy -ne "") { return [pscustomobject]@{ Error = $busy } }
  Update-DayLock -OrchestratorDir $orch -LogDir $dir
  Set-Location (Get-Repo)
  return [pscustomobject]@{
    Repo = (Get-Repo); Orch = $orch; LogDir = $dir
    Cycle = (Get-CurrentCycle -LogDir $dir); Error = ""
  }
}

function Write-Day {
  param([Parameter(Mandatory)]$Day, [Parameter(Mandatory)][string]$Text)
  $line = "{0}  {1}" -f (Get-Date -Format "HH:mm:ss"), $Text
  Write-Host $line
  Add-Utf8Line -Path (Join-Path $Day.LogDir "day.log") -Text $line
}

<#
  The last line an atom prints, and the only one anything downstream acts on. Everything above it is
  for a person scrolling; this is the sentence.
#>
function Write-Atom {
  param([Parameter(Mandatory)][hashtable]$Result)
  Write-Host ("RESULT " + ([pscustomobject]$Result | ConvertTo-Json -Depth 6 -Compress))
}

<#
  An atom that throws still says so. `$ErrorActionPreference` is `Stop` in all of them, so a `git`
  or a `gh` missing from PATH raises rather than returning an exit code, and the raise went past
  every check and out of the script -- leaving whoever is sequencing with no RESULT at all and no
  way to tell a crash from a hang.

  Called from a `trap`, which is the only construct that catches a terminating error raised anywhere
  in a script body without wrapping the whole thing in one indent.
#>
function Write-AtomCrash {
  param([Parameter(Mandatory)][string]$Message, $Day)
  if ($Day -and $Day.LogDir) {
    try {
      New-DayEvent -LogDir $Day.LogDir -Kind "atom_failed" -Data @{ reason = $Message } | Out-Null
    } catch { }
  }
  Write-Host "the atom threw: $Message"
  Write-Atom @{ ok = $false; stop = "an atom threw: $Message" }
}

<#
  A `claude -p` hanging on something external hangs the day, and --max-budget-usd bounds tokens,
  not minutes. Launched with stdin at EOF and a clock over it.

  Two Start-Process traps, both of which fail the FIRST real session and neither of which shows up
  in a dry run. -RedirectStandardInput does not take "NUL" -- it resolves it as a relative path and
  throws -- so stdin comes from a real empty file. And ExitCode stays $null unless .Handle is read
  before waiting, which would make `-ne 0` true on every clean exit.

  The output is stream-json: the file grows while the session runs, which is what makes the day
  watchable at all. The result is the LAST line of that file and never the file.
#>
function Invoke-Session {
  param(
    [Parameter(Mandatory)]$Day,
    [Parameter(Mandatory)][string]$Role,
    [Parameter(Mandatory)][string]$Prompt,
    [Parameter(Mandatory)][int]$Cycle
  )

  $stream = Join-Path $Day.LogDir "$Role-$Cycle.stream.jsonl"
  $stdin = Join-Path $Day.LogDir "empty-stdin"
  New-Item -ItemType File -Force $stdin | Out-Null

  # -ArgumentList joins with a space and quotes nothing, so the prompt -- which holds one -- would
  # arrive as several arguments and the session would read the rest as strays.
  $quoted = $Prompt
  if ($Prompt -match '\s') { $quoted = '"' + $Prompt + '"' }
  $argv = @("-p", $quoted, "--output-format", "stream-json", "--verbose",
            "--permission-mode", "acceptEdits",
            "--settings", ('"' + (Join-Path $Day.Orch "settings.json") + '"'),
            "--fallback-model", "sonnet")

  # The atom's own PID goes on the event. Without it a session reads as running for as long as the
  # stream says it started and nothing says it stopped -- so an atom killed mid-session left the day
  # looking alive forever, which is the one state `abandoned` exists to catch and the one it could
  # not see.
  New-DayEvent -LogDir $Day.LogDir -Kind "session_started" -Data @{
    cycle = $Cycle; role = $Role; stream = (Split-Path -Leaf $stream); prompt = $Prompt; pid = $PID
  } | Out-Null

  # A session that died is where what it collected matters most: 27 denials and then a timeout is a
  # worse day than 27 denials and a clean exit, and reading them only off a healthy result would
  # throw away exactly the ones worth having.
  function Close-Dead([string]$Kind, [string]$Reason) {
    $a = Get-SessionActivity $stream
    New-DayEvent -LogDir $Day.LogDir -Kind $Kind -Data @{
      cycle = $Cycle; role = $Role; reason = $Reason
      denials = $a.Denials; denial_detail = @($a.DenialDetail); rate_limit = $a.RateLimit
    } | Out-Null
  }

  $started = Get-Date
  try {
    $p = Start-Process -FilePath "claude" -ArgumentList $argv -NoNewWindow -PassThru `
                       -RedirectStandardOutput $stream -RedirectStandardInput $stdin
    $null = $p.Handle
  } catch {
    Write-Day $Day "  the session could not be launched: $($_.Exception.Message)"
    Close-Dead "session_failed" "could not be launched: $($_.Exception.Message)"
    return $null
  }

  if (-not $p.WaitForExit($script:SessionTimeout.TotalMilliseconds)) {
    Write-Day $Day "  the session passed $($script:SessionTimeout.TotalMinutes) min -- killed"
    try { $p.Kill() } catch { }
    Close-Dead "session_killed" "passed $($script:SessionTimeout.TotalMinutes) min"
    return $null
  }
  if ($p.ExitCode -ne 0) {
    Write-Day $Day "  claude exited with code $($p.ExitCode)"
    Close-Dead "session_failed" "exit $($p.ExitCode)"
    return $null
  }

  $r = Get-SessionResult $stream
  if ($null -eq $r) {
    Close-Dead "session_failed" "the stream carries no result line"
    return $null
  }

  $denials = @(Get-ResultDenials $r)
  # The usage window is only ever announced mid-stream, so it is carried onto the event or it is
  # lost the moment the session closes.
  $live = Get-SessionActivity $stream
  New-DayEvent -LogDir $Day.LogDir -Kind "session_ended" -Data @{
    cycle = $Cycle; role = $Role; subtype = [string]$r.subtype; is_error = [bool]$r.is_error
    turns = [int]$r.num_turns; cost = [double]$r.total_cost_usd
    seconds = [int]((Get-Date) - $started).TotalSeconds
    denials = $denials.Count; denial_detail = $denials; rate_limit = $live.RateLimit
  } | Out-Null

  # The loudest thing an atom prints, because it is what costs the most without being seen: a denial
  # does not stop the session, it sends it to do the same job by a worse route.
  if ($denials.Count -gt 0) {
    Write-Day $Day "  !! $($denials.Count) permission(s) denied -- whatever it did instead, nobody asked for"
    foreach ($g in (Group-Denials $denials)) {
      Write-Day $Day ("     {0} x{1}: {2}" -f $g.tool, $g.count, ($g.commands | Select-Object -First 1))
    }
  }

  return $r
}

<#
  Whether the session was sound enough to be worth acting on, asked after it ends and before
  anything irreversible. A worker that spent its turns groping around a denied permission never
  reaches the audit, and never reaches `main`.

  The reader says what is out of range and the atom acts on it: labelling a condition `stop` and
  then carrying on would make the level decoration.
#>
function Test-Sound {
  param([Parameter(Mandatory)]$Day)
  $status = Get-DayStatus -LogDir $Day.LogDir
  $halt = @(Get-HaltingAnomalies -Status $status)
  foreach ($a in $halt) {
    Write-Day $Day "  !! [$($a.code)] $($a.text)"
    New-DayEvent -LogDir $Day.LogDir -Kind "anomaly" -Data @{ level = $a.level; code = $a.code; text = $a.text } | Out-Null
  }
  # The ones that do not halt still go on the stream: most cannot be recomputed once the state that
  # produced them is gone, and the morning report promises every one that fired.
  foreach ($a in @($status.Anomalies | Where-Object { $_.level -ne "stop" })) {
    New-DayEvent -LogDir $Day.LogDir -Kind "anomaly" -Data @{ level = $a.level; code = $a.code; text = $a.text } | Out-Null
    Write-Day $Day "  [$($a.code)] $($a.text)"
  }
  if ($halt.Count -eq 0) { return "" }
  return (($halt | ForEach-Object { $_.text }) -join " / ")
}

<#
  Recording what happened must not be able to end the day, and saying so was not enough to make it
  true: `$ErrorActionPreference` is `Stop` in every atom, so a `gh` or a `python` missing from PATH
  throws rather than returning an exit code, and the throw goes past every check to the crash
  handler. So the call is wrapped, and both ways of failing land in the same place.

  Returns whether it worked, because one caller -- the card move -- has to write down which.
#>
function Invoke-BestEffort {
  param([Parameter(Mandatory)]$Day, [Parameter(Mandatory)][string]$What, [Parameter(Mandatory)][scriptblock]$Do)
  $why = ""
  try {
    & $Do
    if ($LASTEXITCODE -ne 0) { $why = "exit $LASTEXITCODE" }
  } catch {
    $why = $_.Exception.Message
  }
  if ($why -eq "") { return $true }
  New-DayEvent -LogDir $Day.LogDir -Kind "record_failed" -Data @{ reason = "$What -- $why" } | Out-Null
  Write-Day $Day "  $What did not go through: $why"
  return $false
}

<#
  Long prose never goes on a command line. The board CLI takes `--text @path` for exactly this: a
  reason worth recording carries a semicolon sooner or later, and whatever parses the command splits
  on it without caring that it sits inside quotes.

  Failing to record is written down and does not stop the day. The stream already holds the fact, so
  what is lost is the copy a person reads on the board.
#>
function Write-Elsewhere {
  param([Parameter(Mandatory)]$Day, [string]$TaskId, $PrNumber,
        [Parameter(Mandatory)][string]$Body, [Parameter(Mandatory)][string]$Name)

  $f = Join-Path $Day.LogDir $Name
  [System.IO.File]::WriteAllText($f, $Body, (New-Object System.Text.UTF8Encoding($false)))

  if ($PrNumber) { $null = Invoke-BestEffort $Day "the comment on PR #$PrNumber" { gh pr comment $PrNumber --body-file $f } }
  if (-not $TaskId) { return $true }
  if (-not (Test-Path $script:ClickUp)) {
    New-DayEvent -LogDir $Day.LogDir -Kind "record_failed" -Data @{ reason = "the board CLI is not at $($script:ClickUp)" } | Out-Null
    Write-Day $Day "  the board CLI is not where it should be"
    return $false
  }
  # Returned, not swallowed. Most callers are recording something the stream already holds and can
  # afford to lose the copy; one of them is writing the only durable record there is.
  return (Invoke-BestEffort $Day "the comment on card $TaskId" { python $script:ClickUp comment $TaskId --text "@$f" })
}

<#
  What the board CLI printed, or `$null` if it did not get to print. Separate from Invoke-BestEffort
  because that one is for recording, where losing the copy is survivable; this is for reading, where
  a failure that comes back as an empty answer is a wrong answer acted on.

  It is a variable so a probe can put its own board behind it. What decides whether a day starts is
  worth testing, and the only alternative to a seam here is a test that spends real requests on a
  real board to find out what happens when that board answers badly.
#>
$script:Board = {
  param([string[]]$Argv)
  try {
    $out = & python $script:ClickUp $Argv
    if ($LASTEXITCODE -ne 0) { return $null }
    return @($out | ForEach-Object { [string]$_ })
  } catch { return $null }
}

<#
  Is there anything a worker could do -- asked before a session is spent rather than after.

  Two things make one: a card in `in progress` is a session that died and has to be picked back up,
  and a card in `Open` carrying `grilled` is a card somebody has already decided. Nothing else is
  eligible, so with neither there is no session worth paying for.

  **This is an emptiness check and not the worker's picking rule**, which is board order, priority,
  what an open PR is already building, and what no machine can do at all. The two are allowed to
  disagree in one direction only: this says a session is worth starting and the worker then finds
  its first card is ungrilled and parks it. That costs a session, and what bounds it is the park
  ceiling, not this. Making it agree would mean writing board order twice, and the second copy
  would be the one nothing tests.

  The order of the questions is the cost. A day that has work answers on one or two listings, and
  only a board that looks empty pays for the tree -- which is where the refresh is, because that
  reading is the one an ending gets decided on. `tree` is cached, and validating a rename against a
  cache built this morning is validating nothing.
#>
function Get-BoardPool {
  param([Parameter(Mandatory)]$Day, [scriptblock]$Board)

  $none = {
    param([string]$Why)
    [pscustomobject]@{ Stop = $Why; Idle = $false; Resume = 0; Grilled = 0 }
  }

  # Only asked of the real one. A probe brings its own board and there is no file behind it.
  if (-not $Board) {
    $Board = $script:Board
    if (-not (Test-Path $script:ClickUp)) { return (& $none "the board CLI is not at $($script:ClickUp)") }
  }

  $resume  = Read-BoardCount (& $Board @("tasks", "--space", $script:Space, "--status", "in progress"))
  $grilled = Read-BoardCount (& $Board @("tasks", "--space", $script:Space, "--status", "Open", "--tag", "grilled"))
  if ($null -eq $resume -or $null -eq $grilled) {
    return (& $none "the board CLI answered something this cannot read")
  }

  if ($resume -gt 0 -or $grilled -gt 0) {
    New-DayEvent -LogDir $Day.LogDir -Kind "board_pool" -Data @{ resume = $resume; grilled = $grilled } | Out-Null
    return [pscustomobject]@{ Stop = ""; Idle = $false; Resume = $resume; Grilled = $grilled }
  }

  # Nothing came back, which is also what a status that no longer exists comes back as: the CLI
  # answers an unknown one with an empty list and exit 0. So an empty answer is not an ending until
  # the names it was asked under are still the board's names.
  $tree = & $Board @("tree", "--refresh")
  if ($null -eq $tree) { return (& $none "the board could not be read at all") }

  $statuses = @(Read-SpaceStatuses -Lines $tree -Space $script:Space)
  if ($statuses.Count -eq 0) { return (& $none "the board has no space called $($script:Space)") }

  # Case-sensitive on purpose: the query sent `Open` and the board answering to `open` would be a
  # rename this is here to catch, not a spelling of the same thing.
  foreach ($s in @("in progress", "Open")) {
    if ($statuses -cnotcontains $s) {
      return (& $none "the $($script:Space) board no longer has a '$s' status, so what is eligible cannot be asked")
    }
  }

  New-DayEvent -LogDir $Day.LogDir -Kind "board_pool" -Data @{ resume = 0; grilled = 0 } | Out-Null
  return [pscustomobject]@{ Stop = ""; Idle = $true; Resume = 0; Grilled = 0 }
}

function Move-Card {
  param([Parameter(Mandatory)]$Day, [Parameter(Mandatory)][string]$TaskId, [Parameter(Mandatory)][string]$To)
  if (-not (Test-Path $script:ClickUp)) { return $false }
  return (Invoke-BestEffort $Day "moving card $TaskId to $To" { python $script:ClickUp move $TaskId --status $To })
}

<#
  `regrill` goes on before `grilled` comes off, and the order is the whole point. The CLI sends one
  request per tag, so a transition can half happen; of the two half states, "both tags" leaves the
  card in the grill's queue where somebody will see it, and "neither tag" leaves it in nobody's.

  Both are checked. A caller that moves a card on the strength of a transition that did not happen
  is how a card reaches a status nothing looks at.
#>
function Set-CardTags {
  param([Parameter(Mandatory)]$Day, [Parameter(Mandatory)][string]$TaskId,
        [string]$Add, [string]$Remove)
  if (-not (Test-Path $script:ClickUp)) { return $false }
  $ok = $true
  if ($Add) {
    $ok = Invoke-BestEffort $Day "tagging card $TaskId '$Add'" { python $script:ClickUp tag $TaskId --add $Add }
  }
  if ($ok -and $Remove) {
    $ok = Invoke-BestEffort $Day "taking '$Remove' off card $TaskId" { python $script:ClickUp tag $TaskId --rm $Remove }
  }
  return $ok
}

<#
  A card that still holds a decision nobody here can make -- met by the worker before it built, or
  by the audit in a diff already finished. Both land here, because the answer to both is the same
  and it is not a question put to somebody at four in the afternoon: it is a card that goes back to
  a grill, which is the one place product decisions are made.

  **Three steps, in this order, and each one guards the next.** The comment is the only durable copy
  of what has to be settled; the tags are what puts the card in the grill's queue and out of the
  worker's; the move is what says it waits on a person. A move on top of a failed comment files a
  card nobody can act on, and a move on top of a failed retag files one nobody will find -- so the
  move only happens when both landed, and a caller that gets `$false` back is looking at a card that
  is still where it was, which is recoverable, rather than one that is somewhere nothing looks.

  Since the board became the transport for the decision itself rather than a copy of it, a board
  that cannot be reached is not a lost line in a report. It is the work lost.
#>
function Request-Grill {
  param([Parameter(Mandatory)]$Day, [Parameter(Mandatory)][string]$TaskId, $Owed, $PrNumber)

  $say = New-Object System.Collections.ArrayList
  if ($PrNumber) {
    # The marker the worker already knows. A card with an open PR is picked up on that PR's branch,
    # and inventing a second marker for the same fact is how the day ends up with two PRs on one
    # card: the protocol that resumes work is one protocol.
    $null = $say.Add("**Not merged.** A decision on this card is not the session's to make, so it needs grilling before anything else happens to it.")
  } else {
    $null = $say.Add("**Needs grilling.** A session stopped on this card: it holds a decision that is not the session's to make.")
  }
  $null = $say.Add("")
  if (@($Owed).Count -gt 0) {
    $null = $say.Add("What has to be settled before anybody builds on it:")
    $null = $say.Add("")
    foreach ($d in @($Owed)) {
      $null = $say.Add("- **" + [string]$d.what + "**")
      if ($d.why) { $null = $say.Add("  " + [string]$d.why) }
      foreach ($o in @($d.options)) { $null = $say.Add("  - " + [string]$o) }
    }
  } else {
    $null = $say.Add("The card was never grilled, so everything in it is still open.")
  }
  if ($PrNumber) {
    $null = $say.Add("")
    $null = $say.Add("PR #$PrNumber is open and green and stays that way. Once this is settled the card goes back in the pool, and whoever takes it picks up that PR's branch rather than opening a second one.")
  }
  $null = $say.Add("")
  $null = $say.Add("Retagged ``regrill``: nothing takes this card again until a grill settles the above and puts ``grilled`` back.")

  $said = Write-Elsewhere $Day -TaskId $TaskId -PrNumber $PrNumber -Body ($say -join "`n") -Name "needs-grill-$($Day.Cycle).md"
  $retagged = $false
  $moved = $false
  if ($said) { $retagged = Set-CardTags $Day -TaskId $TaskId -Add "regrill" -Remove "grilled" }
  if ($said -and $retagged) { $moved = Move-Card $Day -TaskId $TaskId -To "pending" }

  New-DayEvent -LogDir $Day.LogDir -Kind "decision_owed" -Data @{
    cycle = $Day.Cycle; task_id = $TaskId; pr_number = $PrNumber
    decisions = @($Owed); said = $said; retagged = $retagged; moved = $moved
  } | Out-Null

  if ($said -and $retagged -and $moved) { return "" }
  $lost = @()
  if (-not $said)     { $lost += "the decision never reached the card" }
  if (-not $retagged) { $lost += "the card still reads as grilled" }
  if (-not $moved)    { $lost += "the card never reached pending" }
  return ("the board could not be told what this card owes: " + ($lost -join ", "))
}

function Read-Contract {
  param([Parameter(Mandatory)]$Day, [Parameter(Mandatory)][string]$Name)
  $p = Join-Path $Day.LogDir $Name
  if (-not (Test-Path $p)) { return $null }
  try { return ((Get-Content $p -Raw) | ConvertFrom-Json) } catch { return $null }
}

Export-ModuleMember -Function (@(
  "Get-Repo", "New-DayRun", "Open-Day", "Enter-AtomLease", "Write-Day", "Write-Atom", "Write-AtomCrash", "Invoke-Session",
  "Test-Sound", "Invoke-BestEffort", "Write-Elsewhere", "Get-BoardPool", "Move-Card", "Set-CardTags", "Request-Grill",
  "Invoke-Merge", "Invoke-Recover", "Read-Contract"
) + @($script:DayModule.ExportedFunctions.Keys))
