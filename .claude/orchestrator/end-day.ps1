#requires -Version 5.1
<#
  Closes the day: the ending on the stream, the report beside it, the claim released, and whatever
  journal was still open parked rather than left where the next run would overwrite it.

  It works out why on its own. A `stop` anomaly already on the stream is the reason; failing that,
  a worker that found nothing to take is; failing both, it was ended by hand. Nothing is passed in,
  because a reason on a command line is prose on a command line.

    .\end-day.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "atom.psm1") -Force -DisableNameChecking

$script:Day = $null
trap { Write-AtomCrash -Message $_.Exception.Message -Day $script:Day; exit 1 }

$day = Open-Day
$script:Day = $day
if ($day.Error -ne "") { Write-Host $day.Error; Write-Atom @{ ok = $false; reason = $day.Error }; exit 1 }

$st = Get-DayStatus -LogDir $day.LogDir
if ($st.Ended) {
  # Still releases. A run whose ending was written and whose unlock was not is a checkout nothing
  # can use for two and a half hours, and every retry used to report success on the way past it.
  Exit-DayLock -OrchestratorDir $day.Orch -LogDir $day.LogDir
  Write-Atom @{ ok = $true; already = $true; reason = $st.EndReason }
  exit 0
}

$reason = "ended by hand"
$stopped = @($st.Anomalies | Where-Object { $_.level -eq "stop" -and $_.code -ne "stopped" } | Select-Object -First 1)
$outcomes = @(Read-DayEvents $day.LogDir | Where-Object { $_.kind -eq "handoff" })
if ($outcomes.Count -gt 0 -and [string]$outcomes[-1].outcome -eq "no_tasks") {
  $reason = "no_tasks -- nothing left on the board this could take"
}
# An ending an atom reached without a session: nothing eligible before one was spent, or the park
# ceiling. It is written where the reason is worked out rather than only where it was decided,
# because the report is built from the stream and a reason that never reached it read as by hand.
$ended = @(Read-DayEvents $day.LogDir | Where-Object { $_.kind -eq "no_more_cycles" })
if ($ended.Count -gt 0) { $reason = [string]$ended[-1].reason }
if ($stopped.Count -gt 0) { $reason = "$($stopped[0].code): $($stopped[0].text)" }

$parked = Complete-Journal -Repo $day.Repo
if ($parked) { New-DayEvent -LogDir $day.LogDir -Kind "journal_parked" -Data @{ to = $parked } | Out-Null }

# What is open and waiting on a person -- an enrichment, and wrapped like one. `gh` missing from
# PATH raises rather than returning an exit code, and the raise used to go past the ending, the
# report and the unlock: the one call here that nothing depends on decided whether the day could
# close at all.
$prs = @()
try {
  $raw = gh pr list --state open --limit 20 --json number,title,headRefName 2>$null | Out-String
  if ($LASTEXITCODE -eq 0 -and $raw.Trim()) {
    # Projected one by one rather than @($raw | ConvertFrom-Json): on 5.1 that wraps the array in
    # its own PSObject and what lands on the event is {"value":[...],"Count":1}.
    foreach ($p in (ConvertFrom-Json $raw)) {
      $prs += [pscustomobject]@{ number = $p.number; title = [string]$p.title; branch = [string]$p.headRefName }
    }
  }
} catch { $prs = @() }
New-DayEvent -LogDir $day.LogDir -Kind "open_prs" -Data @{ prs = $prs } | Out-Null

New-DayEvent -LogDir $day.LogDir -Kind "day_ended" -Data @{ reason = $reason } | Out-Null
Write-Day $day "=== the day ends: $reason ==="

# The unlock comes before the report on purpose. A report that cannot be written is a page nobody
# reads; a lock that is never released is a checkout nothing can use.
Exit-DayLock -OrchestratorDir $day.Orch -LogDir $day.LogDir
$report = Write-DayReport -LogDir $day.LogDir
Write-Day $day "report: $report"

$final = Get-DayStatus -LogDir $day.LogDir
Write-Atom @{
  ok = $true; reason = $reason; report = $report
  cycles = $final.Cycles.Count; usd = [math]::Round($final.Cost, 2)
  merged = @($final.Merged); open_prs = @($prs | ForEach-Object { $_.number })
}
