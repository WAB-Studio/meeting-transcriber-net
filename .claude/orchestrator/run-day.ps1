#requires -Version 5.1
<#
  The orchestrator. It decides nothing about the code: it picks the moment, launches a fresh
  session, reads the audit's verdict and either continues or stops. Every judgement lives inside
  the sessions (.claude/skills/next-task and .claude/skills/audit-session).

  Each session starts with an empty context, which is what keeps the day from running out of one.

  What this file writes is `events.jsonl` - one line per transition, and the run's only source of
  truth. `day.log` is a render to read down the side of, and `report.md` comes off the same stream
  at close. Anybody wanting to know what the day is doing while it runs does not read this: they
  run `day-status.ps1`, or ask Claude to supervise, which is what the `supervise-day` skill is.

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
Import-Module (Join-Path $Orch "day.psm1") -Force

$EmptyStdin = Join-Path $LogDir "empty-stdin"
New-Item -ItemType File -Force $EmptyStdin | Out-Null

# The scheduled task starts in C:\Windows\System32. Without this, `claude -p` finds no CLAUDE.md,
# no skills and no .claude/settings.json, and works as if the project did not exist.
Set-Location $Repo

function Write-Day([string]$Text) {
  $line = "{0}  {1}" -f (Get-Date -Format "HH:mm:ss"), $Text
  Write-Host $line
  Add-Utf8Line -Path $DayLog -Text $line
}

function Stop-Day([string]$Reason) {
  New-DayEvent -LogDir $LogDir -Kind "day_ended" -Data @{ reason = $Reason } | Out-Null
  Write-Day "=== the day ends: $Reason ==="
}

<#
  Two runs over the same working tree collide: same checkout, same card, same files. A doc saying
  "do not run two" serialises nothing, so it is serialised here.

  The handle is opened exclusively and held for the life of the day, which is the whole mechanism:
  Test-Path followed by Set-Content is a check-then-create, and two launches in the same second --
  the scheduled one and yours -- can both see no lock and both proceed. Holding it also means the
  OS releases it when the process dies however it dies, so a stale file is one that can simply be
  opened again rather than one whose PID has to be believed.
#>
$script:LockHandle = $null

function Enter-Lock {
  try {
    $script:LockHandle = [System.IO.File]::Open(
      $Lock, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::Write,
      [System.IO.FileShare]::None)
  } catch {
    $owner = "?"
    try { $owner = (Get-Content $Lock -Raw -ErrorAction SilentlyContinue).Trim() } catch { }
    return "a day is already running (PID $owner)"
  }
  $script:LockHandle.SetLength(0)
  $bytes = [System.Text.Encoding]::ASCII.GetBytes([string]$PID)
  $script:LockHandle.Write($bytes, 0, $bytes.Length)
  $script:LockHandle.Flush()
  return ""
}

function Exit-Lock {
  if ($script:LockHandle) {
    try { $script:LockHandle.Dispose() } catch { }
    $script:LockHandle = $null
  }
  Remove-Item $Lock -ErrorAction SilentlyContinue
}

<#
  The day checks its own engine before spending anything on a session. It takes a second, it needs
  no `claude`, and it fails at the only moment the answer is worth having: before a broken loop
  costs an hour and twenty-six dollars. This is dev tooling for one machine, so it is checked where
  it runs rather than in the product's CI, which has no business knowing this exists.
#>
function Test-Engine {
  $probe = Join-Path $Orch "test-day.ps1"
  if (-not (Test-Path $probe)) { return "test-day.ps1 is missing" }
  $out = & powershell.exe -NoProfile -File $probe 2>&1
  if ($LASTEXITCODE -ne 0) {
    foreach ($l in @($out | Where-Object { $_ -match 'FAIL|red' })) { Write-Day "    $l" }
    return "the engine's own probe is red"
  }
  return ""
}

# Every command is judged by its own exit code. $ErrorActionPreference does not turn a native exe
# failure into an exception, so a `git` that never ran would read as a clean tree -- the
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

<#
  A `claude -p` hanging on something external hangs the whole day, and --max-budget-usd bounds
  tokens, not minutes. Launched with stdin at EOF and a clock over it.

  Two more Start-Process traps, both of which fail the FIRST real session and neither of which
  shows up in a dry run. -RedirectStandardInput does not take "NUL" or "\\.\NUL" -- it resolves
  them as relative paths and throws -- so stdin comes from a real empty file. And ExitCode stays
  $null unless .Handle is read before waiting, which would make `-ne 0` true on every clean exit
  and stop the day on its first success.

  The output is stream-json: the file grows while the session runs, which is what makes the day
  watchable at all. The result is the LAST line, not the file -- `Get-SessionResult`.
#>
function Invoke-Session {
  param(
    [Parameter(Mandatory)][string]$Prompt,
    [Parameter(Mandatory)][string]$StreamPath,
    [Parameter(Mandatory)][string]$Role,
    [Parameter(Mandatory)][int]$Cycle
  )

  $args = @("-p", (Quote $Prompt), "--output-format", "stream-json", "--verbose",
            "--permission-mode", "acceptEdits",
            "--settings", (Quote $Extra), "--fallback-model", "sonnet")

  New-DayEvent -LogDir $LogDir -Kind "session_started" -Data @{
    cycle = $Cycle; role = $Role; stream = (Split-Path -Leaf $StreamPath); prompt = $Prompt
  } | Out-Null

  # A session that died is the case where what it collected matters most: 27 denials and then a
  # timeout is a worse day than 27 denials and a clean exit, and reading them only off a healthy
  # result would throw away exactly the ones worth having.
  function Close-Session([string]$Kind, [string]$Reason) {
    $a = Get-SessionActivity $StreamPath
    New-DayEvent -LogDir $LogDir -Kind $Kind -Data @{
      cycle = $Cycle; role = $Role; reason = $Reason
      denials = $a.Denials; denial_detail = @($a.DenialDetail); rate_limit = $a.RateLimit
    } | Out-Null
  }

  $started = Get-Date
  try {
    $p = Start-Process -FilePath "claude" -ArgumentList $args -NoNewWindow -PassThru `
                       -RedirectStandardOutput $StreamPath -RedirectStandardInput $EmptyStdin
    $null = $p.Handle
  } catch {
    # `claude` missing from PATH, or a redirection that cannot be opened. Without this the
    # exception escapes past an already-written session_started, and every later reader believes a
    # session is running that was never launched.
    Write-Day "  the session could not be launched: $($_.Exception.Message)"
    Close-Session "session_failed" "could not be launched: $($_.Exception.Message)"
    return $null
  }

  if (-not $p.WaitForExit($SessionTimeout.TotalMilliseconds)) {
    Write-Day "  the session passed $($SessionTimeout.TotalMinutes) min -- killed"
    try { $p.Kill() } catch { }
    Close-Session "session_killed" "passed $($SessionTimeout.TotalMinutes) min"
    return $null
  }
  if ($p.ExitCode -ne 0) {
    Write-Day "  claude exited with code $($p.ExitCode)"
    Close-Session "session_failed" "exit $($p.ExitCode)"
    return $null
  }

  $r = Get-SessionResult $StreamPath
  if ($null -eq $r) {
    Close-Session "session_failed" "the stream carries no result line"
    return $null
  }

  $denials = @(Get-ResultDenials $r)
  # The window is only ever announced mid-stream, so it is carried onto the event or it is lost the
  # moment the session closes -- and it is one of the three things that legitimately end a day.
  $live = Get-SessionActivity $StreamPath
  New-DayEvent -LogDir $LogDir -Kind "session_ended" -Data @{
    cycle         = $Cycle
    role          = $Role
    subtype       = [string]$r.subtype
    is_error      = [bool]$r.is_error
    turns         = [int]$r.num_turns
    cost          = [double]$r.total_cost_usd
    seconds       = [int]((Get-Date) - $started).TotalSeconds
    denials       = $denials.Count
    denial_detail = $denials
    rate_limit    = $live.RateLimit
  } | Out-Null

  # The loudest thing this script prints, because it is what costs the most without being seen: a
  # denial does not stop the session, it sends it to do the same job by a worse route.
  if ($denials.Count -gt 0) {
    Write-Day "  !! $($denials.Count) permission(s) denied in this session -- whatever it did instead, nobody asked for"
    foreach ($g in (Group-Denials $denials)) {
      Write-Day ("     {0} x{1}: {2}" -f $g.tool, $g.count, ($g.commands | Select-Object -First 1))
    }
  }

  return $r
}

<#
  The reader says what is out of range and the script acts on it, which is the same split as the
  verdict: labelling a condition `stop` and then merging anyway would make the level decoration.

  Called after every session and before anything irreversible -- before the audit, and before the
  merge -- so a worker that spent the session groping around a denied permission never reaches
  `main`. Whatever it did instead of the thing it was refused, nobody asked for it.
#>
function Test-Halt {
  param([Parameter(Mandatory)]$Status)
  $halt = @(Get-HaltingAnomalies -Status $Status)
  if ($halt.Count -eq 0) { return "" }
  foreach ($a in $halt) {
    Write-Day "  !! [$($a.code)] $($a.text)"
    New-DayEvent -LogDir $LogDir -Kind "anomaly" -Data @{
      level = $a.level; code = $a.code; text = $a.text
    } | Out-Null
  }
  return ($halt | ForEach-Object { $_.code }) -join ", "
}

# Anomalies that do not halt still go on the stream, because most of them cannot be recomputed once
# the state that produced them is gone, and the morning report promises every one that fired.
function Save-Anomalies {
  param([Parameter(Mandatory)]$Status)
  foreach ($a in @($Status.Anomalies | Where-Object { $_.level -ne "stop" })) {
    New-DayEvent -LogDir $LogDir -Kind "anomaly" -Data @{
      level = $a.level; code = $a.code; text = $a.text
    } | Out-Null
    Write-Day "  [$($a.code)] $($a.text)"
  }
}

$HandoffKeys = @("outcome","task_id","pr_number","isc_closed","probes",
                 "decisions_deferred","left_out","skipped","blocked_reason","head_sha")
$VerdictKeys = @("verdict","reasons","unreported_decisions","isc_unproved",
                 "followups_created","actions_taken","audited_head_sha")

<#
  The contract is derived from what the session emitted, and this script writes the file. Having
  the session also remember to write it was a second way to fail without being a second guarantee:
  on 2026-08-16 a worker said the whole handoff, did not write it, and the day died with the PR
  open and the work done.
#>
function Read-SessionContract {
  param($Result, [string]$Path, [string[]]$Required, [string]$Field, [string[]]$Allowed)

  $c = Get-ContractFromText ([string]$Result.result)
  $err = Test-DayContract -Contract $c -Required $Required -Field $Field -Allowed $Allowed
  if ($err -ne "") { return @{ error = $err } }

  [System.IO.File]::WriteAllText($Path, ($c | ConvertTo-Json -Depth 12), (New-Object System.Text.UTF8Encoding($false)))
  return @{ value = $c }
}

$stop = Enter-Lock
if ($stop -ne "") { Write-Host $stop; exit 1 }

try {
  # No session count and no dollar ceiling: the day runs until the work, the usage window or the
  # audit ends it. Those are real limits; a number picked in advance is a guess about them.
  New-DayEvent -LogDir $LogDir -Kind "day_started" -Data @{ repo = $Repo; dry_run = [bool]$DryRun; pid = $PID } | Out-Null
  Write-Day "=== day starts: runs until the board, the window or a verdict stops it ==="
  $i = 0

  $broken = Test-Engine
  if ($broken -ne "") {
    New-DayEvent -LogDir $LogDir -Kind "engine_failed" -Data @{ reason = $broken } | Out-Null
    Write-Day "engine: $broken"
    Stop-Day "engine: $broken"
    Write-Day ("report: " + (Write-DayReport -LogDir $LogDir))
    return
  }
  New-DayEvent -LogDir $LogDir -Kind "engine_ok" | Out-Null

  while ($true) {
    $i++

    $blocked = Test-Preflight
    if ($blocked -ne "") {
      New-DayEvent -LogDir $LogDir -Kind "preflight_failed" -Data @{ cycle = $i; reason = $blocked } | Out-Null
      Write-Day "[$i] preflight: $blocked"
      Stop-Day "preflight: $blocked"
      break
    }
    New-DayEvent -LogDir $LogDir -Kind "preflight_ok" -Data @{ cycle = $i } | Out-Null

    $handoff = Join-Path $LogDir "handoff-$i.json"
    Remove-Item $handoff -ErrorAction SilentlyContinue   # so a stale file cannot pass as fresh

    # The dry run is one run, not a series: it shows the preflight and what it would launch, then
    # exits. With a clean tree there is nothing further to check without spending a session.
    if ($DryRun) {
      Write-Day "[$i] preflight ok"
      Write-Day "[$i] would launch: claude -p `"/next-task $handoff`" --output-format stream-json --verbose"
      Stop-Day "dry run"
      break
    }

    # --- worker ---------------------------------------------------------------------------
    Write-Day "[$i] worker: /next-task"
    $w = Invoke-Session -Prompt "/next-task $handoff" -StreamPath (Join-Path $LogDir "worker-$i.stream.jsonl") `
                        -Role "worker" -Cycle $i
    if ($null -eq $w) { Stop-Day "the worker left no result"; break }

    $status = Get-DayStatus -LogDir $LogDir
    Write-Day ("[$i] worker done: {0}  turns={1}  usd={2:N2}  running={3:N2}" -f `
               $w.subtype, $w.num_turns, $w.total_cost_usd, $status.Cost)

    # An error leaves unknown effects -- it may have moved a card or opened a PR. Repeating it
    # blind is what CLAUDE.md forbids for a job that may already have been charged.
    if ($w.is_error) { Stop-Day "the worker ended in error"; break }

    $halted = Test-Halt -Status $status
    if ($halted -ne "") { Stop-Day "the worker's session is not sound: $halted"; break }
    Save-Anomalies -Status $status

    $r = Read-SessionContract -Result $w -Path $handoff -Required $HandoffKeys `
                              -Field "outcome" -Allowed @("pr_opened","blocked","no_tasks")
    if ($r.error) {
      New-DayEvent -LogDir $LogDir -Kind "handoff_invalid" -Data @{ cycle = $i; reason = $r.error } | Out-Null
      Stop-Day "invalid handoff: $($r.error)"
      break
    }
    $h = $r.value

    New-DayEvent -LogDir $LogDir -Kind "handoff" -Data @{
      cycle = $i; outcome = [string]$h.outcome; task_id = [string]$h.task_id
      pr_number = $h.pr_number; head_sha = [string]$h.head_sha
      deferred = @($h.decisions_deferred).Count; skipped = @($h.skipped).Count
      # Test-JsonTrue and not `-not $_.passed`: a probe that came back as the string "false" is
      # truthy in PowerShell, and would be counted as green in the one place that counts red ones.
      probes_red = @($h.probes | Where-Object { -not (Test-JsonTrue $_.passed) }).Count
      left_out = @($h.left_out); deferred_detail = @($h.decisions_deferred); skipped_detail = @($h.skipped)
    } | Out-Null

    Write-Day ("[$i] handoff: {0}  task={1}  pr=#{2}  deferred={3}  skipped={4}" -f `
               $h.outcome, $h.task_id, $h.pr_number, @($h.decisions_deferred).Count, @($h.skipped).Count)

    if ($h.outcome -eq "no_tasks") { Stop-Day "no_tasks"; break }
    if ($h.outcome -eq "blocked")  { Stop-Day "worker blocked: $($h.blocked_reason)"; break }

    # --- audit ------------------------------------------------------------------------------
    $verdictFile = Join-Path $LogDir "verdict-$i.json"
    Remove-Item $verdictFile -ErrorAction SilentlyContinue

    Write-Day "[$i] auditing PR #$($h.pr_number) at $($h.head_sha)"
    $a = Invoke-Session -Prompt "/audit-session $handoff $verdictFile" `
                        -StreamPath (Join-Path $LogDir "audit-$i.stream.jsonl") -Role "audit" -Cycle $i
    if ($null -eq $a) { Stop-Day "the audit left no result"; break }

    $status = Get-DayStatus -LogDir $LogDir
    Write-Day ("[$i] audit done: usd={0:N2}  running={1:N2}" -f $a.total_cost_usd, $status.Cost)
    if ($a.is_error) { Stop-Day "the audit ended in error"; break }

    $halted = Test-Halt -Status $status
    if ($halted -ne "") { Stop-Day "the audit's session is not sound: $halted"; break }
    Save-Anomalies -Status $status

    $r = Read-SessionContract -Result $a -Path $verdictFile -Required $VerdictKeys `
                              -Field "verdict" -Allowed @("pass","pass_with_followup","hold")
    if ($r.error) {
      New-DayEvent -LogDir $LogDir -Kind "verdict_invalid" -Data @{ cycle = $i; reason = $r.error } | Out-Null
      Stop-Day "invalid verdict: $($r.error)"
      break
    }
    $v = $r.value

    if ($v.audited_head_sha -ne $h.head_sha) {
      New-DayEvent -LogDir $LogDir -Kind "verdict_invalid" -Data @{
        cycle = $i; reason = "audited $($v.audited_head_sha), the worker delivered $($h.head_sha)"
      } | Out-Null
      Stop-Day "the audit read a different commit than was delivered"
      break
    }

    New-DayEvent -LogDir $LogDir -Kind "verdict" -Data @{
      cycle = $i; verdict = [string]$v.verdict
      undeclared = @($v.unreported_decisions).Count
      isc_unproved = @($v.isc_unproved).Count
      followups = @($v.followups_created).Count
      reasons = @($v.reasons)
      actions_taken = @($v.actions_taken); followups_created = @($v.followups_created)
      undeclared_detail = @($v.unreported_decisions); isc_unproved_detail = @($v.isc_unproved)
    } | Out-Null

    Write-Day ("[$i] VERDICT {0}  undeclared={1}  isc-unproved={2}  followups={3}" -f `
               ([string]$v.verdict).ToUpper(), @($v.unreported_decisions).Count, @($v.isc_unproved).Count,
               @($v.followups_created).Count)
    foreach ($x in $v.reasons)              { Write-Day "        $x" }
    foreach ($x in $v.unreported_decisions) { Write-Day "        undeclared: $($x.what)  [$($x.found_in)]" }
    foreach ($x in $v.actions_taken)        { Write-Day "        done: $x" }

    # One thing decides whether the day goes on, and it is the verdict.
    if ($v.verdict -eq "hold") { Stop-Day "the audit halts the day"; break }

    # The verdict decides, the script acts. Integrating here rather than inside the audit keeps it
    # mechanical -- it cannot be forgotten, cannot happen twice, and lands in the day log -- and it
    # is what lets the next session see this one: the preflight below fast-forwards local main, so
    # cycle N+1 branches from a base that already carries cycle N.
    Write-Day "[$i] integrating PR #$($h.pr_number)"
    gh pr merge $h.pr_number --merge --delete-branch
    if ($LASTEXITCODE -ne 0) {
      New-DayEvent -LogDir $LogDir -Kind "merge_failed" -Data @{
        cycle = $i; pr_number = $h.pr_number; reason = "gh exited with $LASTEXITCODE"
      } | Out-Null
      Stop-Day "the merge failed ($LASTEXITCODE) -- the PR is left open"
      break
    }
    New-DayEvent -LogDir $LogDir -Kind "merged" -Data @{ cycle = $i; pr_number = $h.pr_number } | Out-Null
    Write-Day "[$i] PR #$($h.pr_number) integrated"

    New-DayEvent -LogDir $LogDir -Kind "cooldown" -Data @{ cycle = $i; minutes = $CooldownSeconds / 60 } | Out-Null
    Write-Day "[$i] cooling $($CooldownSeconds / 60) min"
    Start-Sleep -Seconds $CooldownSeconds
  }

  # `gh ... | Tee-Object -Append` wrote UTF-16 over a UTF-8 file, which is why this section of
  # day.log used to come out with a space between every letter. It goes on the stream too: the
  # report is generated from events, so a fact that only ever reached day.log is a fact the morning
  # report cannot carry.
  $prs = @()
  $raw = gh pr list --state open --limit 20 --json number,title,headRefName 2>$null | Out-String
  if ($LASTEXITCODE -eq 0 -and $raw.Trim()) {
    # Projected one by one rather than `@($raw | ConvertFrom-Json)`: on 5.1 that wraps the array in
    # its own PSObject, and what lands on the event is {"value":[...],"Count":1} -- which reads
    # back as one PR with no number and no title.
    try {
      foreach ($p in (ConvertFrom-Json $raw)) {
        $prs += [pscustomobject]@{ number = $p.number; title = [string]$p.title; branch = [string]$p.headRefName }
      }
    } catch { $prs = @() }
  }
  New-DayEvent -LogDir $LogDir -Kind "open_prs" -Data @{ prs = $prs } | Out-Null

  $final = Get-DayStatus -LogDir $LogDir
  Write-Day ("=== cycle $i | {0:N2} USD ===" -f $final.Cost)
  foreach ($an in $final.Anomalies) { Write-Day ("    [{0}] {1}" -f $an.level, $an.text) }

  Write-Day "Open PRs waiting on the user:"
  foreach ($pr in $prs) { Write-Day ("    #{0}  {1}  ({2})" -f $pr.number, $pr.title, $pr.branch) }

  Write-Day ("report: " + (Write-DayReport -LogDir $LogDir))
}
catch {
  # Without this the exception escapes past an already-written session_started, and every later
  # reader believes a session is still running that died minutes ago -- the silent ending this
  # whole change exists to remove, reintroduced by the one path nobody plans for.
  $msg = $_.Exception.Message
  try {
    New-DayEvent -LogDir $LogDir -Kind "day_crashed" -Data @{
      reason = $msg; where = [string]$_.InvocationInfo.PositionMessage
    } | Out-Null
    Write-Day "the day threw: $msg"
    Stop-Day "the executor threw: $msg"
    Write-Day ("report: " + (Write-DayReport -LogDir $LogDir))
  } catch { }
  throw
}
finally {
  Exit-Lock
}
