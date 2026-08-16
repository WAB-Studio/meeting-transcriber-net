#requires -Version 5.1
<#
  El orquestador. No decide nada sobre el codigo: elige el momento, lanza una sesion fresca,
  lee el veredicto de la auditoria y sigue o para. Todo el juicio vive adentro de las sesiones
  (.claude/skills/next-task y .claude/skills/audit-session).

  Cada sesion arranca con contexto en cero, que es lo que hace que el dia no se quede sin tokens.

  docs/orchestrator.md explica como se opera y que significa cada salida.
#>
[CmdletBinding()]
param(
  [int]$MaxSessions      = 4,
  [int]$CooldownMinutes  = 10,
  [double]$MaxUsdSession = 15,
  [double]$MaxUsdDay     = 60,
  [string]$Model         = "opus",
  [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$env:PYTHONIOENCODING  = "utf-8"   # sin esto el CLI de ClickUp crashea al imprimir acentos

$Repo   = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Orch   = $PSScriptRoot
$LogDir = Join-Path $Orch ("log\" + (Get-Date -Format "yyyy-MM-dd"))
$DayLog = Join-Path $LogDir "day.log"
$Extra  = Join-Path $Orch "settings.json"

New-Item -ItemType Directory -Force $LogDir | Out-Null

function Write-Day([string]$Text) {
  $line = "{0}  {1}" -f (Get-Date -Format "HH:mm:ss"), $Text
  Write-Host $line
  Add-Content -Path $DayLog -Value $line -Encoding utf8
}

# El arbol tiene que estar como lo dejaria una sesion que termino bien. Si no lo esta, el dia
# para: una sesion nueva sobre un arbol sucio construye encima de trabajo a medias.
function Test-Preflight {
  if (git -C $Repo status --porcelain) { return "el arbol quedo sucio" }
  $branch = git -C $Repo rev-parse --abbrev-ref HEAD
  if ($branch -ne "main") { return "quedo parado en $branch" }
  git -C $Repo fetch origin main --quiet
  git -C $Repo merge --ff-only origin/main --quiet
  if ($LASTEXITCODE -ne 0) { return "main local diverge de origin" }
  return ""
}

# Una sesion. Devuelve el objeto result de Claude Code, o $null si el proceso no dejo JSON.
function Invoke-Session([string]$Prompt, [string]$OutFile) {
  if ($DryRun) { Write-Day "  DRY-RUN: claude -p `"$Prompt`""; return $null }
  & claude -p $Prompt `
    --output-format json `
    --permission-mode acceptEdits `
    --settings $Extra `
    --model $Model `
    --fallback-model sonnet `
    --max-budget-usd $MaxUsdSession | Out-File -FilePath $OutFile -Encoding utf8
  if (-not (Test-Path $OutFile)) { return $null }
  try { return (Get-Content $OutFile -Raw | ConvertFrom-Json) } catch { return $null }
}

# Un handoff o un veredicto que no tiene la forma acordada no se interpreta con buena voluntad:
# es una sesion que no termino de decir que hizo, y eso frena el dia igual que un rojo.
function Test-Shape($Obj, [string[]]$Required) {
  if ($null -eq $Obj) { return "no se pudo leer el JSON" }
  $missing = @()
  foreach ($k in $Required) { if ($null -eq $Obj.$k) { $missing += $k } }
  if ($missing.Count -gt 0) { return "faltan campos: " + ($missing -join ", ") }
  return ""
}

$HandoffKeys = @("outcome","task_id","task_name","task_list","branch","pr_number","pr_url",
                 "isc_closed","probes","decisions_deferred","left_out","skipped","blocked_reason")
$VerdictKeys = @("verdict","continue_day","reasons","unreported_decisions","isc_unproved",
                 "scope_gap","followups_created","actions_taken")

Write-Day "=== arranca el dia: hasta $MaxSessions sesiones, techo $MaxUsdDay USD ==="

$spent   = 0.0
$backoff = 0

for ($i = 1; $i -le $MaxSessions; $i++) {

  $blocked = Test-Preflight
  if ($blocked -ne "") { Write-Day "[$i] preflight: $blocked -- para el dia"; break }

  if ($spent -ge $MaxUsdDay) { Write-Day "[$i] techo del dia alcanzado ($spent USD) -- para"; break }

  # --- worker -------------------------------------------------------------------------------
  $handoff = Join-Path $LogDir "handoff-$i.json"
  Write-Day "[$i] worker: /next-task"
  $w = Invoke-Session "/next-task $handoff" (Join-Path $LogDir "worker-$i.result.json")
  if ($DryRun) { continue }

  if ($null -eq $w) { Write-Day "[$i] el worker no dejo resultado -- para el dia"; break }
  $spent += [double]$w.total_cost_usd
  Write-Day ("[$i] worker termino: {0}  turnos={1}  usd={2:N2}  acumulado={3:N2}" -f `
             $w.subtype, $w.num_turns, $w.total_cost_usd, $spent)

  if ($w.is_error) {
    # Casi siempre es el limite de uso. Esperar la ventana y reintentar la misma sesion.
    $backoff++
    if ($backoff -gt 3) { Write-Day "[$i] tres backoffs seguidos -- para el dia"; break }
    Write-Day "[$i] error de la API o limite de uso -- espera 60 min (backoff $backoff/3)"
    Start-Sleep -Seconds 3600
    $i--
    continue
  }
  $backoff = 0

  $h = $null
  if (Test-Path $handoff) { try { $h = Get-Content $handoff -Raw | ConvertFrom-Json } catch { } }
  $bad = Test-Shape $h $HandoffKeys
  if ($bad -ne "") { Write-Day "[$i] handoff invalido ($bad) -- para el dia"; break }

  Write-Day ("[$i] handoff: {0}  tarea='{1}'  pr=#{2}  diferidas={3}  salteadas={4}" -f `
             $h.outcome, $h.task_name, $h.pr_number, @($h.decisions_deferred).Count, @($h.skipped).Count)

  if ($h.outcome -eq "no_tasks") { Write-Day "[$i] no quedan tareas elegibles -- cierra el dia"; break }
  if ($h.outcome -eq "blocked")  { Write-Day "[$i] worker trabado: $($h.blocked_reason) -- para el dia"; break }

  # --- auditoria ----------------------------------------------------------------------------
  $verdictFile = Join-Path $LogDir "verdict-$i.json"
  Write-Day "[$i] auditoria del PR #$($h.pr_number)"
  $a = Invoke-Session "/audit-session $handoff $verdictFile" (Join-Path $LogDir "audit-$i.result.json")

  if ($null -eq $a) { Write-Day "[$i] la auditoria no dejo resultado -- para el dia"; break }
  $spent += [double]$a.total_cost_usd
  Write-Day ("[$i] auditoria termino: usd={0:N2}  acumulado={1:N2}" -f $a.total_cost_usd, $spent)

  $v = $null
  if (Test-Path $verdictFile) { try { $v = Get-Content $verdictFile -Raw | ConvertFrom-Json } catch { } }
  $bad = Test-Shape $v $VerdictKeys
  if ($bad -ne "") { Write-Day "[$i] veredicto invalido ($bad) -- para el dia"; break }

  Write-Day ("[$i] VEREDICTO {0}  no-declaradas={1}  isc-sin-probe={2}  followups={3}" -f `
             $v.verdict.ToUpper(), @($v.unreported_decisions).Count, @($v.isc_unproved).Count,
             @($v.followups_created).Count)
  foreach ($r in $v.reasons) { Write-Day "        - $r" }

  if (-not $v.continue_day) { Write-Day "[$i] la auditoria frena el dia"; break }

  if ($i -lt $MaxSessions) {
    Write-Day "[$i] enfria $CooldownMinutes min"
    Start-Sleep -Seconds ($CooldownMinutes * 60)
  }
}

Write-Day ("=== termina el dia: {0:N2} USD ===" -f $spent)
Write-Day "PRs abiertos esperando al usuario:"
gh pr list --state open --limit 20 | Tee-Object -FilePath $DayLog -Append
