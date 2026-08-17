#requires -Version 5.1
<#
  Closes a cycle that stopped to ask, with what the person answered. It reads `.scratch/answers.json`
  and nothing else:

    [ { "id": "q1", "label": "<the option, exactly>", "notes": "<what they typed>" } ]

  **An answer names an option and never what the option means.** The effect is read back off the
  verdict's own option list, so the one place a decision is written down is the place the audit
  wrote it. While an answer carried its own confirm/reject, one mistyped field merged the diff the
  user had just turned down and nothing could tell.

    .\answer-cycle.ps1
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
if ($null -eq $h) { Write-Atom @{ ok = $false; reason = "cycle $cycle has nothing to answer" }; exit 1 }

if (Test-CycleEvent -LogDir $day.LogDir -Cycle $cycle -Kinds @("merged","recovered","answered_before_building")) {
  Write-Atom @{ ok = $true; cycle = $cycle; action = "already closed" }
  exit 0
}

# Two sessions can ask, and which one did is legible from what is on disk: a verdict means the audit
# asked about a PR that exists, and no verdict means the worker asked before building one. The
# answer is collected the same way either way; what differs is what it lets happen next.
$v = Read-Contract $day "verdict-$cycle.json"
$questions = @($h.questions)
if ($null -ne $v) { $questions = @($v.questions) }
if ($questions.Count -eq 0) { Write-Atom @{ ok = $false; reason = "cycle $cycle asked nothing" }; exit 1 }

$file = Join-Path $day.Repo ".scratch\answers.json"
if (-not (Test-Path $file)) { Write-Atom @{ ok = $false; reason = "there is no .scratch/answers.json to read" }; exit 1 }
$answers = $null
try { $answers = (Get-Content $file -Raw) | ConvertFrom-Json } catch { $answers = $null }

$bad = Test-DayAnswers -Answers $answers -Questions $questions
if ($bad -ne "") {
  # Refused whole and left where it is: a partial set is not agreement, and rewriting the file is
  # what changes anything.
  New-DayEvent -LogDir $day.LogDir -Kind "answers_invalid" -Data @{ cycle = $cycle; reason = $bad } | Out-Null
  Write-Day $day "  the answers do not hold: $bad"
  Write-Atom @{ ok = $false; reason = $bad }
  exit 1
}

$byId = @{}
foreach ($q in $questions) { $byId[[string]$q.id] = $q }

$effect = "confirm"
$lines = New-Object System.Collections.ArrayList
foreach ($a in @($answers)) {
  $q = $byId[[string]$a.id]
  $label = [string]$a.label
  # Only the audit's options carry one. A worker asked before there was a PR, so nothing it was
  # answered is about merging anything.
  $picked = "decided"
  if ($null -ne $v) {
    $picked = Get-AnswerEffect -Question $q -Label $label
    if ($picked -eq "reject") { $effect = "reject" }
  }

  New-DayEvent -LogDir $day.LogDir -Kind "answered" -Data @{
    cycle = $cycle; id = [string]$a.id; question = [string]$q.question
    label = $label; effect = $picked; notes = [string]$a.notes
  } | Out-Null
  Write-Day $day "  = [$($a.id)] $label  ($picked)"

  $line = "- **$([string]$q.question)** -> **$label**"
  if ($a.notes) { $line += "`n  " + [string]$a.notes }
  $null = $lines.Add($line)
}

# Written where a person reads it: the run's log is gitignored and per run, so without this the only
# durable trace of a decision made today would be whatever it caused.
$body = @("**Answered by the user while the day ran.**", "", ($lines -join "`n")) -join "`n"
Write-Elsewhere $day -TaskId ([string]$h.task_id) -PrNumber $h.pr_number -Body $body -Name "answers-$cycle.md"

Remove-Item $file -Force -ErrorAction SilentlyContinue

# The worker asked, so there is no PR and nothing to integrate. What the answer buys is a card that
# now settles its own fork: it goes back in the pool carrying it, and the next cycle builds it.
if ($null -eq $v) {
  $moved = $false
  if ([string]$h.task_id) { $moved = Move-Card $day -TaskId ([string]$h.task_id) -To "Open" }
  $parked = Complete-Journal -Repo $day.Repo -TaskId ([string]$h.task_id)
  if ($parked) { New-DayEvent -LogDir $day.LogDir -Kind "journal_parked" -Data @{ to = $parked } | Out-Null }
  New-DayEvent -LogDir $day.LogDir -Kind "answered_before_building" -Data @{
    cycle = $cycle; task_id = [string]$h.task_id; moved = $moved
  } | Out-Null
  Write-Day $day "[$cycle] answered before anything was built -- card $($h.task_id) back in the pool"
  Write-Atom @{ ok = $true; cycle = $cycle; action = "answered"; task_id = [string]$h.task_id; moved = $moved }
  exit 0
}

if ($effect -eq "reject") {
  $to = Invoke-Recover -Day $day -Handoff $h -Reason "you turned down the decision this PR rests on"
  Write-Atom @{ ok = $true; cycle = $cycle; action = "recovered"; to = $to }
  exit 0
}

$failed = Invoke-Merge -Day $day -Handoff $h
if ($failed -ne "") { Write-Atom @{ ok = $false; cycle = $cycle; stop = $failed }; exit 1 }
Write-Atom @{ ok = $true; cycle = $cycle; action = "merged"; pr_number = $h.pr_number }
