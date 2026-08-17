#requires -Version 5.1
<#
  The engine behind the day: the event stream, the contracts sessions speak in, and the rules that
  decide what is abnormal. `run-day.ps1` writes through it, `day-status.ps1` reads through it, and
  `test-day.ps1` proves the parts that do not need a session to run.

  One source of truth per run: `events.jsonl`. Everything anybody decides anything on -- the
  status, the anomalies, the report -- is computed from it, so two readers cannot disagree.
  `day.log` is not that: it is a running commentary written alongside, for a person scrolling. A
  fact that reaches only the log is a fact the report cannot carry, which is why the things worth
  reading in the morning are on the stream first and echoed there second.

  Nothing here launches a session or judges code. The thresholds are thresholds: what they mean,
  and whether one is worth interrupting somebody over, belongs to the skill that reads them.

  ASCII only, and no accented text. Windows PowerShell reads a .ps1 without a BOM as ANSI, so a
  dash or an accent in a comment is a parse error on the machine this actually runs on.
#>

# No StrictMode on purpose: objects out of ConvertFrom-Json do not carry the properties an older
# event never had, and reading an absent one has to give $null rather than throw.

$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# ---------------------------------------------------------------------------------------------
# The stream
# ---------------------------------------------------------------------------------------------

# Add-Content -Encoding utf8 on 5.1 puts a BOM on the file it creates, and a BOM on the first line
# of an NDJSON file breaks every reader that is not PowerShell.
function Add-Utf8Line {
  param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Text)
  [System.IO.File]::AppendAllText($Path, $Text + "`n", $script:Utf8NoBom)
}

function Get-EventsPath { param([Parameter(Mandatory)][string]$LogDir) return (Join-Path $LogDir "events.jsonl") }

<#
  Every file this module reads is one somebody else still has open: a session holds its stream for
  the whole cycle, and the executor appends to events.jsonl between them. So the reading has to be
  done through a handle that permits the writer's -- FileShare::ReadWrite -- which is exactly what
  [System.IO.File]::ReadAllLines does not ask for. On 2026-08-16 every status taken while a session
  ran threw IOException on the open, and the caller reported zero tool calls and zero denials over
  it: the loudest rule of the day, blind for precisely as long as it had something to say, and
  silent about being blind. Nothing here may reach the filesystem any other way.

  A file that is not there yet reads as empty rather than as an error, because the first status can
  land before the first line does.
#>
function Read-OpenFileLines {
  param([Parameter(Mandatory)][string]$Path)
  if (-not (Test-Path $Path)) { return @() }
  $fs = $null
  $sr = $null
  try {
    $fs = New-Object System.IO.FileStream(
      $Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read,
      [System.IO.FileShare]::ReadWrite)
    $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8, $true)
    # Returned unwrapped on purpose: a caller collecting this with @() has to get the lines, and a
    # comma here would hand it one item that happens to be an array.
    return ([string]($sr.ReadToEnd()) -split "`r?`n")
  } catch {
    # Rethrown rather than left as the framework's error: an exception out of a .NET method is not
    # reliably catchable by the caller in 5.1, and a caller that cannot catch this one writes down
    # a zero instead of a failure. `throw` is, so the status can say it could not look.
    throw ("could not read {0}: {1}" -f $Path, $_.Exception.Message)
  } finally {
    if ($sr) { $sr.Dispose() } elseif ($fs) { $fs.Dispose() }
  }
}

<#
  One event per transition. `kind` is the only field a reader interprets; everything else travels
  as it comes, so adding a field does not oblige anybody to change this side.
#>
function New-DayEvent {
  param(
    [Parameter(Mandatory)][string]$LogDir,
    [Parameter(Mandatory)][string]$Kind,
    [hashtable]$Data
  )
  $o = [ordered]@{ ts = (Get-Date).ToUniversalTime().ToString("o"); kind = $Kind }
  if ($Data) { foreach ($k in $Data.Keys) { $o[$k] = $Data[$k] } }
  $line = [pscustomobject]$o | ConvertTo-Json -Depth 8 -Compress
  Add-Utf8Line -Path (Get-EventsPath $LogDir) -Text $line
  return $line
}

<#
  A half-written line is skipped rather than thrown over: the reader runs while the executor
  writes, and a torn read cannot be an error of the reader's.
#>
function Read-DayEvents {
  param([Parameter(Mandatory)][string]$LogDir)
  $p = Get-EventsPath $LogDir
  if (-not (Test-Path $p)) { return @() }
  $out = New-Object System.Collections.ArrayList
  foreach ($line in (Read-OpenFileLines $p)) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    try { $null = $out.Add(($line | ConvertFrom-Json)) } catch { }
  }
  return $out.ToArray()
}

# ---------------------------------------------------------------------------------------------
# What a session emitted
# ---------------------------------------------------------------------------------------------

<#
  With `--output-format stream-json` the result is one LINE of the file, not the file. Parsing the
  whole content throws, and the day would stop on every successful cycle -- that is the trap this
  function exists for. Scanning from the end is only because that is where the line is; what makes
  it the right line is its own `type`, not its position, so a result quoted inside somebody else's
  tool_result is not mistaken for this session's.
#>
function Get-SessionResult {
  param([Parameter(Mandatory)][string]$StreamPath)
  if (-not (Test-Path $StreamPath)) { return $null }
  $lines = @(Read-OpenFileLines $StreamPath)
  for ($i = $lines.Count - 1; $i -ge 0; $i--) {
    $l = $lines[$i]
    if ([string]::IsNullOrWhiteSpace($l)) { continue }
    if ($l.IndexOf('"result"') -lt 0) { continue }   # a cheap skip, not the test: the test is $o.type
    try { $o = $l | ConvertFrom-Json } catch { continue }
    if ($o.type -eq "result") { return $o }
  }
  return $null
}

<#
  The handoff and the verdict are derived from what the session emitted, never from a file the
  session has to remember to write. On 2026-08-16 a worker said the whole handoff as its last
  message, did not write it, and the day died with the work done and the PR open.

  Three attempts, most specific first: the last ```json block, the whole text, and first { to
  last }.
#>
function Get-ContractFromText {
  param([string]$Text)
  if ([string]::IsNullOrWhiteSpace($Text)) { return $null }

  $candidates = New-Object System.Collections.ArrayList

  $fence = $Text.LastIndexOf('```json')
  if ($fence -ge 0) {
    $start = $fence + 7
    $end = $Text.IndexOf('```', $start)
    if ($end -gt $start) { $null = $candidates.Add($Text.Substring($start, $end - $start)) }
  }

  $null = $candidates.Add($Text)

  $first = $Text.IndexOf('{')
  $last = $Text.LastIndexOf('}')
  if ($first -ge 0 -and $last -gt $first) { $null = $candidates.Add($Text.Substring($first, $last - $first + 1)) }

  foreach ($c in $candidates) {
    $t = $c.Trim()
    if (-not $t.StartsWith('{')) { continue }
    try {
      $o = $t | ConvertFrom-Json
      if ($o -is [pscustomobject]) { return $o }
    } catch { }
  }
  return $null
}

<#
  Presence is not enough: in PowerShell the string "false" is truthy, so a verdict that said "hold"
  alongside a lying continue flag would have carried the day on. What the script obeys is validated
  by value.
#>
function Test-DayContract {
  param(
    $Contract,
    [Parameter(Mandatory)][string[]]$Required,
    [Parameter(Mandatory)][string]$Field,
    [Parameter(Mandatory)][string[]]$Allowed
  )
  if ($null -eq $Contract) { return "the session emitted no JSON that can be read" }

  $missing = @()
  foreach ($k in $Required) { if ($null -eq $Contract.$k) { $missing += $k } }
  if ($missing.Count -gt 0) { return "missing fields: " + ($missing -join ", ") }

  if ($Allowed -notcontains [string]$Contract.$Field) {
    return "$Field='$($Contract.$Field)' is not a value in the contract"
  }
  return ""
}

<#
  What the audit has to hand over before the day will stop for it. Checked before a single question
  is published, because the failure it prevents is the worst one this design has: a question with no
  text or no options cannot be put to anybody, and the day would sit waiting for an answer to it
  forever. A malformed ask is taken as a hold instead -- recovery, not a vigil.

  Exactly one option confirms. Two would make "did they agree" a judgement, and the script does not
  make judgements; none would make the PR unmergeable whatever was answered.
#>
function Test-DayQuestions {
  param($Questions)
  $qs = @($Questions)
  if ($qs.Count -eq 0) { return "it asks nothing" }

  $ids = @{}
  foreach ($q in $qs) {
    if (-not $q) { return "one of the questions is empty" }
    $id = [string]$q.id
    if ([string]::IsNullOrWhiteSpace($id)) { return "a question carries no id" }
    if ($ids.ContainsKey($id)) { return "two questions share the id '$id'" }
    $ids[$id] = $true

    if ([string]::IsNullOrWhiteSpace([string]$q.question)) { return "question '$id' has no text" }

    $opts = @($q.options)
    if ($opts.Count -lt 2 -or $opts.Count -gt 4) { return "question '$id' offers $($opts.Count) options, and it has to offer 2 to 4" }

    $labels = @{}
    $confirms = 0
    foreach ($o in $opts) {
      if (-not $o) { return "question '$id' has an empty option" }
      $label = [string]$o.label
      if ([string]::IsNullOrWhiteSpace($label)) { return "an option of question '$id' has no label" }
      if ($labels.ContainsKey($label)) { return "question '$id' offers '$label' twice" }
      $labels[$label] = $true
      if (@("confirm", "reject") -notcontains [string]$o.effect) {
        return "option '$label' of question '$id' has effect '$($o.effect)', which is neither confirm nor reject"
      }
      if ([string]$o.effect -eq "confirm") { $confirms++ }
    }
    if ($confirms -ne 1) { return "question '$id' has $confirms options that confirm, and it has to have exactly one" }
  }
  return ""
}

<#
  The answers a person gave to what the day stopped to ask.

  **The file names the option and never the effect.** Three independent reviewers found the same
  hole on 2026-08-17: while an answer carried its own `effect`, a supervisor that transcribed one
  field wrong -- `{"label":"Open the device natively","effect":"confirm"}` for an option that
  rejects -- merged the PR the user had just turned down, and nothing could tell. So the label is
  matched against the options that were actually offered and the effect is read off the option.
  There is one place a decision is written down, which is the verdict, and the answer only points
  at it.

  The rest is the same rule stated four ways: an answer nobody asked for, two answers to one
  question, a question with no answer, or a file for another run or another cycle are all a
  supervisor and an executor talking about different things, and none of them may pass for consent.
#>
function Test-DayAnswers {
  param($Answers, $Questions, [string]$Run, [int]$Cycle)
  if ($null -eq $Answers) { return "the answers file holds no JSON that can be read" }

  if ($Run -and [string]$Answers.run -ne $Run) { return "these answers are for run '$($Answers.run)', not '$Run'" }
  if ($null -eq $Answers.cycle) { return "the answers name no cycle" }
  if ([int]$Answers.cycle -ne $Cycle) { return "these answers are for cycle $($Answers.cycle), not $Cycle" }

  $byId = @{}
  foreach ($q in @($Questions)) { $byId[[string]$q.id] = $q }

  $seen = @{}
  foreach ($a in @($Answers.answers)) {
    if (-not $a) { return "one of the answers is empty" }
    $id = [string]$a.id
    if ([string]::IsNullOrWhiteSpace($id)) { return "an answer carries no id" }
    if (-not $byId.ContainsKey($id)) { return "'$id' was answered and never asked" }
    if ($seen.ContainsKey($id)) { return "'$id' was answered twice" }
    $seen[$id] = $true

    $label = [string]$a.label
    if ([string]::IsNullOrWhiteSpace($label)) { return "the answer to '$id' names no option" }
    $picked = @(@($byId[$id].options) | Where-Object { [string]$_.label -eq $label })
    if ($picked.Count -ne 1) { return "'$label' is not one of the options offered for '$id'" }
  }

  $missing = @(@($Questions) | ForEach-Object { [string]$_.id } | Where-Object { -not $seen.ContainsKey($_) })
  if ($missing.Count -gt 0) { return "unanswered: " + ($missing -join ", ") }
  return ""
}

<#
  What the effect of an answered question actually was, read off the option that was offered rather
  than off anything the answer carried. Only ever called on answers `Test-DayAnswers` passed, so a
  label that matches nothing cannot reach here -- and if one ever did, `reject` is the reading that
  does not merge.
#>
function Get-AnswerEffect {
  param($Question, [string]$Label)
  foreach ($o in @($Question.options)) {
    if ([string]$o.label -eq $Label) { return [string]$o.effect }
  }
  return "reject"
}

<#
  What the executor does about a verdict, as a function so the probe asks exactly what the loop
  asks. Three answers and no fourth: merge it, ask about it, or put it back.

  A verdict that contradicts itself does not merge. An audit that says `pass` and attaches questions
  has said two things, and the one that costs nothing to obey is the cautious one -- merging a diff
  whose author was still asking about it is the failure the whole arrangement exists to prevent.
#>
function Resolve-Verdict {
  param($Verdict)
  $name = [string]$Verdict.verdict
  $qs = @($Verdict.questions)

  if ($name -eq "ask") {
    $bad = Test-DayQuestions $qs
    if ($bad -ne "") { return [pscustomobject]@{ action = "recover"; reason = "the audit asked for a decision but $bad" } }
    return [pscustomobject]@{ action = "ask"; reason = "" }
  }

  if ($name -eq "hold") { return [pscustomobject]@{ action = "recover"; reason = "the audit held it" } }

  if ($qs.Count -gt 0) {
    return [pscustomobject]@{ action = "recover"; reason = "the verdict is '$name' and it attaches $($qs.Count) question(s), which are two different answers" }
  }
  return [pscustomobject]@{ action = "merge"; reason = "" }
}

<#
  Where a card goes when its PR is not merged, decided from the recoveries already on the stream
  rather than from anything the running process remembers. A day that was killed and relaunched used
  to forget, so a card nothing could land went back in the pool once per restart forever.

  Only a recovery that actually moved the card counts. One that could not reach the board left the
  card where it was, and charging somebody a strike for it would send the next one to `pending` over
  a failure of the board CLI rather than of the work.
#>
function Get-CardDestination {
  param($Recovered, [string]$TaskId)
  if ([string]::IsNullOrWhiteSpace($TaskId)) { return "Open" }
  $before = @(@($Recovered) | Where-Object { $_ -and [string]$_.task -eq $TaskId -and $_.moved })
  if ($before.Count -ge 1) { return "pending" }
  return "Open"
}

<#
  A denial is counted per tool and per what it tried, never as a number: the number says something
  happened, the list says which permission rule is missing. It is the difference between "there
  were 27" and "the board CLI was denied all session".
#>
function New-Denial {
  param([string]$Tool, [string]$Command)
  $cmd = ([string]$Command -replace '\s+', ' ').Trim()
  if ($cmd.Length -gt 120) { $cmd = $cmd.Substring(0, 120) + "..." }
  return [pscustomobject]@{ tool = $Tool; command = $cmd }
}

<#
  A finished session's denials come counted, with their input, in the result. Nothing has to be
  reconstructed from the stream.
#>
function Get-ResultDenials {
  param($Result)
  $out = New-Object System.Collections.ArrayList
  foreach ($d in @($Result.permission_denials)) {
    if (-not $d) { continue }
    $arg = @($d.tool_input.command, $d.tool_input.file_path, $d.tool_input.description) |
           Where-Object { $_ } | Select-Object -First 1
    $null = $out.Add((New-Denial -Tool ([string]$d.tool_name) -Command ([string]$arg)))
  }
  return $out.ToArray()
}

<#
  What identifies "the same thing refused again" is the program, not the tool that carried it. Two
  denied `python` calls spelled differently are one missing rule and a model groping for a way
  around it; a denied `python` and a denied `codex` through the same PowerShell are two unrelated
  rules and grouping them would raise a false alarm on both.

  So: skip leading assignments and `cd`, take the first real word of the first real segment, and
  key on its file name. `$env:X = "utf-8"; python clickup.py lists` and `python clickup.py tasks`
  land on the same key, which is what the 27 denials of 2026-08-16 actually were.
#>
function Get-DenialKey {
  param([string]$Tool, [string]$Command)
  $skip = @('cd', 'set', 'call', 'then', 'do', 'sudo', 'exec')
  foreach ($seg in ($Command -split '\s*(?:;|&&|\|\|)\s*')) {
    if ([string]::IsNullOrWhiteSpace($seg)) { continue }
    if ($seg -match '^\s*\$?[\w:]+\s*=') { continue }          # an assignment, not the command
    foreach ($tok in ($seg -split '\s+')) {
      $t = $tok.Trim('"', "'", '(', ')', '&')
      if ([string]::IsNullOrWhiteSpace($t)) { continue }
      # A segment that only sets the ground -- `cd somewhere` -- is not the command; its argument
      # is not either, so the whole segment is abandoned rather than its first word.
      if ($skip -contains $t.ToLower()) { break }
      $leaf = [System.IO.Path]::GetFileName(($t -replace '\\', '/'))
      if ([string]::IsNullOrWhiteSpace($leaf)) { $leaf = $t }
      return "$Tool $leaf"
    }
  }
  return $Tool
}

function Group-Denials {
  param($Denials)
  $g = @{}
  foreach ($d in @($Denials)) {
    if (-not $d) { continue }
    $k = Get-DenialKey -Tool ([string]$d.tool) -Command ([string]$d.command)
    if (-not $g.ContainsKey($k)) { $g[$k] = New-Object System.Collections.ArrayList }
    $null = $g[$k].Add($d.command)
  }
  $out = New-Object System.Collections.ArrayList
  foreach ($k in @($g.Keys | Sort-Object)) {
    $null = $out.Add([pscustomobject]@{
      tool     = $k
      count    = $g[$k].Count
      commands = @($g[$k] | Select-Object -Unique)
    })
  }
  return $out.ToArray()
}

<#
  JSON says `false`, a session under pressure sometimes says `"false"`, and in PowerShell that
  string is truthy -- which would count a red probe as green in the one place it matters.
#>
function Test-JsonTrue {
  param($Value)
  if ($null -eq $Value) { return $false }
  if ($Value -is [bool]) { return $Value }
  if ($Value -is [string]) { return @('true', '1', 'yes') -contains $Value.ToLower() }
  return [bool]$Value
}

<#
  What a session is doing right now. Silence is measured from the file's timestamp rather than one
  parsed out of it: every arriving line touches the file, so nothing has to be parsed to know that
  nothing has arrived for twenty minutes.
#>
function Get-SessionActivity {
  param([Parameter(Mandatory)][string]$StreamPath)

  $a = [pscustomobject]@{
    Exists       = $false
    LastWrite    = $null
    QuietFor     = $null
    ToolCalls    = 0
    LastTool     = ""
    LastText     = ""
    Denials      = 0
    DenialDetail = @()
    RateLimit    = $null
  }
  if (-not (Test-Path $StreamPath)) { return $a }

  $a.Exists = $true
  $a.LastWrite = (Get-Item $StreamPath).LastWriteTime
  $a.QuietFor = (Get-Date) - $a.LastWrite

  # A tool_result only says it failed; what was attempted is in the tool_use that asked for it,
  # several lines above and tied to it by id.
  $asked = @{}

  foreach ($line in (Read-OpenFileLines $StreamPath)) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    try { $o = $line | ConvertFrom-Json } catch { continue }

    if ($o.type -eq "rate_limit_event" -and $o.rate_limit_info) { $a.RateLimit = $o.rate_limit_info }

    if ($o.type -eq "assistant" -and $o.message -and $o.message.content) {
      foreach ($b in @($o.message.content)) {
        if ($b.type -eq "tool_use") {
          $a.ToolCalls++
          $arg = @($b.input.command, $b.input.file_path, $b.input.pattern, $b.input.description) |
                 Where-Object { $_ } | Select-Object -First 1
          $a.LastTool = ("{0} {1}" -f $b.name, ([string]$arg -replace '\s+', ' ')).Trim()
          if ($b.id) { $asked[[string]$b.id] = [pscustomobject]@{ tool = [string]$b.name; arg = [string]$arg } }
        } elseif ($b.type -eq "text" -and -not [string]::IsNullOrWhiteSpace($b.text)) {
          $a.LastText = (([string]$b.text) -split "`n" | Where-Object { $_.Trim() } | Select-Object -First 1)
        }
      }
    }

    # Under -p a missing permission does not prompt: it denies, and the session carries on as if
    # the tool did not exist. The damage is not the denial, it is what it does next, which is the
    # same job by a worse route with nobody told. While it runs this is the only place it shows;
    # once it ends, the result carries them counted.
    if ($o.type -eq "user" -and $o.message -and $o.message.content) {
      foreach ($b in @($o.message.content)) {
        if ($b.type -eq "tool_result" -and $b.is_error) {
          $c = [string]$b.content
          if ($c -match 'requested permissions|haven''t granted|permission to use|not allowed to use') {
            $a.Denials++
            $src = $null
            if ($b.tool_use_id -and $asked.ContainsKey([string]$b.tool_use_id)) { $src = $asked[[string]$b.tool_use_id] }
            if ($src) { $a.DenialDetail += (New-Denial -Tool $src.tool -Command $src.arg) }
            else      { $a.DenialDetail += (New-Denial -Tool "?" -Command $c) }
          }
        }
      }
    }
  }
  return $a
}

# ---------------------------------------------------------------------------------------------
# The rules
# ---------------------------------------------------------------------------------------------

$script:DefaultRules = @{
  SilenceMinutes = 15    # a session emitting nothing for this long is stuck, not thinking
  DenialGrope    = 2      # two denied attempts at one tool is already groping for a way around
  CostFactor     = 3.0   # a cycle spending three times the median is in a loop
  CostSamples    = 3     # with fewer cycles there is no median worth having
}

function Get-DayRules { return $script:DefaultRules.Clone() }

function New-Anomaly {
  param([string]$Level, [string]$Code, [string]$Text)
  return [pscustomobject]@{ level = $Level; code = $Code; text = $Text }
}

<#
  Thresholds, not judgement. Says what is out of range; what it means, and whether it is worth
  interrupting somebody over, is decided by whoever reads this.
#>
function Get-DayAnomalies {
  param(
    [Parameter(Mandatory)]$Status,
    [hashtable]$Rules
  )
  if (-not $Rules) { $Rules = Get-DayRules }
  $out = New-Object System.Collections.ArrayList

  if ($Status.Running -and $null -ne $Status.QuietForMinutes -and
      $Status.QuietForMinutes -ge $Rules.SilenceMinutes) {
    $null = $out.Add((New-Anomaly "warn" "silence" (
      "the {0} session of cycle {1} has emitted nothing for {2:N0} min" -f $Status.Role, $Status.Cycle, $Status.QuietForMinutes)))
  }

  # The loudest rule here, deliberately. A denial under -p does not stop the session: it sends it
  # to do the same job another way, worse, and tells nobody. On 2026-08-16 there were 27 in one
  # cycle, the board CLI among them, and the cross-model review the repo requires never ran.
  if ($Status.Denials -ge 1) {
    $groping = @($Status.DenialsByTool | Where-Object { $_.count -ge $Rules.DenialGrope })
    if ($groping.Count -gt 0) {
      foreach ($g in $groping) {
        $null = $out.Add((New-Anomaly "stop" "denials" (
          "{0} denied attempts at {1}: it is not missing a tool, it is groping for a way around a permission, and whatever it does instead will be worse and unseen. The rule that is missing has to cover: {2}" -f `
            $g.count, $g.tool, ($g.commands | Select-Object -First 1))))
      }
    } else {
      foreach ($g in @($Status.DenialsByTool)) {
        $null = $out.Add((New-Anomaly "warn" "denials" (
          "{0} denied; under -p that does not prompt, it denies, and the session carries on as if the tool did not exist: {1}" -f `
            $g.tool, ($g.commands | Select-Object -First 1))))
      }
    }
  }

  if ($Status.CycleCosts.Count -ge $Rules.CostSamples -and $null -ne $Status.LastCycleCost) {
    $med = Get-Median $Status.CycleCosts
    if ($med -gt 0 -and $Status.LastCycleCost -gt ($med * $Rules.CostFactor)) {
      $null = $out.Add((New-Anomaly "warn" "cost" (
        "cycle {0} spent {1:N2} USD against a median of {2:N2}" -f $Status.Cycle, $Status.LastCycleCost, $med)))
    }
  }

  if ($Status.Killed) {
    $null = $out.Add((New-Anomaly "stop" "killed" "a session passed the executor's clock and was killed"))
  }

  # Not a fault, and deliberately not `stop`: the day is doing exactly what it was asked to do, and
  # halting it here would undo the recovery this exists for. It is still the loudest thing a
  # supervisor can see, because it is the one state that ends only when a person acts -- there is no
  # timeout behind it, so a question nobody carries is a day that waits until it is killed.
  foreach ($q in @($Status.Questions)) {
    $null = $out.Add((New-Anomaly "warn" "waiting" (
      "cycle {0} is waiting on you before it can go on -- {1}" -f $q.cycle, $q.question)))
  }

  # A status that could not read the live stream knows nothing about that session -- not its
  # silence, not its denials -- and every one of those fields reads zero. Saying so is the whole
  # rule: it does not stop the day, because a torn read is not the reader's error and one hiccup is
  # not worth a paid day, but it never again passes for "nothing abnormal".
  if ($Status.Unreadable) {
    $null = $out.Add((New-Anomaly "warn" "unreadable" (
      "the live session's stream could not be read, so nothing below it was measured -- no silence, no denials, no tool calls: {0}" -f $Status.Unreadable)))
  }

  if ($Status.RateLimit -and $Status.RateLimit.status -and $Status.RateLimit.status -ne "allowed") {
    $reset = ""
    if ($Status.RateLimit.resetsAt) {
      $reset = " (back at " + ([DateTimeOffset]::FromUnixTimeSeconds([int64]$Status.RateLimit.resetsAt)).LocalDateTime.ToString("HH:mm") + ")"
    }
    $null = $out.Add((New-Anomaly "stop" "window" ("the usage window says '{0}'{1}" -f $Status.RateLimit.status, $reset)))
  }

  if ($Status.Ended -and $Status.EndReason -and $Status.EndReason -ne "no_tasks") {
    $null = $out.Add((New-Anomaly "stop" "stopped" ("the day ended on: {0}" -f $Status.EndReason)))
  }

  # The one ending nothing else can report, because the process that would have reported it is the
  # one that is gone: killed by hand, killed by the machine, or dead of an exception the executor
  # could not write down.
  if ($Status.ProcessGone) {
    $null = $out.Add((New-Anomaly "stop" "vanished" (
      "the day's process ({0}) is gone and it never wrote an ending" -f $Status.ExecutorPid)))
  }

  # Anomalies already written to the stream come back even when the state that produced them is
  # gone. Deduplicated on their text, so a rule that fires twice over the same condition reads once.
  $seen = @{}
  foreach ($a in $out) { $seen[[string]$a.text] = $true }
  foreach ($p in @($Status.Past)) {
    if ($p -and -not $seen.ContainsKey([string]$p.text)) {
      $null = $out.Add((New-Anomaly $p.level $p.code $p.text))
      $seen[[string]$p.text] = $true
    }
  }

  return $out.ToArray()
}

<#
  Which anomalies end the day, as one function so the executor and the probe cannot drift apart. A
  level of `stop` means stop: labelling a condition and then merging anyway would make the level
  decoration, which is what it was before this existed. `stopped` is excluded because it is the
  day's own ending being reported back, not a reason to end it again.
#>
function Get-HaltingAnomalies {
  param([Parameter(Mandatory)]$Status)
  return @($Status.Anomalies | Where-Object { $_.level -eq "stop" -and $_.code -ne "stopped" })
}

function Get-Median {
  param([double[]]$Values)
  if (-not $Values -or $Values.Count -eq 0) { return 0 }
  $s = @($Values | Sort-Object)
  $n = $s.Count
  if ($n % 2 -eq 1) { return [double]$s[[int](($n - 1) / 2)] }
  return ([double]$s[$n / 2 - 1] + [double]$s[$n / 2]) / 2
}

# ---------------------------------------------------------------------------------------------
# The state
# ---------------------------------------------------------------------------------------------

function Find-LatestRun {
  param([Parameter(Mandatory)][string]$OrchestratorDir)
  $logs = Join-Path $OrchestratorDir "log"
  if (-not (Test-Path $logs)) { return $null }
  $d = Get-ChildItem $logs -Directory -ErrorAction SilentlyContinue |
       Where-Object { Test-Path (Get-EventsPath $_.FullName) } |
       Sort-Object Name | Select-Object -Last 1
  if ($d) { return $d.FullName }
  return $null
}

<#
  What the day is doing, read off the stream and nothing else. It works the same over a finished
  run: what changes is that `Running` is false and `EndReason` says why.
#>
function Get-DayStatus {
  param(
    [Parameter(Mandatory)][string]$LogDir,
    [hashtable]$Rules
  )
  $ev = Read-DayEvents $LogDir

  $st = [pscustomobject]@{
    LogDir          = $LogDir
    Started         = $null
    Ended           = $false
    EndReason       = ""
    ExecutorPid     = $null
    ProcessGone     = $false
    Past            = @()
    Cycle           = 0
    Role            = ""
    Running         = $false
    RunningSince    = $null
    QuietForMinutes = $null
    LastTool        = ""
    LastText        = ""
    ToolCalls       = 0
    Denials         = 0
    DenialDetail    = @()
    DenialsByTool   = @()
    Killed          = $false
    Unreadable      = ""
    RateLimit       = $null
    Cost            = 0.0
    LastCycleCost   = $null
    CycleCosts      = @()
    Cycles          = @()
    Merged          = @()
    Waiting         = $false
    Questions       = @()
    Answered        = @()
    Recovered       = @()
    Anomalies       = @()
  }

  $cycles = @{}
  $openStream = $null
  $pending = [ordered]@{}

  foreach ($e in $ev) {
    switch ($e.kind) {
      "day_started" { $st.Started = $e.ts; $st.ExecutorPid = $e.pid }
      "day_ended"   { $st.Ended = $true; $st.EndReason = [string]$e.reason }
      # An anomaly is written down when it fires, because most of them cannot be recomputed later:
      # a 20-minute silence disappears the moment the session ends, and a cycle that cost triple
      # the median stops looking like one as soon as another cycle moves the median. The morning
      # report promises every anomaly that fired, so every anomaly that fired is on the stream.
      "anomaly"     { $st.Past += [pscustomobject]@{ level = [string]$e.level; code = [string]$e.code; text = [string]$e.text } }
      "session_started" {
        $st.Cycle = [int]$e.cycle
        $st.Role = [string]$e.role
        $st.Running = $true
        $st.RunningSince = $e.ts
        $openStream = [string]$e.stream
      }
      "session_ended" {
        $st.Running = $false
        $openStream = $null
        if ($null -ne $e.cost) { $st.Cost += [double]$e.cost }
        $c = [int]$e.cycle
        if (-not $cycles.ContainsKey($c)) { $cycles[$c] = [ordered]@{ cycle = $c; cost = 0.0 } }
        $cycles[$c].cost += [double]$e.cost
      }
      "session_killed" { $st.Killed = $true; $st.Running = $false; $openStream = $null }
      "session_failed" { $st.Running = $false; $openStream = $null }
      "handoff" {
        $c = [int]$e.cycle
        if (-not $cycles.ContainsKey($c)) { $cycles[$c] = [ordered]@{ cycle = $c; cost = 0.0 } }
        $cycles[$c].outcome = [string]$e.outcome
        $cycles[$c].task = [string]$e.task_id
        $cycles[$c].pr = $e.pr_number
      }
      "verdict" {
        $c = [int]$e.cycle
        if (-not $cycles.ContainsKey($c)) { $cycles[$c] = [ordered]@{ cycle = $c; cost = 0.0 } }
        $cycles[$c].verdict = [string]$e.verdict
      }
      "merged" { $st.Merged += $e.pr_number }
      # The day stopping to ask is a state, not an event that scrolls past: what a reader needs is
      # which questions are still open right now, so they are held until their answer arrives.
      "question_asked" {
        $pending[[string]$e.id] = [pscustomobject]@{
          id = [string]$e.id; cycle = [int]$e.cycle; question = [string]$e.question
          why = [string]$e.why; options = @($e.options); pr = $e.pr_number; task = [string]$e.task_id
        }
      }
      "answered" {
        $pending.Remove([string]$e.id)
        $st.Answered += [pscustomobject]@{
          id = [string]$e.id; cycle = [int]$e.cycle; question = [string]$e.question
          label = [string]$e.label; effect = [string]$e.effect; notes = [string]$e.notes
        }
      }
      "recovered" {
        $st.Recovered += [pscustomobject]@{
          cycle = [int]$e.cycle; task = [string]$e.task_id; pr = $e.pr_number
          to = [string]$e.to; reason = [string]$e.reason; moved = [bool]$e.moved
        }
      }
    }
  }

  $st.Questions = @($pending.Keys | ForEach-Object { $pending[$_] })
  # A day that has ended is not waiting for anything, whatever the last question on the stream says.
  # The executor releases the lock in `finally`, so an exception thrown between asking and answering
  # leaves both marks -- and reporting that somebody still owes an answer to a process that no
  # longer exists sends them off to write a file nothing will ever read.
  if ($st.Ended) { $st.Questions = @() }
  $st.Waiting = $st.Questions.Count -gt 0

  # What only shows by looking at the live session's own file. A read that fails says so: the status
  # this produces is the only thing watching a session that is spending money, so "I could not look"
  # has to arrive as itself and never as a zero, which is what it looked like the whole time
  # Read-OpenFileLines was reading with the wrong share.
  if ($st.Running -and $openStream) {
    $path = $openStream
    if (-not [System.IO.Path]::IsPathRooted($path)) { $path = Join-Path $LogDir $openStream }
    $a = $null
    try {
      $a = Get-SessionActivity $path
    } catch {
      $st.Unreadable = "{0}: {1}" -f (Split-Path -Leaf $path), $_.Exception.Message
    }
    if ($a -and $a.Exists) {
      $st.QuietForMinutes = [math]::Round($a.QuietFor.TotalMinutes, 1)
      $st.LastTool = $a.LastTool
      $st.LastText = $a.LastText
      $st.ToolCalls = $a.ToolCalls
      $st.Denials += $a.Denials
      $st.DenialDetail += @($a.DenialDetail)
      if ($a.RateLimit) { $st.RateLimit = $a.RateLimit }
    }
  }

  # A closed session's denials arrive counted and with the command it tried, so the executor leaves
  # them on the event. All three terminal kinds carry them: a session that collected 27 denials and
  # then timed out is the case where they matter most, and reading only `session_ended` would have
  # thrown exactly those away.
  foreach ($e in $ev) {
    if (@("session_ended", "session_failed", "session_killed") -notcontains $e.kind) { continue }
    if ($null -ne $e.denials) { $st.Denials += [int]$e.denials }
    if ($e.denial_detail) { $st.DenialDetail += @($e.denial_detail) }
    if ($e.rate_limit) { $st.RateLimit = $e.rate_limit }
  }

  $st.DenialsByTool = Group-Denials $st.DenialDetail

  # A session cannot be running if the process that launched it is gone. Without this, a day killed
  # with Stop-Process -- which the skill tells people to use -- reads as "running" forever, and the
  # silence rule slowly counts up over a session that ended hours ago.
  if ($st.ExecutorPid -and -not $st.Ended) {
    if (-not (Get-Process -Id ([int]$st.ExecutorPid) -ErrorAction SilentlyContinue)) {
      $st.ProcessGone = $true
      $st.Running = $false
      # Nobody is on the other end of the question any more. Left standing it would send a
      # supervisor off to collect an answer for a process that cannot receive it, and `vanished`
      # -- which is the thing actually worth saying -- would read as the smaller of the two.
      $st.Waiting = $false
      $st.Questions = @()
    }
  }

  $ordered = @($cycles.Keys | Sort-Object)
  $st.Cycles = @($ordered | ForEach-Object { [pscustomobject]$cycles[$_] })
  $st.CycleCosts = @($st.Cycles | Where-Object { $_.cost -gt 0 } | ForEach-Object { [double]$_.cost })
  if ($st.Cycles.Count -gt 0) { $st.LastCycleCost = [double]($st.Cycles[-1].cost) }

  $st.Anomalies = Get-DayAnomalies -Status $st -Rules $Rules
  return $st
}

# ---------------------------------------------------------------------------------------------
# The renders
# ---------------------------------------------------------------------------------------------

<#
  What gets read in the morning when nobody was watching. It comes off the same stream as
  everything else, so it cannot tell a different story than the reader told live.
#>
function Write-DayReport {
  param([Parameter(Mandatory)][string]$LogDir)

  $st = Get-DayStatus -LogDir $LogDir
  $ev = Read-DayEvents $LogDir
  $L = New-Object System.Collections.ArrayList

  $null = $L.Add("# The day " + (Split-Path -Leaf $LogDir))
  $null = $L.Add("")
  $state = "running"
  if ($st.Ended) { $state = "ended - " + $st.EndReason }
  $null = $L.Add(("{0} cycle(s) | {1:N2} USD | {2}" -f $st.Cycles.Count, $st.Cost, $state))
  $null = $L.Add("")

  if ($st.Cycles.Count -gt 0) {
    $null = $L.Add("| Cycle | Task | PR | Handoff | Verdict | USD |")
    $null = $L.Add("| --- | --- | --- | --- | --- | --- |")
    foreach ($c in $st.Cycles) {
      $task = "-"; if ($c.task) { $task = $c.task }
      $pr = "-"; if ($c.pr) { $pr = "#" + $c.pr }
      $outcome = "-"; if ($c.outcome) { $outcome = $c.outcome }
      $verdict = "-"; if ($c.verdict) { $verdict = $c.verdict }
      $null = $L.Add(("| {0} | {1} | {2} | {3} | {4} | {5:N2} |" -f $c.cycle, $task, $pr, $outcome, $verdict, $c.cost))
    }
    $null = $L.Add("")
  }

  if ($st.Merged.Count -gt 0) {
    $null = $L.Add("Merged into main: " + (($st.Merged | ForEach-Object { "#$_" }) -join ", "))
    $null = $L.Add("")
  }

  # First, because it is the only section describing something still happening. A day that asked
  # and was never answered is sitting there right now holding the lock, and a reader who scrolls
  # past that has been told the wrong thing about their own machine.
  if ($st.Waiting) {
    $null = $L.Add("## Still waiting on you")
    $null = $L.Add("")
    $null = $L.Add("The day stopped to ask and there is no timeout behind it: it goes on when these are answered, and not before.")
    $null = $L.Add("")
    foreach ($q in $st.Questions) {
      $null = $L.Add(("- **cycle {0}** ({1}) - {2}" -f $q.cycle, $q.task, $q.question))
    }
    $null = $L.Add("")
  }

  # The durable record of what a person decided mid-day. The card and the PR carry it too, but this
  # is the only place all of a day's decisions are in one list, in the order they were put.
  if ($st.Answered.Count -gt 0) {
    $null = $L.Add("## What you decided")
    $null = $L.Add("")
    foreach ($a in $st.Answered) {
      $line = "- **{0}** - {1} -> **{2}**" -f $a.effect, $a.question, $a.label
      if ($a.notes) { $line += " (" + $a.notes + ")" }
      $null = $L.Add($line)
    }
    $null = $L.Add("")
  }

  if ($st.Recovered.Count -gt 0) {
    $null = $L.Add("## Put back rather than merged")
    $null = $L.Add("")
    $null = $L.Add("The PR is open and the card moved. The day did not stop over any of these.")
    $null = $L.Add("")
    foreach ($r in $st.Recovered) {
      $pr = "-"; if ($r.pr) { $pr = "#" + $r.pr }
      if ($r.moved) {
        $null = $L.Add(("- {0} {1} -> ``{2}`` - {3}" -f $r.task, $pr, $r.to, $r.reason))
      } else {
        # The one line in this section somebody has to act on: the day meant to put the card back
        # and could not, so it is still in `in review` and no worker will pick it up.
        $null = $L.Add(("- **{0} {1} did not move** - it should be ``{2}`` and the board still says otherwise. {3}" -f `
          $r.task, $pr, $r.to, $r.reason))
      }
    }
    $null = $L.Add("")
  }

  # What the day left for a person to act on, which is the reason they open this file at all.
  $leftOut = @($ev | Where-Object { $_.kind -eq "handoff" } | ForEach-Object { @($_.left_out) } | Where-Object { $_ })
  $actions = @($ev | Where-Object { $_.kind -eq "verdict" } | ForEach-Object { @($_.actions_taken) } | Where-Object { $_ })
  if ($leftOut.Count -gt 0) {
    $null = $L.Add("## Left out")
    $null = $L.Add("")
    foreach ($x in $leftOut) { $null = $L.Add("- " + [string]$x) }
    $null = $L.Add("")
  }
  if ($actions.Count -gt 0) {
    $null = $L.Add("## What the audit did")
    $null = $L.Add("")
    foreach ($x in $actions) { $null = $L.Add("- " + [string]$x) }
    $null = $L.Add("")
  }

  $open = @($ev | Where-Object { $_.kind -eq "open_prs" } | Select-Object -Last 1)
  if ($open.Count -gt 0 -and @($open[0].prs).Count -gt 0) {
    $null = $L.Add("## Open PRs waiting on you")
    $null = $L.Add("")
    foreach ($pr in @($open[0].prs)) { $null = $L.Add(("- #{0} {1}" -f $pr.number, $pr.title)) }
    $null = $L.Add("")
  }

  # This section comes before everything else that went wrong because it is the only part of this
  # file anybody acts on by editing one line: each row is a permission rule that is missing.
  if ($st.DenialsByTool.Count -gt 0) {
    $null = $L.Add("## Permissions denied")
    $null = $L.Add("")
    $null = $L.Add("Under ``-p`` a denial does not prompt: it denies, and the session tries something worse without saying so.")
    $null = $L.Add("")
    $null = $L.Add("| Tool | Times | What it tried |")
    $null = $L.Add("| --- | --- | --- |")
    foreach ($g in $st.DenialsByTool) {
      foreach ($c in $g.commands) {
        $null = $L.Add(("| {0} | {1} | ``{2}`` |" -f $g.tool, $g.count, ($c -replace '\|', ' ')))
      }
    }
    $null = $L.Add("")
  }

  $null = $L.Add("## Anomalies")
  $null = $L.Add("")
  if ($st.Anomalies.Count -eq 0) {
    $null = $L.Add("No rule fired.")
  } else {
    foreach ($a in $st.Anomalies) { $null = $L.Add(("- **{0}** ({1}) - {2}" -f $a.code, $a.level, $a.text)) }
  }
  $null = $L.Add("")

  $bad = @($ev | Where-Object { $_.kind -match 'invalid|failed|killed' })
  if ($bad.Count -gt 0) {
    $null = $L.Add("## What went wrong")
    $null = $L.Add("")
    foreach ($f in $bad) { $null = $L.Add(("- ``{0}`` - {1}" -f $f.kind, ([string]$f.reason))) }
    $null = $L.Add("")
  }

  $path = Join-Path $LogDir "report.md"
  [System.IO.File]::WriteAllText($path, ($L -join "`n") + "`n", $script:Utf8NoBom)
  return $path
}

Export-ModuleMember -Function Add-Utf8Line, Get-EventsPath, Read-OpenFileLines, New-DayEvent, Read-DayEvents,
  Get-SessionResult, Get-ContractFromText, Test-DayContract, Test-JsonTrue, Get-SessionActivity,
  Test-DayQuestions, Test-DayAnswers, Get-AnswerEffect, Resolve-Verdict, Get-CardDestination,
  New-Denial, Get-ResultDenials, Get-DenialKey, Group-Denials,
  Get-DayRules, Get-DayAnomalies, Get-HaltingAnomalies, Get-Median,
  Find-LatestRun, Get-DayStatus, Write-DayReport
