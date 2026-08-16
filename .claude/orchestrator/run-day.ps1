#requires -Version 5.1
<#
  The orchestrator. It decides nothing about the code: it picks the moment, launches a fresh
  session, reads the audit's verdict and either continues or stops. Every judgement lives inside
  the sessions (.claude/skills/next-task and .claude/skills/audit-session).

  Each session starts with an empty context, which is what keeps the day from running out of one.

  docs/orchestrator.md says how this is operated and what each outcome means.
#>
[CmdletBinding()]
param(
  [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$env:PYTHONIOENCODING  = "utf-8"   # without this the ClickUp CLI dies printing accents

$Repo   = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Orch   = $PSScriptRoot
$LogDir = Join-Path $Orch ("log\" + (Get-Date -Format "yyyy-MM-dd_HHmmss"))
$DayLog = Join-Path $LogDir "day.log"
$Extra  = Join-Path $Orch "settings.json"
$Lock   = Join-Path $Orch "day.lock"

$CooldownSeconds = 600
$SessionTimeout  = [TimeSpan]::FromMinutes(90)

New-Item -ItemType Directory -Force $LogDir | Out-Null
$EmptyStdin = Join-Path $LogDir "empty-stdin"
New-Item -ItemType File -Force $EmptyStdin | Out-Null

# The scheduled task starts in C:\Windows\System32. Without this, `claude -p` finds no CLAUDE.md,
# no skills and no .claude/settings.json, and works as if the project did not exist.
Set-Location $Repo

function Write-Day([string]$Text) {
  $line = "{0}  {1}" -f (Get-Date -Format "HH:mm:ss"), $Text
  Write-Host $line
  Add-Content -Path $DayLog -Value $line -Encoding utf8
}

# Two runs over the same working tree collide: same checkout, same card, same files. A doc saying
# "do not run two" serialises nothing, so it is serialised here.
function Enter-Lock {
  if (Test-Path $Lock) {
    $owner = (Get-Content $Lock -Raw).Trim()
    $alive = Get-Process -Id $owner -ErrorAction SilentlyContinue
    if ($alive) { return "a day is already running (PID $owner)" }
    Write-Day "orphan lock from PID $owner -- discarded"
  }
  Set-Content -Path $Lock -Value $PID -Encoding ascii
  return ""
}

# Every command is judged by its own exit code. $ErrorActionPreference does not turn a native
# exe failure into an exception, so a `git` that never ran would read as a clean tree -- the
# observation tool failing, dressed up as a healthy state.
function Test-Preflight {
  $dirty = git -C $Repo status --porcelain
  if ($LASTEXITCODE -ne 0) { return "git status failed ($LASTEXITCODE)" }
  if ($dirty)              { return "the tree was left dirty" }

  $branch = git -C $Repo rev-parse --abbrev-ref HEAD
  if ($LASTEXITCODE -ne 0) { return "git rev-parse failed ($LASTEXITCODE)" }
  if ($branch -ne "main")  { return "left standing on $branch" }

  git -C $Repo fetch origin main --quiet
  if ($LASTEXITCODE -ne 0) { return "git fetch failed -- main never checked against origin" }

  git -C $Repo merge --ff-only origin/main --quiet
  if ($LASTEXITCODE -ne 0) { return "local main has diverged from origin" }

  return ""
}

# -ArgumentList joins its elements with a space and quotes nothing, so anything holding a space
# arrives as several arguments. The prompt is "/next-task <path>", which is exactly that case:
# unquoted, the session receives "/next-task" and treats the path as a stray positional.
function Quote([string]$Value) {
  if ($Value -match '\s') { return '"' + $Value + '"' }
  return $Value
}

# A `claude -p` hanging on something external hangs the whole day, and --max-budget-usd bounds
# tokens, not minutes. Launched with stdin at EOF and a clock over it.
#
# Two more Start-Process traps, both of which fail the FIRST real session and neither of which
# shows up in a dry run. -RedirectStandardInput does not take "NUL" or "\\.\NUL" -- it resolves
# them as relative paths and throws -- so stdin comes from a real empty file. And ExitCode stays
# $null unless .Handle is read before waiting, which would make `-ne 0` true on every clean exit
# and stop the day on its first success.
function Invoke-Session([string]$Prompt, [string]$OutFile) {
  if ($DryRun) { Write-Day "  DRY-RUN: claude -p `"$Prompt`""; return $null }

  $args = @("-p", (Quote $Prompt), "--output-format", "json",
            "--permission-mode", "acceptEdits",
            "--settings", (Quote $Extra), "--fallback-model", "sonnet")
  $p = Start-Process -FilePath "claude" -ArgumentList $args -NoNewWindow -PassThru `
                     -RedirectStandardOutput $OutFile -RedirectStandardInput $EmptyStdin
  $null = $p.Handle
  if (-not $p.WaitForExit($SessionTimeout.TotalMilliseconds)) {
    Write-Day "  the session passed $($SessionTimeout.TotalMinutes) min -- killed"
    try { $p.Kill() } catch { }
    return $null
  }
  if ($p.ExitCode -ne 0) { Write-Day "  claude exited with code $($p.ExitCode)"; return $null }
  # -Encoding UTF8 or the day log comes back mojibake: the CLI writes UTF-8 and Get-Content
  # defaults to the console codepage, which on this machine is cp1252.
  try { return (Get-Content $OutFile -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { return $null }
}

# Presence is not enough: in PowerShell the string "false" is truthy, so a verdict that said
# "hold" alongside a lying continue flag would have carried the day on. What the script actually
# obeys is validated by value; the rest is for whoever reads the log.
function Read-Contract([string]$Path, [string[]]$Required, [string]$Field, [string[]]$Allowed) {
  if (-not (Test-Path $Path)) { return @{ error = "the session wrote no $(Split-Path -Leaf $Path)" } }
  try { $o = Get-Content $Path -Raw -Encoding UTF8 | ConvertFrom-Json } catch { return @{ error = "unreadable JSON" } }

  $missing = @()
  foreach ($k in $Required) { if ($null -eq $o.$k) { $missing += $k } }
  if ($missing.Count -gt 0) { return @{ error = "missing fields: " + ($missing -join ", ") } }

  if ($Allowed -notcontains [string]$o.$Field) {
    return @{ error = "$Field='$($o.$Field)' is not a value in the contract" }
  }
  return @{ value = $o }
}

$HandoffKeys = @("outcome","task_id","pr_number","isc_closed","probes",
                 "decisions_deferred","left_out","skipped","blocked_reason","head_sha")
$VerdictKeys = @("verdict","reasons","unreported_decisions","isc_unproved",
                 "followups_created","actions_taken","audited_head_sha")

$stop = Enter-Lock
if ($stop -ne "") { Write-Host $stop; exit 1 }

try {
  # No session count and no dollar ceiling: the day runs until the work, the usage window or the
  # audit ends it. Those are real limits; a number picked in advance is a guess about them.
  Write-Day "=== day starts: runs until the board, the window or a verdict stops it ==="
  $spent = 0.0
  $i     = 0

  while ($true) {
    $i++

    $blocked = Test-Preflight
    if ($blocked -ne "") { Write-Day "[$i] preflight: $blocked -- day stops"; break }

    # --- worker ---------------------------------------------------------------------------
    $handoff = Join-Path $LogDir "handoff-$i.json"
    Remove-Item $handoff -ErrorAction SilentlyContinue   # so a stale file cannot pass as fresh

    Write-Day "[$i] worker: /next-task"
    $w = Invoke-Session "/next-task $handoff" (Join-Path $LogDir "worker-$i.result.json")
    if ($DryRun) { continue }

    if ($null -eq $w) { Write-Day "[$i] the worker left no result -- day stops"; break }
    $cost = 0.0
    if ($null -ne $w.total_cost_usd) { $cost = [double]$w.total_cost_usd }
    $spent += $cost
    Write-Day ("[$i] worker done: {0}  turns={1}  usd={2:N2}  running={3:N2}" -f `
               $w.subtype, $w.num_turns, $cost, $spent)

    # An error leaves unknown effects -- it may have moved a card or opened a PR. Repeating it
    # blind is what CLAUDE.md forbids for a job that may already have been charged, so the day
    # stops and a person picks it up.
    if ($w.is_error) { Write-Day "[$i] the worker ended in error -- day stops"; break }

    $r = Read-Contract $handoff $HandoffKeys "outcome" @("pr_opened","blocked","no_tasks")
    if ($r.error) { Write-Day "[$i] invalid handoff: $($r.error) -- day stops"; break }
    $h = $r.value

    Write-Day ("[$i] handoff: {0}  task={1}  pr=#{2}  deferred={3}  skipped={4}" -f `
               $h.outcome, $h.task_id, $h.pr_number, @($h.decisions_deferred).Count, @($h.skipped).Count)

    if ($h.outcome -eq "no_tasks") { Write-Day "[$i] no eligible tasks left -- day closes"; break }
    if ($h.outcome -eq "blocked")  { Write-Day "[$i] worker blocked: $($h.blocked_reason) -- day stops"; break }

    # --- audit ----------------------------------------------------------------------------
    $verdictFile = Join-Path $LogDir "verdict-$i.json"
    Remove-Item $verdictFile -ErrorAction SilentlyContinue

    Write-Day "[$i] auditing PR #$($h.pr_number) at $($h.head_sha)"
    $a = Invoke-Session "/audit-session $handoff $verdictFile" (Join-Path $LogDir "audit-$i.result.json")

    if ($null -eq $a) { Write-Day "[$i] the audit left no result -- day stops"; break }
    $cost = 0.0
    if ($null -ne $a.total_cost_usd) { $cost = [double]$a.total_cost_usd }
    $spent += $cost
    Write-Day ("[$i] audit done: usd={0:N2}  running={1:N2}" -f $cost, $spent)
    if ($a.is_error) { Write-Day "[$i] the audit ended in error -- day stops"; break }

    $r = Read-Contract $verdictFile $VerdictKeys "verdict" @("pass","pass_with_followup","hold")
    if ($r.error) { Write-Day "[$i] invalid verdict: $($r.error) -- day stops"; break }
    $v = $r.value

    if ($v.audited_head_sha -ne $h.head_sha) {
      Write-Day "[$i] the audit read $($v.audited_head_sha), the worker delivered $($h.head_sha) -- day stops"
      break
    }

    Write-Day ("[$i] VERDICT {0}  undeclared={1}  isc-unproved={2}  followups={3}" -f `
               $v.verdict.ToUpper(), @($v.unreported_decisions).Count, @($v.isc_unproved).Count,
               @($v.followups_created).Count)
    foreach ($x in $v.reasons)              { Write-Day "        $x" }
    foreach ($x in $v.unreported_decisions) { Write-Day "        undeclared: $($x.what)  [$($x.found_in)]" }
    foreach ($x in $v.actions_taken)        { Write-Day "        done: $x" }

    # One thing decides whether the day goes on, and it is the verdict.
    if ($v.verdict -eq "hold") { Write-Day "[$i] the audit halts the day"; break }

    # The verdict decides, the script acts. Integrating here rather than inside the audit keeps
    # it mechanical -- it cannot be forgotten, cannot happen twice, and lands in the day log --
    # and it is what lets the next session see this one: the preflight below fast-forwards local
    # main, so cycle N+1 branches from a base that already carries cycle N instead of stacking
    # another PR against code it cannot see.
    Write-Day "[$i] integrating PR #$($h.pr_number)"
    gh pr merge $h.pr_number --merge --delete-branch
    if ($LASTEXITCODE -ne 0) {
      Write-Day "[$i] the merge failed ($LASTEXITCODE) -- day stops, the PR is left open"
      break
    }
    Write-Day "[$i] PR #$($h.pr_number) integrated"

    Write-Day "[$i] cooling $($CooldownSeconds / 60) min"
    Start-Sleep -Seconds $CooldownSeconds
  }

  Write-Day ("=== day ends in cycle $i : {0:N2} USD ===" -f $spent)
  Write-Day "Open PRs waiting on the user:"
  gh pr list --state open --limit 20 | Tee-Object -FilePath $DayLog -Append
}
finally {
  Remove-Item $Lock -ErrorAction SilentlyContinue
}
