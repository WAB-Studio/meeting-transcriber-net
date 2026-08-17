#requires -Version 5.1
<#
  Acts on this cycle's verdict. Three answers and no fourth: integrate the PR, put the card back, or
  print what has to be asked before either can happen.

  Which one it is comes out of the verdict, not out of whoever runs this. A cycle with no verdict --
  an audit that never ran, a worker whose close was refused -- is put back, because the reading that
  costs nothing is the one that does not put an unread diff into `main`.

    .\close-cycle.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$env:PYTHONIOENCODING  = "utf-8"
Import-Module (Join-Path $PSScriptRoot "atom.psm1") -Force -DisableNameChecking

$script:Day = $null
trap { Write-AtomCrash -Message $_.Exception.Message -Day $script:Day; exit 1 }

$day = Open-Day
$script:Day = $day
if ($day.Error -ne "") { Write-Host $day.Error; Write-Atom @{ ok = $false; reason = $day.Error }; exit 1 }

$cycle = $day.Cycle
$h = Read-Contract $day "handoff-$cycle.json"
if ($null -eq $h) { Write-Atom @{ ok = $false; reason = "cycle $cycle has no handoff" }; exit 1 }

# Closing twice was not harmless: it recorded a second recovery, and because a card's destination is
# counted off those, the duplicate sent it to `pending` as though two sessions had failed to land it.
# A close that did not reach the board is not one of those -- it is the case this atom is run again
# for, and `Test-CycleClosed` is what tells the two apart.
if (Test-CycleClosed -LogDir $day.LogDir -Cycle $cycle) {
  Write-Atom @{ ok = $true; cycle = $cycle; action = "already closed" }
  exit 0
}

$v = Read-Contract $day "verdict-$cycle.json"
if ($null -eq $v) {
  $rec = Invoke-Recover -Day $day -Handoff $h -Reason "no verdict was reached for this cycle"
  if ($rec.Lost -ne "") { Write-Atom @{ ok = $false; cycle = $cycle; stop = $rec.Lost }; exit 1 }
  Write-Atom @{ ok = $true; cycle = $cycle; action = "recovered"; to = $rec.To }
  exit 0
}

# One function decides, here and in the probe, so the two cannot drift. It also refuses to merge a
# verdict that contradicts itself -- a `pass` that owes a decision has said two things.
$act = Resolve-Verdict $v

# A decision the audit will not make does not stop the day and does not wait for anybody. It goes on
# the card, in writing, and the card goes to `pending` where no worker touches it until a grill has
# settled it. The PR stays open and green; nothing merges it on a guess.
if ($act.to -eq "pending") {
  $lost = Request-Grill -Day $day -TaskId ([string]$h.task_id) -Owed @($v.decisions_owed) -PrNumber $h.pr_number
  if ($lost -ne "") { Write-Atom @{ ok = $false; cycle = $cycle; stop = $lost }; exit 1 }
  $parked = Complete-Journal -Repo $day.Repo -TaskId ([string]$h.task_id)
  if ($parked) { New-DayEvent -LogDir $day.LogDir -Kind "journal_parked" -Data @{ to = $parked } | Out-Null }
  Write-Day $day "[$cycle] PR #$($h.pr_number) left open -- card $($h.task_id) owes a decision and goes to pending"
  Write-Atom @{
    ok = $true; cycle = $cycle; action = "parked"; task_id = [string]$h.task_id
    pr_number = $h.pr_number; decisions = @($v.decisions_owed)
  }
  exit 0
}

if ($act.action -eq "recover") {
  $why = $act.reason
  if ([string]$v.verdict -eq "hold") { $why += " -- " + ((@($v.reasons) | Select-Object -First 2) -join " ") }
  $rec = Invoke-Recover -Day $day -Handoff $h -Reason $why
  if ($rec.Lost -ne "") { Write-Atom @{ ok = $false; cycle = $cycle; stop = $rec.Lost }; exit 1 }
  Write-Atom @{ ok = $true; cycle = $cycle; action = "recovered"; to = $rec.To }
  exit 0
}

$failed = Invoke-Merge -Day $day -Handoff $h
if ($failed -ne "") { Write-Atom @{ ok = $false; cycle = $cycle; stop = $failed }; exit 1 }
Write-Atom @{ ok = $true; cycle = $cycle; action = "merged"; pr_number = $h.pr_number }
