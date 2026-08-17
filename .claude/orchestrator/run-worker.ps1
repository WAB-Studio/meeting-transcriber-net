#requires -Version 5.1
<#
  One working session: the checkout is checked, a fresh journal is laid down, `/next-task` runs, and
  what it emitted becomes this cycle's handoff.

    .\run-worker.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$env:PYTHONIOENCODING  = "utf-8"
Import-Module (Join-Path $PSScriptRoot "atom.psm1") -Force -DisableNameChecking

$script:Day = $null
trap { Write-AtomCrash -Message $_.Exception.Message -Day $script:Day; exit 1 }

$HandoffKeys = @("outcome","task_id","pr_number","isc_closed","probes",
                 "decisions_deferred","left_out","skipped","blocked_reason","head_sha")

$day = Open-Day
$script:Day = $day
if ($day.Error -ne "") { Write-Host $day.Error; Write-Atom @{ ok = $false; reason = $day.Error }; exit 1 }

# Every command is judged by its own exit code. $ErrorActionPreference does not turn a native exe
# failure into an exception, so a `git` that never ran would read as a clean tree -- the observing
# tool failing, dressed up as a healthy state.
function Test-Preflight {
  $dirty = git -C $day.Repo status --porcelain
  if ($LASTEXITCODE -ne 0) { return "git status failed ($LASTEXITCODE)" }
  if ($dirty)              { return "the tree was left dirty" }
  $branch = git -C $day.Repo rev-parse --abbrev-ref HEAD
  if ($LASTEXITCODE -ne 0) { return "git rev-parse failed ($LASTEXITCODE)" }
  if ($branch -ne "main")  { return "left standing on $branch" }
  git -C $day.Repo fetch origin main --quiet
  if ($LASTEXITCODE -ne 0) { return "git fetch failed -- main never checked against origin" }
  git -C $day.Repo merge --ff-only origin/main --quiet
  if ($LASTEXITCODE -ne 0) { return "local main has diverged from origin" }
  return ""
}

$blocked = Test-Preflight
if ($blocked -ne "") {
  New-DayEvent -LogDir $day.LogDir -Kind "preflight_failed" -Data @{ reason = $blocked } | Out-Null
  Write-Day $day "preflight: $blocked"
  Write-Atom @{ ok = $false; stop = "preflight: $blocked" }
  exit 1
}
New-DayEvent -LogDir $day.LogDir -Kind "preflight_ok" | Out-Null

# A cycle that was never closed is not replaced by opening another one. What sequences these is a
# model, so calling this again after an audit or a close was skipped is not a hypothetical -- and
# the second worker would take a new card while the first one's PR sat open with its card in
# `in progress`, which is the abandonment the whole arrangement exists to prevent.
if ($day.Cycle -gt 0) {
  $open = Read-Contract $day "handoff-$($day.Cycle).json"
  if ($open -and @("pr_opened", "ask") -contains [string]$open.outcome -and
      -not (Test-CycleEvent -LogDir $day.LogDir -Cycle $day.Cycle -Kinds @("merged","recovered","answered_before_building"))) {
    Write-Atom @{ ok = $false; reason = "cycle $($day.Cycle) is still open -- close it before opening another" }
    exit 1
  }
}

$cycle = $day.Cycle + 1
$day.Cycle = $cycle

# Laid down before the session, so the session only adds prose under headings already there.
# Whatever a dead cycle left is parked first rather than overwritten.
$parked = Reset-Journal -Repo $day.Repo
if ($parked) {
  New-DayEvent -LogDir $day.LogDir -Kind "journal_parked" -Data @{ to = $parked } | Out-Null
  Write-Day $day "a journal from before was parked: $parked"
}

$handoff = Join-Path $day.LogDir "handoff-$cycle.json"
Remove-Item $handoff -ErrorAction SilentlyContinue   # so a stale file cannot pass as fresh

Write-Day $day "[$cycle] worker: /next-task"
$w = Invoke-Session -Day $day -Role "worker" -Prompt "/next-task $handoff" -Cycle $cycle
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
$c = Get-ContractFromText ([string]$w.result)
$bad = Test-DayContract -Contract $c -Required $HandoffKeys -Field "outcome" -Allowed @("pr_opened","ask","blocked","no_tasks")
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

# A fork the card did not settle, met before anything was built. It costs a short session instead
# of a whole diff, which is the entire reason a worker is allowed to ask at all.
if ([string]$c.outcome -eq "ask") {
  $unreadable = Test-DayAsk @($c.questions)
  if ($unreadable -ne "") {
    New-DayEvent -LogDir $day.LogDir -Kind "handoff_invalid" -Data @{ cycle = $cycle; reason = "it asks, but $unreadable" } | Out-Null
    Write-Atom @{ ok = $false; stop = "the worker asked for a decision but $unreadable" }
    exit 1
  }
  # Cleared as the question is published and never afterwards. The answers land at a fixed path, so
  # one left behind by a cycle that died is a file the next question would read as its own -- and a
  # label that happens to match would decide something nobody was asked.
  Remove-Item (Join-Path $day.Repo ".scratch\answers.json") -Force -ErrorAction SilentlyContinue

  foreach ($q in @($c.questions)) {
    New-DayEvent -LogDir $day.LogDir -Kind "question_asked" -Data @{
      cycle = $cycle; id = [string]$q.id; question = [string]$q.question; why = [string]$q.why
      options = @($q.options); task_id = [string]$c.task_id
    } | Out-Null
    Write-Day $day "  ? [$($q.id)] $([string]$q.question)"
  }
  Write-Atom @{ ok = $true; cycle = $cycle; action = "ask"; task_id = [string]$c.task_id; questions = @($c.questions) }
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
