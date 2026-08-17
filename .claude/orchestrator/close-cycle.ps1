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

$day = Open-Day
if ($day.Error -ne "") { Write-Host $day.Error; Write-Atom @{ ok = $false; reason = $day.Error }; exit 1 }

$cycle = $day.Cycle
$h = Read-Contract $day "handoff-$cycle.json"
if ($null -eq $h) { Write-Atom @{ ok = $false; reason = "cycle $cycle has no handoff" }; exit 1 }

$v = Read-Contract $day "verdict-$cycle.json"
if ($null -eq $v) {
  $to = Invoke-Recover -Day $day -Handoff $h -Reason "no verdict was reached for this cycle"
  Write-Atom @{ ok = $true; cycle = $cycle; action = "recovered"; to = $to }
  exit 0
}

# One function decides, here and in the probe, so the two cannot drift. It also refuses to merge a
# verdict that contradicts itself -- a `pass` carrying questions has said two things.
$act = Resolve-Verdict $v

if ($act.action -eq "ask") {
  # Written down before anybody is asked, because the report promises every question the day put and
  # the answer arrives in a separate run of a separate atom.
  foreach ($q in @($v.questions)) {
    New-DayEvent -LogDir $day.LogDir -Kind "question_asked" -Data @{
      cycle = $cycle; id = [string]$q.id; question = [string]$q.question; why = [string]$q.why
      options = @($q.options); pr_number = $h.pr_number; task_id = [string]$h.task_id
    } | Out-Null
    Write-Day $day "  ? [$($q.id)] $([string]$q.question)"
  }
  Write-Day $day "[$cycle] this cycle needs an answer before it closes"
  Write-Atom @{ ok = $true; cycle = $cycle; action = "ask"; questions = @($v.questions) }
  exit 0
}

if ($act.action -eq "recover") {
  $why = $act.reason
  if ([string]$v.verdict -eq "hold") { $why += " -- " + ((@($v.reasons) | Select-Object -First 2) -join " ") }
  $to = Invoke-Recover -Day $day -Handoff $h -Reason $why
  Write-Atom @{ ok = $true; cycle = $cycle; action = "recovered"; to = $to }
  exit 0
}

$failed = Invoke-Merge -Day $day -Handoff $h
if ($failed -ne "") { Write-Atom @{ ok = $false; cycle = $cycle; stop = $failed }; exit 1 }
Write-Atom @{ ok = $true; cycle = $cycle; action = "merged"; pr_number = $h.pr_number }
