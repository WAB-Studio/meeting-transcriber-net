#requires -Version 5.1
<#
  One working session: the checkout is checked, a fresh journal is laid down, `/next-task` runs on
  the card `run-picker.ps1` chose, and what it emitted becomes this cycle's handoff.

    .\run-picker.ps1     # first -- which card
    .\run-worker.ps1     # then -- the work on it

  **This does not decide which card.** That is the picker's, in one place, and a worker that went
  looking for its own would be the second copy of a rule whose two copies already disagreed once.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$env:PYTHONIOENCODING  = "utf-8"
Import-Module (Join-Path $PSScriptRoot "atom.psm1") -Force -DisableNameChecking

$script:Day = $null
trap { Write-AtomCrash -Message $_.Exception.Message -Day $script:Day; exit 1 }

$HandoffKeys = @("outcome","task_id","isc_closed","probes",
                 "decisions_deferred","left_out","skipped","blocked_reason","head_sha")

# Said, but allowed to be null: a `blocked` handoff has no PR to name and says so by writing null,
# and refusing that would stop the day over a field it was right to leave empty. The outcome that
# must carry a number, `pr_opened`, is checked for one by value further down.
$HandoffPresent = @("pr_number")

$day = Open-Day
$script:Day = $day
if ($day.Error -ne "") { Write-Host $day.Error; Write-Atom @{ ok = $false; reason = $day.Error }; exit 1 }

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

$cycle = $day.Cycle + 1
$day.Cycle = $cycle

# The card comes from the picker and is never chosen here. A pick that is not there is not an
# ending and not a failure of the work: it is a step of the cycle that has not been run, so this
# says which one rather than going and doing it -- a worker that picked its own card would be the
# second copy of a rule whose two copies disagreed once already, and the copy nothing tests.
$pickfile = Join-Path $day.LogDir "pick-$cycle.json"
$pick = Read-Contract $day "pick-$cycle.json"
if ($null -eq $pick) {
  Write-Atom @{ ok = $false; reason = "cycle $cycle has no pick -- run-picker.ps1 chooses the card" }
  exit 1
}
if ([string]$pick.outcome -ne "picked" -or -not [string]$pick.task_id) {
  Write-Atom @{ ok = $false; reason = "the pick for cycle $cycle names no card to work ($([string]$pick.outcome))" }
  exit 1
}

# Laid down before the session, so the session only adds prose under headings already there.
# Whatever a dead cycle left is parked first rather than overwritten.
$parked = Reset-Journal -Repo $day.Repo
if ($parked) {
  New-DayEvent -LogDir $day.LogDir -Kind "journal_parked" -Data @{ to = $parked } | Out-Null
  Write-Day $day "a journal from before was parked: $parked"
}

$handoff = Join-Path $day.LogDir "handoff-$cycle.json"
Remove-Item $handoff -ErrorAction SilentlyContinue   # so a stale file cannot pass as fresh

Write-Day $day "[$cycle] worker: /next-task on $($pick.task_id)"
$w = Invoke-Session -Day $day -Role "worker" -Prompt "/next-task $handoff $pickfile" -Cycle $cycle
if ($null -eq $w) { Write-Atom @{ ok = $false; stop = "the worker left no result" }; exit 1 }

$status = Get-DayStatus -LogDir $day.LogDir
Write-Day $day ("[$cycle] worker done: {0}  turns={1}  usd={2:N2}  running={3:N2}" -f `
                $w.subtype, $w.num_turns, $w.total_cost_usd, $status.Cost)

# An error leaves unknown effects -- it may have moved a card or opened a PR. Repeating it blind is
# what CLAUDE.md forbids for a job that may already have been charged.
if ($w.is_error) { Write-Atom @{ ok = $false; stop = "the worker ended in error" }; exit 1 }

$unsound = Test-Sound -Day $day
if ($unsound -ne "") { Write-Atom @{ ok = $false; stop = "the worker's session is not sound: $unsound" }; exit 1 }

# The contract is derived from what the session emitted, and this writes the file. Having the
# session also remember to write it was a second way to fail without being a second guarantee: a
# worker once said the whole handoff, did not write it, and the day died with the PR open.
# `no_tasks` is not among them any more. A session handed one card cannot discover that the board is
# empty, and while it was allowed it was the quiet way out of the whole arrangement: a worker that
# failed to read its pick, or fell back on looking at the board itself, returned it with an empty
# card id -- which named no card, so it slipped past the mismatch check below, and the day moved on
# leaving the picked card untouched.
$c = Get-ContractFromText ([string]$w.result)
$bad = Test-DayContract -Contract $c -Required $HandoffKeys -Present $HandoffPresent `
                        -Field "outcome" -Allowed @("pr_opened","needs_grill","blocked")
if ($bad -ne "") {
  New-DayEvent -LogDir $day.LogDir -Kind "handoff_invalid" -Data @{ cycle = $cycle; reason = $bad } | Out-Null
  Write-Atom @{ ok = $false; stop = "invalid handoff: $bad" }
  exit 1
}
[System.IO.File]::WriteAllText($handoff, ($c | ConvertTo-Json -Depth 12), (New-Object System.Text.UTF8Encoding($false)))

New-DayEvent -LogDir $day.LogDir -Kind "handoff" -Data @{
  cycle = $cycle; outcome = [string]$c.outcome; task_id = [string]$c.task_id
  pr_number = $c.pr_number; head_sha = [string]$c.head_sha
  deferred = @($c.decisions_deferred).Count; skipped = @($c.skipped).Count
  # Test-JsonTrue and not `-not $_.passed`: a probe that came back as the string "false" is truthy
  # in PowerShell, and would be counted green in the one place that counts red ones.
  probes_red = @($c.probes | Where-Object { -not (Test-JsonTrue $_.passed) }).Count
  left_out = @($c.left_out); deferred_detail = @($c.decisions_deferred); skipped_detail = @($c.skipped)
} | Out-Null

Write-Day $day ("[$cycle] handoff: {0}  task={1}  pr=#{2}  deferred={3}  skipped={4}" -f `
                $c.outcome, $c.task_id, $c.pr_number, @($c.decisions_deferred).Count, @($c.skipped).Count)

# The worker was handed a card and came back holding another one. This stops the day, and the whole
# arrangement rests on it: a boundary that logs its own violation and carries on is a suggestion.
# What the next atoms would otherwise do is audit and merge a card nobody chose, which is exactly
# the failure the split was built to prevent -- and it would land in `main` before anybody read the
# warning that said so.
#
# Nothing is thrown away by stopping. The PR the worker opened is open, its card is where the worker
# left it, and the handoff is on disk; what a person finds in the morning is a finished PR against
# an unchosen card, named in the report, rather than that card already in `main`.
if ([string]$c.task_id -ne [string]$pick.task_id) {
  New-DayEvent -LogDir $day.LogDir -Kind "anomaly" -Data @{
    level = "stop"; code = "pick_ignored"
    text = "cycle $cycle was picked $([string]$pick.task_id) and the worker came back holding '$([string]$c.task_id)'"
  } | Out-Null
  Write-Day $day "  !! picked $($pick.task_id) and the worker worked '$($c.task_id)'"
  Write-Atom @{
    ok = $false; cycle = $cycle
    stop = "the worker did not work the card it was given: picked $([string]$pick.task_id), came back with '$([string]$c.task_id)'"
  }
  exit 1
}

# The card still holds a decision that is not the worker's, met before anything was built. The
# worker does not put it to anybody: it says the card needs grilling, and grilling is where product
# decisions are made. What it costs is a short session instead of a whole diff written to a guess.
if ([string]$c.outcome -eq "needs_grill") {
  if (-not [string]$c.task_id) {
    New-DayEvent -LogDir $day.LogDir -Kind "handoff_invalid" -Data @{
      cycle = $cycle; reason = "it says needs_grill and names no card"
    } | Out-Null
    Write-Atom @{ ok = $false; stop = "the worker says a card needs grilling and does not say which" }
    exit 1
  }
  # Validated only when it says something. A card with no tag at all has nothing to name yet -- that
  # is the ordinary case, it is most of them, and demanding a decision the worker never got far
  # enough to see would end the day on the first ungrilled card.
  if (@($c.decisions_owed).Count -gt 0) {
    $unreadable = Test-DecisionsOwed @($c.decisions_owed)
    if ($unreadable -ne "") {
      New-DayEvent -LogDir $day.LogDir -Kind "handoff_invalid" -Data @{
        cycle = $cycle; reason = "it says a decision is owed but $unreadable"
      } | Out-Null
      Write-Atom @{ ok = $false; stop = "the worker says a decision is owed but $unreadable" }
      exit 1
    }
  }
  $park = Request-Grill -Day $day -TaskId ([string]$c.task_id) -Owed @($c.decisions_owed)
  if ($park.Lost -ne "") { Write-Atom @{ ok = $false; cycle = $cycle; stop = $park.Lost }; exit 1 }
  foreach ($d in @($c.decisions_owed)) { Write-Day $day ("  ? " + [string]$d.what) }
  Write-Day $day "[$cycle] card $($c.task_id) needs grilling before anything is built on it"

  # Asked after the park is on the stream, so the card that hit the ceiling is counted in it.
  $ceiling = Test-ParkCeiling -Status (Get-DayStatus -LogDir $day.LogDir)
  if ($ceiling -ne "") {
    New-DayEvent -LogDir $day.LogDir -Kind "no_more_cycles" -Data @{ reason = "blocked -- $ceiling" } | Out-Null
    Write-Day $day "[$cycle] $ceiling"
    Write-Atom @{
      ok = $true; cycle = $cycle; outcome = "blocked"; task_id = [string]$c.task_id
      blocked_reason = $ceiling; decisions = @($c.decisions_owed)
    }
    exit 0
  }

  Write-Atom @{
    ok = $true; cycle = $cycle; action = "parked"; task_id = [string]$c.task_id
    decisions = @($c.decisions_owed)
  }
  exit 0
}

# A session that built something and wrote down nothing about what it tried leaves the next one to
# repeat every dead end it already walked. It is written down and it does not stop the cycle: a PR
# whose work is done and whose CI is green is not made worse by a missing diary, and refusing to
# audit it would throw away the verifiable thing over the unverifiable one.
if ([string]$c.outcome -eq "pr_opened") {
  $thin = Test-JournalBody (Get-JournalPath $day.Repo)
  if ($thin -ne "") {
    New-DayEvent -LogDir $day.LogDir -Kind "anomaly" -Data @{
      level = "warn"; code = "journal"
      text = "cycle $cycle opened a PR and left no journal, so what it tried and discarded is lost"
    } | Out-Null
    Write-Day $day "[$cycle] the journal is empty -- what this session discarded is lost"
  }
}

# A PR that names no commit cannot be audited against what was delivered, and an empty SHA on both
# sides passes an equality check while proving nothing.
if ([string]$c.outcome -eq "pr_opened") {
  $missing = @()
  if ([string]::IsNullOrWhiteSpace([string]$c.head_sha)) { $missing += "head_sha" }
  if (-not $c.pr_number -or [int]$c.pr_number -le 0) { $missing += "pr_number" }
  if ($missing.Count -gt 0) {
    New-DayEvent -LogDir $day.LogDir -Kind "handoff_invalid" -Data @{
      cycle = $cycle; reason = "it says pr_opened with no " + ($missing -join " and no ")
    } | Out-Null
    Write-Atom @{ ok = $false; stop = "the handoff says pr_opened with no " + ($missing -join " and no ") }
    exit 1
  }
}

Write-Atom @{
  ok = $true; cycle = $cycle; outcome = [string]$c.outcome; task_id = [string]$c.task_id
  pr_number = $c.pr_number; blocked_reason = [string]$c.blocked_reason
}
