#requires -Version 5.1
<#
  The engine behind the day: the event stream, the contracts sessions speak in, and the rules that
  decide what is abnormal. The atoms write through it, `day-status.ps1` reads through it, and
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
  Whether the session said a field at all, which is not the same question as whether its value is
  empty. `pr_number: null` is an answer; no `pr_number` is silence.
#>
function Test-ContractSaid {
  param($Contract, [string]$Name)
  if ($null -eq $Contract -or -not $Contract.PSObject -or -not $Contract.PSObject.Properties) { return $false }
  return (@($Contract.PSObject.Properties.Name) -contains $Name)
}

<#
  A name the schema spells differently, and a list nothing went into, are settled here before
  anything judges the contract. On 2026-08-17 a worker ran for 56 minutes, pushed to its PR and got
  CI green, and the day ended on its handoff calling the claims it closed `claims_closed` and
  leaving out `skipped`, which a cycle that skipped nothing had no reason to write.

  An alias is a name a session really used, never one that might sound right: the table is a record
  of what happened, and a synonym nothing ever emitted is an invention that would let a real
  mistake through. A field defaults only where its empty value and its absence say the same thing
  to every later reader -- which is why the lists of what was deferred, left out or skipped default
  and `isc_closed` and `probes` do not. Those two are what the audit corroborates, and an audit
  that reads no claims because none were written would find nothing wrong and merge.
#>
function Repair-Contract {
  param($Contract, [hashtable]$Aliases = @{}, [string[]]$EmptyList = @(), [string[]]$EmptyText = @())
  if ($null -eq $Contract) { return $null }

  foreach ($from in @($Aliases.Keys)) {
    $to = [string]$Aliases[$from]
    if ((Test-ContractSaid $Contract $from) -and -not (Test-ContractSaid $Contract $to)) {
      $Contract | Add-Member -NotePropertyName $to -NotePropertyValue $Contract.$from -Force
    }
  }
  foreach ($k in $EmptyList) {
    if (-not (Test-ContractSaid $Contract $k)) { $Contract | Add-Member -NotePropertyName $k -NotePropertyValue @() -Force }
  }
  foreach ($k in $EmptyText) {
    if (-not (Test-ContractSaid $Contract $k)) { $Contract | Add-Member -NotePropertyName $k -NotePropertyValue "" -Force }
  }
  return $Contract
}

<#
  What a session emitted is written down before anything judges it. The session is gone and was
  paid for, and until 2026-08-17 every way of refusing it threw it away with it: that day's run
  folder ended with no handoff in it at all, so nothing could audit 56 minutes of work already
  done, then or later. The only other copy is the session stream, which is a transcript nobody
  reads.

  Called the moment a session returns and not inside any rejection branch. Ordering is the whole of
  it: a save that hangs off one judgement is lost to the next one somebody adds -- the first draft
  of this saved on an unreadable contract, and left the audit's own commit check, the error flag
  and the soundness check still able to end a day holding the only copy of what was said.

  The raw text and not the parsed object, because the emission that cannot be parsed at all is
  exactly the one worth keeping. Nothing here overwrites: an atom whose contract was refused can be
  run again, and the second session's words are not a reason to lose the first's.
#>
function Save-EmittedContract {
  param([Parameter(Mandatory)][string]$Path, [string]$Text)
  $dir  = Split-Path -Parent $Path
  $stem = [System.IO.Path]::GetFileNameWithoutExtension($Path)
  $ext  = [System.IO.Path]::GetExtension($Path)
  $try  = $Path
  $n    = 1
  while (Test-Path $try) { $n++; $try = Join-Path $dir ("{0}-{1}{2}" -f $stem, $n, $ext) }
  [System.IO.File]::WriteAllText($try, [string]$Text, $script:Utf8NoBom)
  return $try
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
    [Parameter(Mandatory)][string[]]$Allowed,
    [string[]]$Present = @()
  )
  if ($null -eq $Contract) { return "the session emitted no JSON that can be read" }

  $missing = @()
  foreach ($k in $Required) { if ($null -eq $Contract.$k) { $missing += $k } }

  # A field whose empty value is an answer -- `pr_number: null` is "this card has no PR", which is
  # most of them. Requiring it by value would refuse the ordinary case, and dropping it from the
  # contract would let a session that simply forgot the field through, which is the one that costs:
  # a card with an open PR picked up as fresh work opens a second PR against it. So the question
  # asked here is whether the session said anything at all, which is what `PSObject.Properties`
  # answers and what `$null -eq` cannot.
  foreach ($k in $Present) { if (-not (Test-ContractSaid $Contract $k)) { $missing += $k } }

  if ($missing.Count -gt 0) { return "missing fields: " + (@($missing | Sort-Object -Unique) -join ", ") }

  if ($Allowed -notcontains [string]$Contract.$Field) {
    return "$Field='$($Contract.$Field)' is not a value in the contract"
  }
  return ""
}

<#
  What a session has to hand over when it says a decision is owed. One shape for both of them: the
  worker meets a fork before it builds and knows only what the fork is, the audit meets one in a
  finished diff and can usually name the options too. `what` is what the grill reads first, so it is
  the one field nothing may leave out.

  Checked before it reaches a card, because a decision nobody can read is a card that comes back
  from the next grill in the same state. An unreadable one is taken as a plain hold instead.
#>
function Test-DecisionsOwed {
  param($Owed)
  $ds = @($Owed)
  if ($ds.Count -eq 0) { return "it says a decision is owed and names none" }
  foreach ($d in $ds) {
    if (-not $d) { return "one of the decisions is empty" }
    if ([string]::IsNullOrWhiteSpace([string]$d.what)) { return "a decision does not say what it is" }
    # Filtered, not just wrapped: @($null).Count is 1 in PowerShell, so an absent `options` read as
    # a decision offering exactly one -- and every worker that named a fork without listing them was
    # refused for saying too little.
    $opts = @(@($d.options) | Where-Object { $null -ne $_ })
    if ($opts.Count -eq 1) { return "'$([string]$d.what)' offers one option, which is not a decision" }
    foreach ($o in $opts) {
      if ([string]::IsNullOrWhiteSpace([string]$o)) { return "'$([string]$d.what)' has an empty option" }
    }
  }
  return ""
}

<#
  What the day does about a verdict, as a function so the probe asks exactly what the loop asks.
  Two answers and no third: integrate the PR, or leave it open and put the card somewhere.

  **Nothing here waits for anybody.** An `ask` used to stop the day until a person answered, and the
  cost of that was the day: the question arrives mid-afternoon, whoever could answer it is out, and
  every remaining cycle is spent sitting. So a decision the audit cannot make goes onto the card, in
  writing, and the card goes to `pending` where no worker touches it until a grill settles it. The
  PR stays open and green and nothing merges it on a guess.

  A verdict that contradicts itself does not merge. An audit that says `pass` and attaches a decision
  it will not make has said two things, and the reading that costs nothing is the one that does not
  put an unread diff into `main`.
#>
function Resolve-Verdict {
  param($Verdict)
  $name = [string]$Verdict.verdict
  # Filtered, not just wrapped: @($null).Count is 1, so a `pass` that simply omitted the field
  # read as owing one decision and never merged.
  $owed = @(@($Verdict.decisions_owed) | Where-Object { $null -ne $_ })

  if ($name -eq "ask") {
    $bad = Test-DecisionsOwed $owed
    if ($bad -ne "") {
      return [pscustomobject]@{ action = "recover"; to = ""; tags = @(); reason = "the audit says a decision is owed but $bad" }
    }
    # Always `pending`, never the pool: the pool is where a worker looks, and the one thing that must
    # not happen to this card is another session building on the decision nobody has made yet.
    return [pscustomobject]@{ action = "recover"; to = "pending"; tags = @(); reason = "a decision on this card is not the audit's to make" }
  }

  if ($name -eq "hold") {
    # Where a held card goes is the audit's to say, because it is the only thing that read the PR.
    # A hold because the diff is wrong puts the card back in the pool for the next session; a hold
    # because the card itself was never settled puts it back with `regrill`, so a grill reaches it
    # before another session builds on the same unanswered question. Deriving that from the word
    # `hold` alone was the orchestrator deciding something it cannot see.
    $to = ""
    if ($Verdict.card -and [string]$Verdict.card.to) { $to = [string]$Verdict.card.to }
    $tags = @(@($Verdict.card.tags) | Where-Object { $_ })
    return [pscustomobject]@{ action = "recover"; to = $to; tags = $tags; reason = "the audit held it" }
  }

  if ($owed.Count -gt 0) {
    return [pscustomobject]@{
      action = "recover"; to = ""; tags = @()
      reason = "the verdict is '$name' and it owes $($owed.Count) decision(s), which are two different answers"
    }
  }
  return [pscustomobject]@{ action = "merge"; to = ""; tags = @(); reason = "" }
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
      # Split, and not GetFileName: what arrives here is somebody's shell line rather than a path,
      # and .NET Framework's GetFileName throws on the quotes, pipes and angle brackets one is full
      # of. That reaches further than the key it computes -- this runs from the atom that closes a
      # cycle and from the one that ends the day, so one denied `(Get-Date).ToString("o")` left an
      # audit paid for and its verdict on the PR, with nothing able to record it or end the day.
      $leaf = @(($t -replace '\\', '/') -split '/')[-1]
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
# The journal
# ---------------------------------------------------------------------------------------------

<#
  What a session knew and nobody else does: what it tried and threw away, where it got to, what it
  was going to do next. The card keeps conclusions and the stream keeps transitions; neither keeps
  work half done, which is what the next session repeats.

  The lifecycle is mechanical and the content is not. These move the file; the session writes it.
#>
$script:JournalHeadings = @("Where I got to", "Tried and discarded", "What I would do next")

function Get-JournalPath {
  param([Parameter(Mandatory)][string]$Repo)
  return (Join-Path $Repo ".scratch\current.md")
}

<#
  Is there anything under the headings. Archiving a journal that says nothing files an empty file
  and loses the only record there was, so this is what the gate asks.
#>
function Test-JournalBody {
  param([Parameter(Mandatory)][string]$Path)
  if (-not (Test-Path $Path)) { return "there is no journal at $Path" }
  foreach ($line in (Read-OpenFileLines $Path)) {
    $t = ([string]$line).Trim()
    if ($t -eq "" -or $t.StartsWith("#")) { continue }
    return ""
  }
  return "the journal has its headings and nothing under them"
}

function Get-JournalTask {
  param([Parameter(Mandatory)][string]$Path)
  if (-not (Test-Path $Path)) { return "" }
  foreach ($line in (Read-OpenFileLines $Path)) {
    if ([string]$line -match '^#\s+(\S+)') { return $Matches[1] }
  }
  return ""
}

<#
  The shape is laid down before the session starts, so the session only ever adds prose under
  headings that are already there. A shape a session has to reproduce from memory is one that comes
  back wrong on the day nobody is watching.

  Whatever was already there is put away first: a cycle that died between its session and its close
  left a journal nothing has filed, and overwriting it would throw away exactly the case this
  exists for.
#>
function Reset-Journal {
  param([Parameter(Mandatory)][string]$Repo)
  $p = Get-JournalPath $Repo
  $parked = ""
  if (Test-Path $p) { $parked = Complete-Journal -Repo $Repo }
  New-Item -ItemType Directory -Force (Split-Path -Parent $p) | Out-Null
  $lines = New-Object System.Collections.ArrayList
  $null = $lines.Add("# ")
  $null = $lines.Add("")
  foreach ($h in $script:JournalHeadings) {
    $null = $lines.Add("## $h")
    $null = $lines.Add("")
    $null = $lines.Add("")
  }
  [System.IO.File]::WriteAllText($p, ($lines -join "`n"), $script:Utf8NoBom)
  return $parked
}

<#
  Parking is what happens unless the work landed, which is what makes a crash come out right
  without anything having been written for the crash: only a merge archives, and everything else --
  a hold, a question that ended the cycle, a session that died -- falls through to parked.

  An empty journal is not filed either way. There is nothing in it to find again, and a folder of
  empty files is how a real one stops being noticed.
#>
function Complete-Journal {
  param([Parameter(Mandatory)][string]$Repo, [switch]$Merged, [string]$TaskId)
  $p = Get-JournalPath $Repo
  if (-not (Test-Path $p)) { return "" }
  if ((Test-JournalBody $p) -ne "") { Remove-Item $p -Force; return "" }

  if (-not $TaskId) { $TaskId = Get-JournalTask $p }
  if (-not $TaskId) { $TaskId = "unfiled" }

  if ($Merged) {
    $dir = Join-Path $Repo ".scratch\archive"
    $name = "{0}-{1}.md" -f (Get-Date -Format "yyyy-MM-dd"), $TaskId
  } else {
    $dir = Join-Path $Repo ".scratch\parked"
    $name = "$TaskId.md"
  }
  New-Item -ItemType Directory -Force $dir | Out-Null
  $dest = Join-Path $dir $name

  # A card can come back more than once, and each attempt knows something the last did not. Moving
  # over the previous one threw away exactly the history this exists to keep -- and relying on the
  # session to have copied the old file forward puts the guarantee back in the hands that drop it.
  if ((Test-Path $dest) -and -not $Merged) {
    $before = Get-Content $dest -Raw
    $now = Get-Content $p -Raw
    $joined = $before.TrimEnd() + "`n`n---`n`n" + $now.TrimEnd() + "`n"
    [System.IO.File]::WriteAllText($dest, $joined, $script:Utf8NoBom)
    Remove-Item $p -Force
    return $dest
  }

  Move-Item -Path $p -Destination $dest -Force
  return $dest
}

# ---------------------------------------------------------------------------------------------
# One day at a time
# ---------------------------------------------------------------------------------------------

<#
  Nothing holds the day open for its whole length any more: what runs is one atom at a time, and
  between them there is no process to own a handle. So the lock is a claim with a timestamp on it,
  refreshed by every atom, and a claim nobody has touched in longer than a session can legally take
  is not a claim.

  That is weaker than a handle the OS releases on death, and the weakness is bounded: a day killed
  outright keeps the next one out for `LockStaleMinutes` and no longer.

  It also carries the run, which is what lets every atom take no arguments. A command with a path
  in it is a command something has to spell, and the layer that decides permissions splits on
  characters that appear in paths -- so the one place the current run is written down is here, and
  the atoms read it rather than being told.
#>
$script:LockStaleMinutes = 150     # the session clock is 90; this leaves room for one plus its close

function Get-LockPath {
  param([Parameter(Mandatory)][string]$OrchestratorDir)
  return (Join-Path $OrchestratorDir "day.lock")
}

<#
  Taking it is one atomic create and never a check followed by a write. Two `start-day` in the same
  second -- the scheduled one and yours -- both saw no lock and both proceeded, and from there two
  orchestrators shared a checkout, a card and a set of files while each believed it owned them.

  Taking over a stale claim is the one path that reads before it writes, and that is sound because
  stale means nothing has touched it for two and a half hours.
#>
function Enter-DayLock {
  param([Parameter(Mandatory)][string]$OrchestratorDir, [Parameter(Mandatory)][string]$LogDir)
  $p = Get-LockPath $OrchestratorDir
  $run = Split-Path -Leaf $LogDir

  if (Test-NewLock -Path $p) {
    Update-DayLock -OrchestratorDir $OrchestratorDir -LogDir $LogDir
    return ""
  }

  $held = $null
  try { $held = (Get-Content $p -Raw -ErrorAction Stop) | ConvertFrom-Json } catch { $held = $null }

  # Unreadable is not permission. A contender reading during somebody else's write sees exactly
  # this, and treating it as a free lock is how two days end up sharing a checkout.
  if ($null -eq $held -or -not [string]$held.run) { return "the lock could not be read, so it is not free" }
  if ([string]$held.run -eq $run) {
    Update-DayLock -OrchestratorDir $OrchestratorDir -LogDir $LogDir
    return ""
  }

  $age = ((Get-Date).ToUniversalTime() - ([datetime][string]$held.ts).ToUniversalTime()).TotalMinutes
  if ($age -lt $script:LockStaleMinutes) {
    return ("the day {0} holds the lock and touched it {1:N0} min ago" -f $held.run, $age)
  }

  # Taking over goes through the same atomic create as a free lock, so two contenders that both see
  # the same stale claim cannot both win: overwriting it outright let them.
  Remove-Item $p -Force -ErrorAction SilentlyContinue
  if (-not (Test-NewLock -Path $p)) { return "another day took the stale lock first" }
  Update-DayLock -OrchestratorDir $OrchestratorDir -LogDir $LogDir
  return ""
}

# Exclusive creation, which is the only part of taking a lock that has to be indivisible: two
# `start-day` in the same second both saw no lock and both proceeded.
function Test-NewLock {
  param([Parameter(Mandatory)][string]$Path)
  try {
    $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::CreateNew,
                                 [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    $fs.Dispose()
    return $true
  } catch { return $false }
}

function Update-DayLock {
  param([Parameter(Mandatory)][string]$OrchestratorDir, [Parameter(Mandatory)][string]$LogDir)
  $o = [pscustomobject][ordered]@{
    run = (Split-Path -Leaf $LogDir); dir = $LogDir
    ts  = (Get-Date).ToUniversalTime().ToString("o")
  }
  [System.IO.File]::WriteAllText((Get-LockPath $OrchestratorDir), ($o | ConvertTo-Json -Compress), $script:Utf8NoBom)
}

<#
  Released by its owner and by nobody else. An unconditional delete let a day that had already lost
  a stale claim take the lock away from the run that legitimately took it over.
#>
function Exit-DayLock {
  param([Parameter(Mandatory)][string]$OrchestratorDir, [string]$LogDir)
  $p = Get-LockPath $OrchestratorDir
  if (-not (Test-Path $p)) { return }
  if ($LogDir) {
    $held = $null
    try { $held = (Get-Content $p -Raw -ErrorAction Stop) | ConvertFrom-Json } catch { $held = $null }
    if ($held -and [string]$held.run -and [string]$held.run -ne (Split-Path -Leaf $LogDir)) { return }
  }
  Remove-Item $p -Force -ErrorAction SilentlyContinue
}

<#
  Which run the atoms are working on, off the lock and nothing else. An empty string means no day is
  open, which every atom but `start-day` treats as its own refusal to act.
#>
function Get-CurrentRun {
  param([Parameter(Mandatory)][string]$OrchestratorDir)
  $p = Get-LockPath $OrchestratorDir
  if (-not (Test-Path $p)) { return "" }
  try { $held = (Get-Content $p -Raw -ErrorAction Stop) | ConvertFrom-Json } catch { return "" }
  $dir = [string]$held.dir
  if ($dir -and (Test-Path $dir)) { return $dir }
  return ""
}

<#
  Whether something has already happened to a cycle. What sequences the atoms is a model, so an atom
  run twice is not a hypothetical -- and the second run was not harmless: closing a cycle twice
  recorded two recoveries, and because a card's destination is counted off those, the duplicate sent
  it to `pending` as though two sessions had failed to land it.

  So every atom that changes something asks this first, and a cycle already past that point is left
  alone rather than done again.
#>
function Test-CycleEvent {
  param([Parameter(Mandatory)][string]$LogDir, [Parameter(Mandatory)][int]$Cycle,
        [Parameter(Mandatory)][string[]]$Kinds)
  foreach ($e in (Read-DayEvents $LogDir)) {
    if ($Kinds -notcontains [string]$e.kind) { continue }
    if ($null -ne $e.cycle -and [int]$e.cycle -eq $Cycle) { return $true }
  }
  return $false
}

# The four ways a cycle is finished with, in one place because two atoms ask and a fifth way added
# to one of them would otherwise be missing from the other.
$script:CycleClosers = @("merged", "recovered", "decision_owed", "settled")

# The closers that write nothing to the board, so there is no half-landed state for `Test-CycleClosed`
# to guard against. A merge has no board write between deciding it and it being true; a settled cycle
# had its board write done by the worker, before this side ever saw the handoff.
$script:CloserNeedsNoMove = @("merged", "settled")

<#
  Whether this cycle is finished with -- asked before closing one and before opening the next.

  **A close counts only if it landed.** Closing a cycle is a merge or a card put somewhere, and the
  card half goes over a network: the comment can reach the board and the move fail behind it. That
  event is written either way, because what happened is what gets recorded, and a guard that read it
  as "closed" left the only repair there is -- running the atom again -- answering `already closed`
  over a card still sitting in `in review`, where no worker looks for it.

  So a rerun after a half close does the whole close again. It costs a second copy of the comment on
  the card, which somebody reads twice, and it converges: the card ends where it belongs. A merge
  needs no such test -- there is no board write between deciding it and it being true -- and `gh`
  refuses the second one anyway.
#>
function Test-CycleClosed {
  param([Parameter(Mandatory)][string]$LogDir, [Parameter(Mandatory)][int]$Cycle)
  foreach ($e in (Read-DayEvents $LogDir)) {
    if ($script:CycleClosers -notcontains [string]$e.kind) { continue }
    if ($null -eq $e.cycle -or [int]$e.cycle -ne $Cycle) { continue }
    if ($script:CloserNeedsNoMove -notcontains [string]$e.kind -and -not $e.moved) { continue }
    return $true
  }
  return $false
}

<#
  Which cycle is in play, counted off the stream rather than passed in. A cycle opens when its
  worker starts, so the number is the worker sessions already started -- and every atom after that
  worker is working on the one it opened.
#>
function Get-CurrentCycle {
  param([Parameter(Mandatory)][string]$LogDir)
  $n = 0
  foreach ($e in (Read-DayEvents $LogDir)) {
    if ($e.kind -eq "session_started" -and [string]$e.role -eq "worker") { $n++ }
  }
  return $n
}

# ---------------------------------------------------------------------------------------------
# The board, read off the CLI that prints it
# ---------------------------------------------------------------------------------------------

<#
  How many tasks the board CLI just listed. **Zero and "this cannot be read" are different answers**
  and keeping them apart is the whole reason this is a function: the caller spends a session on the
  first and ends the day on the second, and a reader that rounds an output it no longer understands
  down to an empty board would end every day in silence exactly when there is work.

  A row is two spaces, the id, and the columns after it; the empty answer says so in words. Anything
  else is `$null`, which is "the CLI said something this does not understand".
#>
function Read-BoardCount {
  param($Lines)
  $rows  = 0
  $empty = $false
  foreach ($l in @($Lines)) {
    $t = [string]$l
    if     ($t -match '^\s{2}[0-9a-z]{6,14}\s{2,}\S') { $rows++ }
    elseif ($t -match 'sin tareas')                   { $empty = $true }
  }
  if ($rows -gt 0) { return $rows }
  if ($empty)      { return 0 }
  return $null
}

<#
  The statuses a space actually has, off `tree`. The filters are asked for by name and **the CLI
  answers an unknown status with an empty list and exit 0** -- so a status that was renamed on the
  board reads exactly like a board with nothing on it. Whoever filters by name checks the name is
  still there first.

  The separator between them is an arrow the CLI prints as UTF-8 and this shell reads through the
  console codepage, so what arrives here is either the arrow or two or three bytes of nonsense. That
  is why the split is on everything that is not a plain letter, digit or space rather than on the
  character itself: the names being looked up are ASCII, and a name that is not survives as pieces,
  which is a wrong answer nobody asks for rather than a right one nobody gets.
#>
function Read-SpaceStatuses {
  param($Lines, [Parameter(Mandatory)][string]$Space)
  $inSpace = $false
  foreach ($l in @($Lines)) {
    $t = [string]$l
    # A space heads its block at the left margin, `Name  (id)`; everything under it is indented.
    if ($t -match '^(\S.*?)\s+\(\d+\)\s*$') {
      $inSpace = ($matches[1].Trim() -eq $Space)
      continue
    }
    if ($inSpace -and $t -match 'estados por defecto:\s*(.+)$') {
      return @($matches[1] -split '[^A-Za-z0-9 ]+' | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" })
    }
  }
  return @()
}

# ---------------------------------------------------------------------------------------------
# The rules
# ---------------------------------------------------------------------------------------------

$script:DefaultRules = @{
  SilenceMinutes   = 15    # a session emitting nothing for this long is stuck, not thinking
  DenialGrope      = 2     # two denied attempts at one tool is already groping for a way around
  CostFactor       = 3.0   # a cycle spending three times the median is in a loop
  CostSamples      = 3     # with fewer cycles there is no median worth having
  AbandonedMinutes = 30    # no session up and nothing on the stream: whoever was sequencing is gone
  ParksPerDay      = 2     # the second card sent back for grilling is the grill behind, not the board
}

function Get-DayRules { return $script:DefaultRules.Clone() }

<#
  Whether the day has sent back as many cards as it is allowed to. Parking is cheap by design -- one
  PR waits on a grill instead of on a merge -- but it is only cheap while it is rare, and nothing in
  the arrangement made it rare: a board with nothing grilled parks every card it reaches, one paid
  session each, and reports an empty board in the morning after emptying it itself.

  So the ceiling is here rather than in whoever reads the day: the second card back is the grill
  being behind, and the answer to that is a grill, not another cycle.
#>
function Test-ParkCeiling {
  param([Parameter(Mandatory)]$Status, [hashtable]$Rules)
  if (-not $Rules) { $Rules = Get-DayRules }
  # Distinct cards that actually reached `pending`, not park events. One card grilled and met again
  # the same day is one card the grill is answering, and counting it twice would end a day that is
  # working. A park whose move never landed is not a card back either -- it is a card still where it
  # was, which the atom stops over on its own.
  $cards = @($Status.Owed | Where-Object { $_.moved } | ForEach-Object { [string]$_.task } |
             Where-Object { $_ -ne "" } | Sort-Object -Unique)
  if ($cards.Count -lt $Rules.ParksPerDay) { return "" }
  return ("$($cards.Count) cards went back for grilling today ($($cards -join ', ')) -- the grill " +
          "is behind the board, and another cycle would only send back a third")
}

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

  # Matched on how the reason starts, not on the bare word. `end-day.ps1` writes the ending as
  # `no_tasks -- <why>`, so an equality check here called the one ordinary ending a day has an
  # anomaly, in the report, every time it happened.
  if ($Status.Ended -and $Status.EndReason -and $Status.EndReason -notmatch '^no_tasks') {
    $null = $out.Add((New-Anomaly "stop" "stopped" ("the day ended on: {0}" -f $Status.EndReason)))
  }

  # The one ending nothing else can report, because whatever would have reported it is what is
  # gone. Between cycles the next atom starts within seconds, so a run with no session up and
  # nothing on its stream for half an hour is one nobody is sequencing any more -- and the lock it
  # left behind keeps the next day out until it goes stale.
  if (-not $Status.Ended -and -not $Status.Running -and
      $null -ne $Status.IdleForMinutes -and $Status.IdleForMinutes -ge $Rules.AbandonedMinutes) {
    $null = $out.Add((New-Anomaly "stop" "abandoned" (
      "nothing has happened for {0:N0} min and no session is up: the day was left half way and never wrote an ending" -f $Status.IdleForMinutes)))
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
    IdleForMinutes  = $null
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
    Owed            = @()
    Recovered       = @()
    Settled         = @()
    Anomalies       = @()
  }

  $cycles = @{}
  $openStream = $null
  $openPid = $null

  foreach ($e in $ev) {
    switch ($e.kind) {
      "day_started" { $st.Started = $e.ts }
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
        $openPid = $e.pid
      }
      "session_ended" {
        $st.Running = $false
        $openStream = $null
        $openPid = $null
        if ($null -ne $e.cost) { $st.Cost += [double]$e.cost }
        $c = [int]$e.cycle
        if (-not $cycles.ContainsKey($c)) { $cycles[$c] = [ordered]@{ cycle = $c; cost = 0.0 } }
        $cycles[$c].cost += [double]$e.cost
      }
      "session_killed" { $st.Killed = $true; $st.Running = $false; $openStream = $null }
      "session_failed" { $st.Running = $false; $openStream = $null }
      # Seeded here as well as from the handoff, so a cycle that ended at the pick -- no card taken,
      # no worker run -- still has a row saying which card it was looking at and what it cost.
      "pick" {
        $c = [int]$e.cycle
        if (-not $cycles.ContainsKey($c)) { $cycles[$c] = [ordered]@{ cycle = $c; cost = 0.0 } }
        $cycles[$c].task = [string]$e.task_id
        $cycles[$c].outcome = [string]$e.outcome
      }
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
      # A cycle that produced no PR: the worker met a card there was nothing to build on and put it
      # where it belongs itself. Counted so the report can tell it apart from a cycle that built
      # something -- a run of these is sessions being spent to discover there was no work.
      "settled" {
        $st.Settled += [pscustomobject]@{
          cycle = [int]$e.cycle; task = [string]$e.task_id
          outcome = [string]$e.outcome; reason = [string]$e.reason
        }
      }
      # A PR left open and green because a decision on its card is nobody's here to make. It is the
      # one outcome worth counting across days: a run of them says the arrangement is wrong, not the
      # card.
      "decision_owed" {
        $st.Owed += [pscustomobject]@{
          cycle = [int]$e.cycle; task = [string]$e.task_id; pr = $e.pr_number
          decisions = @($e.decisions); said = [bool]$e.said; retagged = [bool]$e.retagged; moved = [bool]$e.moved
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

  # A session cannot be running if the atom that launched it is gone. Without this the stream says
  # it started, nothing says it stopped, and the day reads as working for as long as anybody looks --
  # while `abandoned`, which only fires when nothing is running, never gets its turn.
  if ($st.Running -and $openPid) {
    if (-not (Get-Process -Id ([int]$openPid) -ErrorAction SilentlyContinue)) {
      $st.Running = $false
      $openStream = $null
    }
  }

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

  # How long since anything happened at all. It is the only thing that can tell a day being
  # sequenced from one that was left where it stood, now that no single process lives as long as
  # the run: a session running is measured by its own stream's silence, and between cycles the next
  # atom starts within seconds.
  if ($ev.Count -gt 0 -and -not $st.Running) {
    $last = $ev[$ev.Count - 1].ts
    if ($last) {
      $st.IdleForMinutes = [math]::Round(((Get-Date).ToUniversalTime() - ([datetime][string]$last).ToUniversalTime()).TotalMinutes, 1)
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

  # First, because it is the only section naming work that is finished and not in `main`. Each of
  # these is a green PR held up by one decision nobody here could make, and the card says which.
  if ($st.Owed.Count -gt 0) {
    $null = $L.Add("## Parked on a decision")
    $null = $L.Add("")
    $null = $L.Add("The PR is open and green, and the card carries what has to be settled. Each one that reached ``pending`` goes back in the pool once a grill settles it; any that did not is called out under its own line.")
    $null = $L.Add("")
    foreach ($o in $st.Owed) {
      $pr = "-"; if ($o.pr) { $pr = "#" + $o.pr }
      foreach ($d in @($o.decisions)) {
        $null = $L.Add(("- {0} {1} - {2}" -f $o.task, $pr, [string]$d.what))
      }
      # The one line in this section somebody has to act on. Each of these steps guards the next, so
      # naming which one stopped says exactly what state the card is in and what has to be redone.
      if (-not ($o.said -and $o.retagged -and $o.moved)) {
        $missed = @()
        if (-not $o.said)     { $missed += "the decision never reached the card" }
        if (-not $o.retagged) { $missed += "it still reads as grilled" }
        if (-not $o.moved)    { $missed += "it never reached ``pending``" }
        $null = $L.Add(("  **{0} was not parked**: {1}." -f $o.task, ($missed -join ", ")))
      }
    }
    $null = $L.Add("")
  }

  # Separate from the recoveries below because it is a different bill. A recovery is work that was
  # done and not let through; this is a session spent finding out there was nothing to do. One is
  # the arrangement working, a run of them is the picker handing out cards that were already
  # finished, and the two cost the same and must not read the same in the morning.
  if ($st.Settled.Count -gt 0) {
    $null = $L.Add("## Cycles that built nothing")
    $null = $L.Add("")
    foreach ($x in $st.Settled) {
      $why = [string]$x.reason
      if (-not $why) { $why = "no reason was recorded" }
      $null = $L.Add(("- {0} - ``{1}`` - {2}" -f $x.task, $x.outcome, $why))
    }
    $null = $L.Add("")
  }

  if ($st.Recovered.Count -gt 0) {
    $null = $L.Add("## Put back rather than merged")
    $null = $L.Add("")
    $null = $L.Add("The PR is open. A card that moved cost the day nothing; one that did not is where the day stopped, and it says so on its own line.")
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
  Get-SessionResult, Get-ContractFromText, Test-ContractSaid, Repair-Contract, Save-EmittedContract,
  Test-DayContract, Test-JsonTrue, Get-SessionActivity,
  Test-DecisionsOwed, Resolve-Verdict, Get-CardDestination,
  New-Denial, Get-ResultDenials, Get-DenialKey, Group-Denials,
  Get-JournalPath, Test-JournalBody, Get-JournalTask, Reset-Journal, Complete-Journal,
  Get-LockPath, Test-NewLock, Enter-DayLock, Update-DayLock, Exit-DayLock,
  Get-CurrentRun, Get-CurrentCycle, Test-CycleEvent, Test-CycleClosed,
  Read-BoardCount, Read-SpaceStatuses,
  Get-DayRules, Get-DayAnomalies, Get-HaltingAnomalies, Get-Median, Test-ParkCeiling,
  Find-LatestRun, Get-DayStatus, Write-DayReport
