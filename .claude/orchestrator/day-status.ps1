#requires -Version 5.1
<#
  The reader. Says what the day is doing and which anomaly rules are firing, at any moment and
  with no AI in it. It launches nothing, judges no code, and does not decide whether something is
  worth interrupting a person over: that belongs to whoever is running the day.

  Nothing in the day depends on this. It is for a person who wants to look.

  The thresholds live in `day.psm1` and are thresholds, not judgement.

    .\day-status.ps1              # readable, the most recent run
    .\day-status.ps1 -Json        # the same state for a program to read
    .\day-status.ps1 -Report      # writes report.md and says where it landed
    .\day-status.ps1 -Run <path>  # an older run instead of the last one
#>
[CmdletBinding()]
param(
  [string]$Run,
  [switch]$Json,
  [switch]$Report
)

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "day.psm1") -Force

if (-not $Run) { $Run = Find-LatestRun -OrchestratorDir $PSScriptRoot }
if (-not $Run -or -not (Test-Path $Run)) {
  if ($Json) { '{"error":"no run with an event stream"}'; exit 1 }
  Write-Host "No run has an event stream. Was the day ever launched?"
  exit 1
}

$st = Get-DayStatus -LogDir $Run

if ($Report) {
  $p = Write-DayReport -LogDir $Run
  if ($Json) { [pscustomobject]@{ report = $p } | ConvertTo-Json -Compress } else { Write-Host $p }
  exit 0
}

if ($Json) {
  # A high -Depth or the denial detail and the anomalies come out as System.Object[].
  $st | ConvertTo-Json -Depth 8
  exit 0
}

# --- readable ---------------------------------------------------------------------------------

function Line([string]$Label, $Value) { Write-Host ("{0,-13} {1}" -f $Label, $Value) }

Write-Host ""
Write-Host ("  " + (Split-Path -Leaf $Run)) -ForegroundColor Cyan
Write-Host ""

if ($st.Ended) {
  Line "state" ("ended - " + $st.EndReason)
} elseif ($st.Waiting) {
  # Ahead of Running on purpose. Between the question and the answer no session is up, so without
  # this the day reads as "between cycles" -- idle -- while it is in the one state that ends only
  # when a person acts.
  Line "state" ("waiting on you - {0} question(s), no timeout behind them" -f $st.Questions.Count)
} elseif ($st.Running) {
  $q = "just launched"
  if ($null -ne $st.QuietForMinutes) { $q = "{0:N1} min since it emitted anything" -f $st.QuietForMinutes }
  Line "state" ("running - {0} session of cycle {1}, {2}" -f $st.Role, $st.Cycle, $q)
} else {
  Line "state" ("between cycles - the last one was {0}" -f $st.Cycle)
}

Line "spent" ("{0:N2} USD over {1} cycle(s)" -f $st.Cost, $st.Cycles.Count)
if ($st.LastTool)  { Line "last thing" $st.LastTool }
if ($st.LastText)  { Line "last said" $st.LastText }
if ($st.ToolCalls) { Line "tool calls" ("{0} in this session" -f $st.ToolCalls) }
if ($st.Merged.Count -gt 0) { Line "merged" (($st.Merged | ForEach-Object { "#$_" }) -join ", ") }

if ($st.Cycles.Count -gt 0) {
  Write-Host ""
  foreach ($c in $st.Cycles) {
    $pr = "-"; if ($c.pr) { $pr = "#$($c.pr)" }
    $outcome = ""; if ($c.outcome) { $outcome = $c.outcome }
    $verdict = ""; if ($c.verdict) { $verdict = $c.verdict }
    Write-Host ("  cycle {0}  {1,-6} {2,-12} {3,-20} {4,6:N2} USD" -f $c.cycle, $pr, $outcome, $verdict, $c.cost)
  }
}

if ($st.Waiting) {
  Write-Host ""
  Write-Host "  WAITING ON YOU" -ForegroundColor Cyan
  foreach ($q in $st.Questions) {
    Write-Host ("   [{0}] cycle {1}  {2}" -f $q.id, $q.cycle, $q.question) -ForegroundColor Cyan
    # Only the audit's options carry an effect: a worker asks before there is a PR to have one.
    foreach ($o in @($q.options)) {
      $tail = ""; if ($o.effect) { $tail = "  ({0})" -f $o.effect }
      Write-Host ("     {0}{1}" -f $o.label, $tail) -ForegroundColor DarkCyan
    }
  }
}

if ($st.DenialsByTool.Count -gt 0) {
  Write-Host ""
  Write-Host "  PERMISSIONS DENIED" -ForegroundColor Red
  foreach ($g in $st.DenialsByTool) {
    Write-Host ("   {0} x{1}" -f $g.tool, $g.count) -ForegroundColor Red
    foreach ($c in ($g.commands | Select-Object -First 3)) { Write-Host ("     $c") -ForegroundColor DarkRed }
  }
}

Write-Host ""
if ($st.Anomalies.Count -eq 0) {
  Write-Host "  nothing abnormal" -ForegroundColor DarkGray
} else {
  foreach ($a in $st.Anomalies) {
    $color = "Yellow"
    if ($a.level -eq "stop") { $color = "Red" }
    Write-Host ("  [{0}] {1}" -f $a.code, $a.text) -ForegroundColor $color
  }
}
Write-Host ""
