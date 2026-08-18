#requires -Version 5.1
<#
  The day to work on, and the claim on the checkout that says which run every atom after this one
  belongs to. What was left unfinished is continued; when nothing was, a run is opened. Its RESULT
  says which of the two it did and which atom the run is waiting on.

    .\start-day.ps1

  There is no second script and no argument saying which. A run stops for reasons nobody chose --
  the usage window, a conversation that ended, an atom that refused -- and after any of them the
  answer is the same: carry on. Starting fresh over an unfinished run is `end-day.ps1` first, the
  atom that already means that.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$env:PYTHONIOENCODING  = "utf-8"   # without this the board CLI dies printing accents
Import-Module (Join-Path $PSScriptRoot "atom.psm1") -Force -DisableNameChecking

$script:Day = $null
trap { Write-AtomCrash -Message $_.Exception.Message -Day $script:Day; exit 1 }

$day = New-DayRun
$script:Day = $day
if ($day.Error -ne "") {
  Write-Host $day.Error
  Write-Atom @{ ok = $false; reason = $day.Error }
  exit 1
}

# The engine is checked before a session is launched rather than in the product's CI, which has no
# business knowing this exists. It takes a second and it fails at the only moment the answer is
# worth having: before a broken loop costs an hour and twenty dollars. A continued run is probed
# again on purpose -- what it is continuing into is the code as it stands now, which is not the
# code the run was opened against.
$probe = Join-Path $day.Orch "test-day.ps1"
$out = & powershell.exe -NoProfile -File $probe 2>&1
if ($LASTEXITCODE -ne 0) {
  foreach ($l in @($out | Where-Object { $_ -match 'FAIL' })) { Write-Host "    $l" }
  New-DayEvent -LogDir $day.LogDir -Kind "engine_failed" -Data @{ reason = "the engine's own probe is red" } | Out-Null
  # The ending is `end-day.ps1`'s and the whole of it is: the reason, the report and the release.
  # Writing two thirds of it here was harmless while the only run that could meet a red probe was
  # an empty one -- over a continued run it threw away the report of everything that had happened.
  New-DayEvent -LogDir $day.LogDir -Kind "no_more_cycles" -Data @{ reason = "engine: the probe is red" } | Out-Null
  Write-Atom @{ ok = $false; stop = "the engine's own probe is red" }
  exit 1
}

$resume = Get-ResumePoint -LogDir $day.LogDir

if ($day.Continued) {
  New-DayEvent -LogDir $day.LogDir -Kind "day_continued" -Data @{
    cycle = $day.Cycle; next = $resume.Next; reason = $resume.Reason
  } | Out-Null
  Write-Day $day "=== the day carries on: $($resume.Reason) ==="
} else {
  New-DayEvent -LogDir $day.LogDir -Kind "day_started" -Data @{ repo = $day.Repo } | Out-Null
  Write-Day $day "=== day starts ==="
}
New-DayEvent -LogDir $day.LogDir -Kind "engine_ok" | Out-Null

Write-Atom @{
  ok = $true
  action = $(if ($day.Continued) { "continued" } else { "started" })
  run = (Split-Path -Leaf $day.LogDir)
  cycle = $day.Cycle
  next = $resume.Next
  reason = $resume.Reason
}
