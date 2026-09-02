# ISA format — the file-shape contract

Adapted from the LifeOS ISA format spec v2.18.0. This is what `IsaStructureTests` parses, so a
change here is a change to a test.

## Frontmatter

```yaml
---
phase: scoping | climbing | learning | complete
progress: M/N
updated: YYYY-MM-DD
---
```

`progress` is a mechanical count of `- [x]` over all claims, never an opinion — the gate test
recomputes it and fails on a mismatch. `phase` is `complete` only when `M == N`.

LifeOS's `slug`, `iteration`, `parent`/`children`, `density_score`, `frame_drift` and the
principal-goal block are omitted: they serve a hierarchy of task ISAs and a run-scoring system
that does not exist here. This repo has exactly one ISA, and it is a project ISA.

## Sections

Order is fixed. An empty section is excluded from the file entirely.

| # | Section | Purpose | Required |
| --- | --- | --- | --- |
| 1 | `## Goal` | The hard-to-vary spine. 1–3 sentences naming verifiable done. | always |
| 2 | `## Features` | Claims, grouped as `### F<n> · <name>` blocks. | always |
| 3 | `## Not yet specified` | Fog — in-scope questions too dim to be claims. | when populated |
| 4 | `## Learning` | Conjecture / refuted-by / learned / criterion-now. | when understanding changed |
| 5 | `## Verification` | One-line provenance stub per closed claim. | always |

**`## Test Strategy` is deliberately absent.** LifeOS gives every claim a table row naming its
probe; here that was sixty-five rows of near-identical `dotnet test … --filter-class`
boilerplate, and it was dropped on 2026-08-07. The consequence is worth
knowing rather than forgetting: an open claim does not carry the probe it will close on, so
whoever picks the work up decides what would falsify it. A closed claim still names its evidence
— that is what the `## Verification` stub is.

**`## Decisions` is deliberately absent**, and it was here until 2026-08-13. A decision has a
place it is read, and the ISA is not it: a rejected alternative is re-proposed by somebody
editing the file where it would go, not by somebody reading the claims surface, so the entry
that would have stopped them was filed where they were never going to look. Thirty-five of them
had accumulated, over half already said in the code, in `docs/`, in `## Learning` or in a claim,
and the section was reaching a third of the file. Where each kind goes now:

- **A rejected alternative** — a comment in the file where somebody would try it again. That is
  the one destination the ISA cannot serve.
- **What changed a claim** — the claim itself. `refined:` recorded that a claim had been rewritten
  in a log nobody diffs; `git log -- ISA.md` is that, exactly.
- **What a probe taught** — `## Learning`, whole entries only.
- **What the product is and why** — `arquitectura.md`, which is what it is for.
- **How one session got somewhere** — the commit message and the board comment. Neither is read on
  every task, which is the point.

**Problem, Vision, Out of Scope, Principles and Constraints are deliberately absent.** They are
`arquitectura.md` — §1 decisions, §2 overview, §16 deferred decisions — and duplicating them
here would create exactly the parallel artifact the ISA doctrine forbids. `## Goal` points at
the sections it rests on. If prose and claims ever disagree, that is a finding, not a formatting
problem: one of the two is wrong about the product.

`## Language` is absent for a simpler reason — CLAUDE.md's contract section already fixes the
vocabulary this product argues about, and a term enters a glossary only after it has actually
caused a confusion.

## Feature blocks

```markdown
### F0 · Cross-cutting
Why: one line — why this feature exists and what done means for it.
Board: —
- [x] ISC-1: claim text
- [ ] ISC-2: Anti: what must not happen

### F1 · Recording
Why: ...
Board: 3 · Grabador WinUI
- [ ] ISC-3: ...
```

- `F0` is reserved for claims that span features — the contract invariants, offline tests, the
  build staying clean. Its `Board:` is `—`; cross-cutting work has no single list.
- Features are `F1`, `F2`, … in build order, and each `Board:` names one ClickUp list in the
  `MeetingTranscriber` space, verbatim. The gate test checks the list exists.
- `Why:` is load-bearing. It states what the name and the claims do not — restating either is
  noise.
- ISC IDs are global and stable across the whole file, never per-feature.

## Claims

- **The text says what must be true, not what makes it true.** No type, method, project or test
  name in a claim — a claim outlives the design under it, and the evidence stub is where the test
  is named. A claim a rename would falsify was never about the product.
- `- [x]` closed, `- [ ]` open. A claim closes on evidence, never on a task's status.
- **A claim is written open, and reaches `main` in its final words before the work that closes it
  starts.** Writing it already ticked beside the work it judges scores that work against a bet it
  never had to clear, and rewording it there scores the work against a bet rewritten to fit. Checks
  15 and 16 refuse both, so sharpening a claim the branch will close is its own change on `main`.
- `Anti:` prefixes a claim about what must *not* happen. At least one is required overall — a
  goal with no failure mode worth naming is under-specified.
- Nested IDs (`ISC-7.1`) organise a split; the atomicity rule applies at the leaves.
- **An ID is immutable.** Issued once as the next number after the highest, and after that never
  renumbered, never reused for a different claim, never moved. A split keeps the parent as
  container. A drop leaves a tombstone: `- [ ] ISC-9: [DROPPED 2026-08-07: what stopped being
  true]`, so references elsewhere stay valid. The line says why in itself — a tombstone pointing
  at a section for the reason is one more thing that can go missing, and `git log -- ISA.md` holds
  the rest.
- **The numbering was reset once, on 2026-08-14**, when the claims were rewritten to say what must
  be true rather than which type makes it so, and the duplicates that had accumulated were merged.
  127 claims became 114 and every ID moved. Anything written before that date — a commit message,
  a board comment, a review — names a claim by a number that now belongs to another one, so an ID
  read out of history is checked against `git show` of the ISA at that commit and never against
  the file as it stands. It is the only reset, and the rule above is what makes it the last.
- **A new ID is issued only when nothing here already says it.** The gate cannot check that, so
  the skill's Scaffold workflow carries it as a rule on the writer: every claim is read before one
  is added, and a statement an existing claim encapsulates sharpens that claim or becomes a leaf
  under it. What the gate can check is the arithmetic of immutability, which is check 10.

## Verification stubs

One line per closed claim, and the line is a pointer, not a paragraph — the proof lives in git
and CI:

```markdown
- ISC-11 — `TurnsTests` green 2026-08-07
- ISC-15 — `git grep` over `tests/` returned no match 2026-08-07
```

Name the test class or the command precisely enough that the next reader can re-run it. A test
class alone is enough when the whole class is the probe; name the method when one method is. Naming
them costs nothing: gate 11 measures the prose left when the backticked spans come out, so four
method names are free and the sentence explaining them is not. Free has a ceiling on it:
gate 12 bounds the whole stub, so a probe walk long enough to reach it is one to shorten
rather than one to tick.

**Say what the evidence does not reach.** A claim marked `[x]` reads as fully probed, and most are
not: a hand run covers one machine, a unit test cannot open a device, half a claim is argued off
the code rather than measured. That sentence is provenance and its only home is the stub — the
design argument belongs in the file it explains, but which half of a closure nobody ran belongs
where the closure is recorded. It is also the first thing a collapse deletes, because it is pure
prose where a test name is free, so gate 11 prices it at exactly the cost of the retelling it
exists to stop. Cut a sentence that argues before one that bounds.

**A `dotnet test` probe names its project.** `dotnet test --filter "FullyQualifiedName~X"` is
silently ignored by the Microsoft.Testing.Platform runner that xunit.v3 uses — it runs
everything and exits 0, which is a pass that proved nothing. The form that works is
`dotnet test tests/<Project> --no-build -- --filter-class "*ClassName"`; without the project,
the non-matching projects exit non-zero on zero tests.

**A paid run is never CI evidence.** Tests never touch the network and never spend credits, so a
claim that only a live Deepgram run can close records that run's date and approved budget in its
stub, and CI is not asked to hold it.

## Probe placement

Attach the probe where the thing meets its consumer, and verify through that boundary rather
than reaching into internals. A claim about stored names is probed by reading the schema, not by
asserting on the convention that produced it — that is why `CorpusNamingTests` spells out every
name on disk.

## Structural gates

Mechanical, enforced by `IsaStructureTests`, and hard failures:

1. `progress:` equals the actual `[x]`/total count.
2. `phase: complete` requires `M == N` and an empty `## Not yet specified`.
3. Every `### F<n>` block has a `Why:` and a `Board:` line naming a known list.
4. No ISC ID appears twice.
5. Every closed claim has a `## Verification` stub.
6. The sections that are present appear in the fixed order.
7. Every bullet inside a feature block parses as a claim line.
8. Every non-blank line under `## Verification` parses as a stub.
9. `## Learning` is whole entries of four labels in order, and nothing else.
10. No number is missing between the first ID and the last, at the root and under every parent.
11. No `## Verification` stub carries more prose outside its pointers than `LongestStubProse`.
12. A stub's backticks close, no backticked span runs past `LongestPointer` for free, and its
    evidence runs no longer than `LongestStubProse` and `MostFreePointers` together.
13. Every `## Verification` stub names a claim above, and that claim is marked closed.
14. No ISC ID carries two `## Verification` stubs.
15. No claim is closed by the change that wrote it — a claim marked `[x]` that `origin/main` does
    not carry at all is a claim born ticked.
16. No claim is closed in words the change also wrote — a claim marked `[x]` saying something other
    than what `origin/main` had it saying while it was open is a claim reworded into its closure.
17. No closed claim is reworded and left on the evidence for its old words — a claim `origin/main`
    already had `[x]`, saying something else here, whose `## Verification` stub has not moved.

The first two are one rule in two shapes: **a closure is scored against what `main` was already
carrying, under that id and in those words.** The third is the rule after it: **a claim that moves
takes its evidence with it.** All three read history, because these are the rules the file cannot
state about itself — a claim marked `[x]` reads the same whether it stood open for a week or was
written that way in the diff that closes it, and a stub reads the same whether it was written for
the sentence above it or for one since rewritten.

What they compare the working tree against is the trunk before this change, so they answer before
the change is committed, and CI owes them `fetch-depth: 0` at the checkout. On a branch that is the
fork point from `origin/main`. Standing on `main` it is HEAD, so the pass before a direct push is a
real comparison and refuses one — but once the commit exists there is nothing left to compare, which
is why **the push event says where it began and CI on `main` reads the gates against that.** Nothing
infers the route: an inference from HEAD sitting on the trunk is equally true of a branch nobody has
committed on, and would judge that branch against the trunk minus its own tip.

That backstop is an alarm and not a refusal — the push has happened by the time it speaks. What it
buys is that a direct push nobody ran the pass over is named on `main` rather than never, and that
holds for a merge too: a branch merged after `main` reworded a claim it ticks goes red on the trunk
though its own run was green, because that is the state ISC-176 forbids and the merge is what made
it true. The answer is a commit that re-runs the probe under the words `main` now carries or puts
them back, and either is a push.

Check 16 reaches a claim `main` had **open**, and nothing else. A claim already closed there is not
being closed by this change whatever happens to its words, and the repo has twice moved one
correctly: `ISC-121` in PR #58 and `ISC-120` in PR #74 each followed a product that had changed
under a standing closure, and each rewrote its `## Verification` stub in the same commit to say so.
Check 17 is that habit made mechanical, and it runs one way: a stub is rewritten whenever a probe is
re-run, and a claim that has not moved has nothing to say about that. Replayed on 2026-09-02 over
every first-parent commit that had touched `ISA.md`, it names one — `ISC-157`, widened on 2026-08-25
and left standing over a run against the narrower sentence — and no other, which is the whole of its
false-positive measurement. A re-run **replaces** a stub rather than accumulating under it: checks 11
and 12 bound what one may say, so the newest probe is what a stub carries and the ones before it are
in `git log -- ISA.md`.

Ids, words and evidence are all they compare, so reordering the file and moving a claim between
blocks stay invisible, and renumbering a closed claim reads as one appearing — which is right, since
check 10 refuses renumbering anyway.

**What is left for a reviewer.** The gates only push the reword out of the diff that ticks it. They
cannot see whether it was written ahead of the work or worded to fit it, wherever it landed — a
direct push to `main`, an `ISA.md`-only pull request, or the branch that built the thing and left
the claim open for a later one to tick. That residue is one question, *did this claim say this
before anybody knew what would be built?*, and it is asked in `.claude/agents/auditor.md`,
`.claude/skills/github/SKILL.md` and the Skeptic lens, which are where a PR is read.

Check 17 leaves two of its own there. It sees that a stub's bytes moved and never that a probe was
run, so a date bumped over a run nobody made passes it; and it reaches a claim whose *words* moved,
never one the world moved under — `ISC-157` will read stale again the day `ISC-157.1` lands and
somebody can ask for work beforehand, with its sentence untouched and every gate quiet. Both are the
same question in the end, *was this evidence produced against this sentence?*, and it is asked
wherever the one above is.

There is no exception for a split, and narrowing is not a way round one. Splitting a closed claim
into new ids marked closed is a second closure however it reads, so check 15 refuses it; narrowing
an open claim's text under the same tick is check 16's. **A narrowing goes in a change that does not
tick it** — on `main` or in a pull request, either is fine — and the branch that closes it ticks a
line it did not write. The one time the repo split a closed claim — `ISC-139` in PR #63 — the new
leaf was about a screen that same pull request built, which is the defect and not the exception.

Check 11 was advisory until 2026-08-26, on the argument that a pointer cannot be told from a
paragraph by counting characters. That holds for the line, which here is mostly test names — seven
of them is 668 characters saying nothing but where to look — and not for what is left once the
backticked spans come out, which on the file of that day separated cleanly into stubs nobody had
flagged and stubs that were essays. The three numbers live on `LongestStubProse` and
`MostFreePointers` in `IsaStructureTests` and on `LongestPointer` in `IsaDocument`, each with the
measurement it came off; changing one means editing the constant there, and this line is
deliberately not a second copy of it.

The reason it is a gate and not a note is where the rule lived. Whoever appends to an append-only
section greps their own ISC and never opens the file, so they take the neighbour as the model and
never read this line at all. Check 12 is there because the measure can be read as exact and is
not: ticks are what makes a span free, so unclosed ones swallow the prose between two of them,
an unbounded one launders a paragraph as a pointer, and an unbounded number of bounded ones
launders the same paragraph in fourteen pieces. Pricing a span cannot reach the third, so what
closes it is the second half of check 12, which measures the whole stub and not the prose left in
it. That is a bound and not an exactness: `IsaDocument` and `IsaStructureTests` say which case
each still lets through, and one of them is a person's to see rather than the gate's — a stub over
budget can buy a second budget by becoming two claims, and nothing mechanical tells that from a
claim that was always two.

Check 10 is the other hand of check 4. A duplicate ID is a number issued twice; a hole is a claim
deleted instead of tombstoned, or a run of them shifted down to close one — which silently
re-points every board task and stub that named the old number. Together they leave one shape a
renumber cannot take.

Checks 7 to 9 exist because a parser that skips what it does not recognise reports a spliced
section as a sound one. On 2026-08-13 an append landed on the first line of an existing entry and
welded two of them together; the gate was green over it, so were three adversarial reviewers, and
it merged. What a section holds is now read exhaustively, and a line the shape does not describe
is a failure rather than a line nobody parsed.

The board check is against a hardcoded list of the eight phase lists, because tests never touch
the network. It catches a `Board:` line invented or mistyped here; it cannot catch a list renamed
in ClickUp. Renaming a list means editing that array in `IsaStructureTests`.

Advisory, reported by CheckCompleteness and never blocking — a count that blocks just gets
manufactured:

- at least one `Anti:` claim exists;
- no claim bundles two verifiable things;
- nothing shipped that no claim asked for.
