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

# The board CLI. An atom talks to the board only to act on something already decided: recording a
# decision a person made, and putting a card back. It judges nothing.
$script:ClickUp = Join-Path $env:USERPROFILE ".claude\skills\clickup\clickup.py"

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

<#
  The PR is integrated here rather than by whatever decided it should be: it cannot be forgotten,
  cannot happen twice, and lands on the stream. The next preflight fast-forwards local `main`, so
  the cycle after this one branches from a base already carrying this one.

  Archiving the journal is part of the same act and not a step beside it. **Only a merge archives**
  -- everything else falls through to parked -- which is what makes a cycle that died come out right
  without anything having been written for the case.

  Nothing after the merge can undo it, so nothing after the merge fails the cycle. What the card
  does not get said on it is written down and the day goes on: the diff is in `main` either way, and
  refusing to admit that would leave a merged PR reported as a failure nobody can act on.
#>
function Invoke-Merge {
  param([Parameter(Mandatory)]$Day, [Parameter(Mandatory)]$Handoff)
  Write-Day $Day "integrating PR #$($Handoff.pr_number)"

  # --match-head-commit is what makes the audit's verdict a fact about what lands. Between the audit
  # reading the head and this running, the branch can move -- and the gap is at its widest exactly
  # when a card went back for a decision, which can take hours. Without it the merge takes whatever
  # is at the tip now, which is code nobody read.
  gh pr merge $Handoff.pr_number --merge --delete-branch --match-head-commit $Handoff.head_sha
  if ($LASTEXITCODE -ne 0) {
    New-DayEvent -LogDir $Day.LogDir -Kind "merge_failed" -Data @{
      cycle = $Day.Cycle; pr_number = $Handoff.pr_number; reason = "gh exited with $LASTEXITCODE"
    } | Out-Null
    return "the merge failed ($LASTEXITCODE) -- the PR is left open, and the head may have moved since it was audited"
  }
  New-DayEvent -LogDir $Day.LogDir -Kind "merged" -Data @{ cycle = $Day.Cycle; pr_number = $Handoff.pr_number } | Out-Null
  Write-Day $Day "PR #$($Handoff.pr_number) integrated"

  # The journal is local and gitignored, so filing it is not keeping it. What the next person can
  # still read is the card, which is remote and survives the clone.
  $filed = Complete-Journal -Repo $Day.Repo -Merged -TaskId ([string]$Handoff.task_id)
  if ($filed) {
    New-DayEvent -LogDir $Day.LogDir -Kind "journal_filed" -Data @{ to = $filed } | Out-Null
    $body = "**What the session tried, and what it threw away.**`n`n" + (Get-Content $filed -Raw)
    $said = Write-Elsewhere $Day -TaskId ([string]$Handoff.task_id) -Body $body -Name "journal-$($Day.Cycle).md"
    if (-not $said) { Write-Day $Day "  the journal did not reach card $($Handoff.task_id) -- it is only at $filed, which is gitignored" }
  }
  return ""
}

<#
  What used to end the day. A verdict saying this PR does not hold up is a fact about one PR and not
  about the hours left: the PR stays open for a person to read, the card goes back to the pool
  carrying the reason, and the next cycle takes the next task.

  Twice is a different fact. A card that comes back a second time is one two sessions could not
  land, and returning it to the pool again is how a day spends itself going in a circle -- so the
  second time it goes to `pending`, which is where work waits on a person.

  **The comment guards the move**, the same way it does for a card that owes a decision. What tells
  the next worker this card already has a PR is the `**Not merged.**` comment on it; a card that
  reached the pool without one is a card somebody starts over, and the day ends with two PRs against
  one task. So a comment that did not land leaves the card where it is -- in `in review`, where a
  person will find it -- and the caller is told, rather than the card being moved on the strength of
  a record that does not exist.
#>
function Invoke-Recover {
  param([Parameter(Mandatory)]$Day, [Parameter(Mandatory)]$Handoff, [Parameter(Mandatory)][string]$Reason)

  $task = [string]$Handoff.task_id
  # Read off the stream and not out of anything still running: a day relaunched used to forget, and
  # a card nothing could land went back in the pool once per restart forever.
  $to = Get-CardDestination -Recovered (Get-DayStatus -LogDir $Day.LogDir).Recovered -TaskId $task

  if (-not $task) {
    New-DayEvent -LogDir $Day.LogDir -Kind "recovered" -Data @{
      cycle = $Day.Cycle; task_id = ""; pr_number = $Handoff.pr_number; to = $to
      reason = $Reason; said = $false; moved = $false
    } | Out-Null
    return [pscustomobject]@{ To = $to; Lost = "the handoff names no card, so there is nothing to put back" }
  }

  $say = New-Object System.Collections.ArrayList
  $null = $say.Add("**Not merged.** $Reason")
  $null = $say.Add("")
  if ($Handoff.pr_number) {
    $null = $say.Add("PR #$($Handoff.pr_number) is left open and this card goes back to ``$to``. Pick it up on that PR's own branch rather than opening a second one. The day did not stop over it.")
  } else {
    $null = $say.Add("This card goes back to ``$to``. The day did not stop over it.")
  }
  if ($to -eq "pending") {
    $null = $say.Add("")
    $null = $say.Add("Second time it has come back, so it waits on a person rather than going back in the pool.")
  }

  $said  = Write-Elsewhere $Day -TaskId $task -PrNumber $Handoff.pr_number -Body ($say -join "`n") -Name "recovered-$($Day.Cycle).md"
  $moved = $false
  if ($said) { $moved = Move-Card $Day -TaskId $task -To $to }

  # What was meant and what happened. Recording the intention as the outcome left a card in
  # `in review`, where no worker looks for it, while the report called it recovered.
  New-DayEvent -LogDir $Day.LogDir -Kind "recovered" -Data @{
    cycle = $Day.Cycle; task_id = $task; pr_number = $Handoff.pr_number; to = $to
    reason = $Reason; said = $said; moved = $moved
  } | Out-Null

  $parked = Complete-Journal -Repo $Day.Repo -TaskId $task
  if ($parked) { New-DayEvent -LogDir $Day.LogDir -Kind "journal_parked" -Data @{ to = $parked } | Out-Null }

  if ($said -and $moved) {
    Write-Day $Day "not merged -- PR #$($Handoff.pr_number) left open, card $task -> $to"
    return [pscustomobject]@{ To = $to; Lost = "" }
  }
  $lost = @()
  if (-not $said)  { $lost += "the reason never reached the card, so nothing says its PR is already open" }
  if (-not $moved) { $lost += "the card never reached $to" }
  Write-Day $Day "not merged -- PR #$($Handoff.pr_number) left open, and card $task stayed where it was"
  return [pscustomobject]@{ To = $to; Lost = ("the board could not be told this PR did not land: " + ($lost -join ", ")) }
}

function Read-Contract {
  param([Parameter(Mandatory)]$Day, [Parameter(Mandatory)][string]$Name)
  $p = Join-Path $Day.LogDir $Name
  if (-not (Test-Path $p)) { return $null }
  try { return ((Get-Content $p -Raw) | ConvertFrom-Json) } catch { return $null }
}

Export-ModuleMember -Function (@(
  "Get-Repo", "New-DayRun", "Open-Day", "Enter-AtomLease", "Write-Day", "Write-Atom", "Write-AtomCrash", "Invoke-Session",
  "Test-Sound", "Invoke-BestEffort", "Write-Elsewhere", "Move-Card", "Set-CardTags", "Request-Grill",
  "Invoke-Merge", "Invoke-Recover", "Read-Contract"
) + @($script:DayModule.ExportedFunctions.Keys))
