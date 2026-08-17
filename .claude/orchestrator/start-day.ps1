#requires -Version 5.1
<#
  Opens a day: a folder for its stream, a claim on the checkout, and a probe over the engine before
  anything is spent. Refuses if a day is already open.

    .\start-day.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$env:PYTHONIOENCODING  = "utf-8"   # without this the board CLI dies printing accents
Import-Module (Join-Path $PSScriptRoot "atom.psm1") -Force -DisableNameChecking

$day = New-DayRun
if ($day.Error -ne "") {
  Write-Host $day.Error
  Write-Atom @{ ok = $false; reason = $day.Error }
  exit 1
}

# The engine is checked before a session is launched rather than in the product's CI, which has no
# business knowing this exists. It takes a second and it fails at the only moment the answer is
# worth having: before a broken loop costs an hour and twenty dollars.
$probe = Join-Path $day.Orch "test-day.ps1"
$out = & powershell.exe -NoProfile -File $probe 2>&1
if ($LASTEXITCODE -ne 0) {
  foreach ($l in @($out | Where-Object { $_ -match 'FAIL' })) { Write-Host "    $l" }
  New-DayEvent -LogDir $day.LogDir -Kind "engine_failed" -Data @{ reason = "the engine's own probe is red" } | Out-Null
  New-DayEvent -LogDir $day.LogDir -Kind "day_ended" -Data @{ reason = "engine: the probe is red" } | Out-Null
  Exit-DayLock -OrchestratorDir $day.Orch
  Write-Atom @{ ok = $false; reason = "the engine's own probe is red" }
  exit 1
}

New-DayEvent -LogDir $day.LogDir -Kind "day_started" -Data @{ repo = $day.Repo } | Out-Null
New-DayEvent -LogDir $day.LogDir -Kind "engine_ok" | Out-Null
Write-Day $day "=== day starts ==="

Write-Atom @{ ok = $true; run = (Split-Path -Leaf $day.LogDir) }
