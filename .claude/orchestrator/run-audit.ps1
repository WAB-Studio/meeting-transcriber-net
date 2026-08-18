#requires -Version 5.1
<#
  One reading session: `/audit-session` over the PR this cycle's worker delivered, and what it
  emitted becomes the verdict.

    .\run-audit.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$env:PYTHONIOENCODING  = "utf-8"
Import-Module (Join-Path $PSScriptRoot "atom.psm1") -Force -DisableNameChecking

$script:Day = $null
trap { Write-AtomCrash -Message $_.Exception.Message -Day $script:Day; exit 1 }

$VerdictKeys = @("verdict","reasons","unreported_decisions","isc_unproved",
                 "followups_created","actions_taken","audited_head_sha","decisions_owed")

# What the audit did, where a list nobody wrote and a list nothing went into say the same thing.
#
# `isc_unproved` and `unreported_decisions` are deliberately not here, and it is the same line the
# worker's `isc_closed` and `probes` sit on: they are the findings a merge hangs off, so an audit
# that never says whether a claim was corroborated must not have "none were unproved" written in
# for it. A `pass` merges into `main` with nobody in the loop, and silence is not a finding.
$VerdictEmptyList = @("reasons","followups_created","actions_taken","decisions_owed")

$day = Open-Day
$script:Day = $day
if ($day.Error -ne "") { Write-Host $day.Error; Write-Atom @{ ok = $false; reason = $day.Error }; exit 1 }

$cycle = $day.Cycle
$h = Read-Contract $day "handoff-$cycle.json"
if ($null -eq $h) {
  Write-Atom @{ ok = $false; reason = "cycle $cycle has no handoff to audit" }
  exit 1
}
if ([string]$h.outcome -ne "pr_opened") {
  Write-Atom @{ ok = $false; reason = "cycle $cycle ended as '$($h.outcome)', so there is no PR to audit" }
  exit 1
}
# A second audit of the same cycle is a second paid session over a diff already judged, and its
# verdict would overwrite the one the cycle is about to be closed on.
if (Test-CycleEvent -LogDir $day.LogDir -Cycle $cycle -Kinds @("verdict")) {
  Write-Atom @{ ok = $false; reason = "cycle $cycle already has a verdict -- close it rather than auditing it again" }
  exit 1
}

$verdictFile = Join-Path $day.LogDir "verdict-$cycle.json"
Remove-Item $verdictFile -ErrorAction SilentlyContinue

Write-Day $day "[$cycle] auditing PR #$($h.pr_number) at $($h.head_sha)"
$a = Invoke-Session -Day $day -Role "audit" -Cycle $cycle `
                    -Prompt "/audit-session $(Join-Path $day.LogDir "handoff-$cycle.json") $verdictFile"
if ($null -eq $a) { Write-Atom @{ ok = $false; stop = "the audit left no result" }; exit 1 }

# Before every judgement below, the commit check included: an audit that read the PR after somebody
# pushed to it is refused, and its ten minutes of reading are still worth keeping.
$said = Save-EmittedContract -Path (Join-Path $day.LogDir "verdict-$cycle.emitted.txt") -Text ([string]$a.result)

$status = Get-DayStatus -LogDir $day.LogDir
Write-Day $day ("[$cycle] audit done: usd={0:N2}  running={1:N2}" -f $a.total_cost_usd, $status.Cost)
if ($a.is_error) { Write-Atom @{ ok = $false; stop = "the audit ended in error" }; exit 1 }

$unsound = Test-Sound -Day $day
if ($unsound -ne "") { Write-Atom @{ ok = $false; stop = "the audit's session is not sound: $unsound" }; exit 1 }

$c = Repair-Contract (Get-ContractFromText ([string]$a.result)) -EmptyList $VerdictEmptyList
$bad = Test-DayContract -Contract $c -Required $VerdictKeys -Field "verdict" `
                        -Allowed @("pass","pass_with_followup","ask","hold")
if ($bad -ne "") {
  New-DayEvent -LogDir $day.LogDir -Kind "verdict_invalid" -Data @{ cycle = $cycle; reason = $bad; said = $said } | Out-Null
  Write-Atom @{ ok = $false; stop = "invalid verdict: $bad -- what the session emitted is in $(Split-Path -Leaf $said)" }
  exit 1
}

# The audit reads the PR, and the worker delivered a commit. If somebody pushed between the two,
# the verdict is about different code than was handed over.
if ([string]::IsNullOrWhiteSpace([string]$c.audited_head_sha) -or
    [string]$c.audited_head_sha -ne [string]$h.head_sha) {
  New-DayEvent -LogDir $day.LogDir -Kind "verdict_invalid" -Data @{
    cycle = $cycle; reason = "audited $($c.audited_head_sha), the worker delivered $($h.head_sha)"
  } | Out-Null
  Write-Atom @{ ok = $false; stop = "the audit read a different commit than was delivered" }
  exit 1
}

[System.IO.File]::WriteAllText($verdictFile, ($c | ConvertTo-Json -Depth 12), (New-Object System.Text.UTF8Encoding($false)))

New-DayEvent -LogDir $day.LogDir -Kind "verdict" -Data @{
  cycle = $cycle; verdict = [string]$c.verdict
  undeclared = @($c.unreported_decisions).Count
  isc_unproved = @($c.isc_unproved).Count
  followups = @($c.followups_created).Count
  owed = @($c.decisions_owed).Count
  reasons = @($c.reasons)
  actions_taken = @($c.actions_taken); followups_created = @($c.followups_created)
  undeclared_detail = @($c.unreported_decisions); isc_unproved_detail = @($c.isc_unproved)
} | Out-Null

Write-Day $day ("[$cycle] VERDICT {0}  undeclared={1}  isc-unproved={2}  followups={3}" -f `
                ([string]$c.verdict).ToUpper(), @($c.unreported_decisions).Count,
                @($c.isc_unproved).Count, @($c.followups_created).Count)
foreach ($x in $c.reasons) { Write-Day $day "        $x" }

Write-Atom @{ ok = $true; cycle = $cycle; verdict = [string]$c.verdict }
