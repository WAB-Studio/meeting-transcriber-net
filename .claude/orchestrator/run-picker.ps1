#requires -Version 5.1
<#
  Which card the next session takes, and nothing else. `/pick-task` runs, and what it emitted becomes
  this cycle's pick.

    .\run-picker.ps1

  It is an atom of its own rather than a few lines at the top of `run-worker.ps1` for two reasons,
  and only one of them is tidiness.

  The pick is a judgement over card text -- board order, priority, whether an ungrilled card in
  front of the pool is one the next grilled card would be built on top of -- so it needs a model,
  not a script. Making it the worker's first job put that judgement at the end of a fifty-minute
  session, in the context of whatever feature that session had been living inside, and paid for it
  out of the worker's budget. Worst of all, the session that files `BUG - ...` on its way out was
  the one ruling on whether its own bug outranked the board.

  And separating it puts a RESULT between the deciding and the working, which is a place to look.
  Whoever is sequencing sees the card and the reason for it **before** a worker is dispatched.

  ASCII only, and no accented text. Windows PowerShell reads a .ps1 without a BOM as ANSI, so a
  dash or an accent in a comment is a parse error on the machine this actually runs on.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$env:PYTHONIOENCODING  = "utf-8"
Import-Module (Join-Path $PSScriptRoot "atom.psm1") -Force -DisableNameChecking

$script:Day = $null
trap { Write-AtomCrash -Message $_.Exception.Message -Day $script:Day; exit 1 }

$PickKeys = @("outcome","task_id","why","skipped","blocked_reason")

# Said, but allowed to be null. `pr_number: null` is "this card has no PR in flight" and is the
# ordinary answer; a picker that omitted the field entirely is one that may have found an open PR
# and not mentioned it, and that card gets picked up as fresh work and opened a second PR against.
$PickPresent = @("pr_number")

$day = Open-Day
$script:Day = $day
if ($day.Error -ne "") { Write-Host $day.Error; Write-Atom @{ ok = $false; reason = $day.Error }; exit 1 }

# Asked before anything is spent. A dirty tree or a diverged `main` is not a board problem, and a
# card picked against a checkout the worker will refuse is a session paid for nothing.
$blocked = Test-Preflight -Day $day
if ($blocked -ne "") {
  New-DayEvent -LogDir $day.LogDir -Kind "preflight_failed" -Data @{ reason = $blocked } | Out-Null
  Write-Day $day "preflight: $blocked"
  Write-Atom @{ ok = $false; stop = "preflight: $blocked" }
  exit 1
}
New-DayEvent -LogDir $day.LogDir -Kind "preflight_ok" | Out-Null

$still = Test-CycleStillOpen -Day $day
if ($still -ne "") { Write-Atom @{ ok = $false; reason = $still }; exit 1 }

# Nothing eligible on the board is an ending, and it is one that costs a request rather than a
# session. It is asked here, before the picker runs, precisely so an empty board never pays for a
# model to look at it.
$pool = Get-BoardPool -Day $day
if ($pool.Stop -ne "") {
  New-DayEvent -LogDir $day.LogDir -Kind "board_unreadable" -Data @{ reason = $pool.Stop } | Out-Null
  Write-Day $day "the board: $($pool.Stop)"
  Write-Atom @{ ok = $false; stop = "the board could not say what is eligible: $($pool.Stop)" }
  exit 1
}
if ($pool.Idle) {
  # On the stream and not only in the RESULT. `end-day.ps1` works out why a day ended by reading
  # this back, and an ending that lived only in what an atom printed came out of the morning
  # report as "ended by hand" -- which is the one thing it was not.
  $why = "no_tasks -- nothing on the board is grilled and nothing was left in progress"
  New-DayEvent -LogDir $day.LogDir -Kind "no_more_cycles" -Data @{ reason = $why } | Out-Null
  Write-Day $day "nothing in progress and nothing grilled -- no session to spend"
  Write-Atom @{ ok = $true; outcome = "no_tasks"; reason = $why }
  exit 0
}

# The cycle this pick belongs to. `Get-CurrentCycle` counts worker sessions, so this is the same
# number `run-worker.ps1` will compute when it comes to consume the pick: the picker's own session
# does not advance it, which is what lets the pick be looked at, and the worker be launched, as two
# separate acts on the same cycle.
$cycle = $day.Cycle + 1
$day.Cycle = $cycle

$pickfile = Join-Path $day.LogDir "pick-$cycle.json"

# A pick already made is returned, not made again. What sequences these is a model, so this atom
# running twice before its worker is not a hypothetical -- and recomputing was not harmless: it
# spends a second session, and the board it reads has moved, so the answer it prints can differ from
# the one already announced. The card somebody was told about would then not be the card worked.
$done = Read-Contract $day "pick-$cycle.json"
if ($done -and [string]$done.outcome) {
  Write-Day $day ("[$cycle] pick already made: {0} {1} -- {2}" -f $done.outcome, $done.task_id, $done.why)
  Write-Atom @{
    ok = $true; cycle = $cycle; outcome = [string]$done.outcome; task_id = [string]$done.task_id
    pr_number = $done.pr_number; why = [string]$done.why; blocked_reason = [string]$done.blocked_reason
  }
  exit 0
}

Write-Day $day "[$cycle] picker: /pick-task"
$k = Invoke-Session -Day $day -Role "picker" -Prompt "/pick-task $pickfile" -Cycle $cycle
if ($null -eq $k) { Write-Atom @{ ok = $false; stop = "the picker left no result" }; exit 1 }

$status = Get-DayStatus -LogDir $day.LogDir
Write-Day $day ("[$cycle] picker done: {0}  turns={1}  usd={2:N2}  running={3:N2}" -f `
                $k.subtype, $k.num_turns, $k.total_cost_usd, $status.Cost)

# An error leaves unknown effects -- the picker may have moved a card it declared unbuildable.
# Repeating it blind is what CLAUDE.md forbids for a job that may already have been charged.
if ($k.is_error) { Write-Atom @{ ok = $false; stop = "the picker ended in error" }; exit 1 }

$unsound = Test-Sound -Day $day
if ($unsound -ne "") { Write-Atom @{ ok = $false; stop = "the picker's session is not sound: $unsound" }; exit 1 }

$c = Get-ContractFromText ([string]$k.result)
$bad = Test-DayContract -Contract $c -Required $PickKeys -Present $PickPresent `
                        -Field "outcome" -Allowed @("picked","blocked","no_tasks")
# A pick naming no card is the one failure that reads as success everywhere downstream: the worker
# would be launched with an empty id and would go back to picking one itself, which is the whole
# arrangement undone in silence.
if ($bad -eq "" -and [string]$c.outcome -eq "picked" -and -not [string]$c.task_id) {
  $bad = "it says picked and names no card"
}
if ($bad -eq "" -and [string]$c.outcome -eq "blocked" -and -not [string]$c.blocked_reason) {
  $bad = "it says blocked and does not say what by"
}
if ($bad -ne "") {
  New-DayEvent -LogDir $day.LogDir -Kind "pick_invalid" -Data @{ cycle = $cycle; reason = $bad } | Out-Null
  Write-Atom @{ ok = $false; stop = "invalid pick: $bad" }
  exit 1
}

[System.IO.File]::WriteAllText($pickfile, ($c | ConvertTo-Json -Depth 12), (New-Object System.Text.UTF8Encoding($false)))

New-DayEvent -LogDir $day.LogDir -Kind "pick" -Data @{
  cycle = $cycle; outcome = [string]$c.outcome; task_id = [string]$c.task_id
  pr_number = $c.pr_number; why = [string]$c.why
  skipped = @($c.skipped); blocked_reason = [string]$c.blocked_reason
} | Out-Null

# The line that says whether the ordering rule is picking the right thing, and the only place it
# shows without opening a transcript.
Write-Day $day ("[$cycle] pick: {0} {1} -- {2}" -f $c.outcome, $c.task_id, $c.why)

# After the contract is on disk and on the stream, and never before: the session declares what
# nobody could build and this is what moves it, so a crash anywhere in the pick leaves those cards
# in the pool rather than in `pending` with nothing saying why they went there.
$stranded = @(Skip-Card -Day $day -Skipped @($c.skipped))
if ($stranded.Count -gt 0) {
  New-DayEvent -LogDir $day.LogDir -Kind "anomaly" -Data @{
    level = "warn"; code = "skip_failed"
    text = "cycle $cycle could not put " + ($stranded -join ", ") + " in pending, so the next pick meets them again"
  } | Out-Null
}

if ([string]$c.outcome -eq "no_tasks") {
  $why = "no_tasks -- " + [string]$c.why
  New-DayEvent -LogDir $day.LogDir -Kind "no_more_cycles" -Data @{ reason = $why } | Out-Null
  Write-Atom @{ ok = $true; cycle = $cycle; outcome = "no_tasks"; reason = $why }
  exit 0
}

# Something stands in front of the pool that no session may build past -- an ungrilled card the next
# grilled one would be built on top of, a list that no longer resolves, a board waiting entirely
# behind unmerged PRs. The day ends rather than working around it, and no card is moved: what has to
# happen is a person's, and moving the card would only take it out of the queue they read.
if ([string]$c.outcome -eq "blocked") {
  $why = "blocked -- " + [string]$c.blocked_reason
  New-DayEvent -LogDir $day.LogDir -Kind "no_more_cycles" -Data @{ reason = $why } | Out-Null
  Write-Day $day "[$cycle] $why"
  Write-Atom @{
    ok = $true; cycle = $cycle; outcome = "blocked"; task_id = [string]$c.task_id
    blocked_reason = [string]$c.blocked_reason
  }
  exit 0
}

Write-Atom @{
  ok = $true; cycle = $cycle; outcome = "picked"; task_id = [string]$c.task_id
  pr_number = $c.pr_number; why = [string]$c.why; skipped = @($c.skipped)
}
