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

- `- [x]` closed, `- [ ]` open. A claim closes on evidence, never on a task's status.
- `Anti:` prefixes a claim about what must *not* happen. At least one is required overall — a
  goal with no failure mode worth naming is under-specified.
- Nested IDs (`ISC-7.1`) organise a split; the atomicity rule applies at the leaves.
- **IDs never renumber.** A split keeps the parent as container. A drop leaves a tombstone:
  `- [ ] ISC-9: [DROPPED 2026-08-07: what stopped being true]`, so references elsewhere stay
  valid. The line says why in itself — a tombstone pointing at a section for the reason is one
  more thing that can go missing, and `git log -- ISA.md` holds the rest.

## Verification stubs

One line per closed claim, and the line is a pointer, not a paragraph — the proof lives in git
and CI:

```markdown
- ISC-11 — `TurnsTests` green 2026-08-07
- ISC-15 — `git grep` over `tests/` returned no match 2026-08-07
```

Name the test class or the command precisely enough that the next reader can re-run it. A test
class alone is enough when the whole class is the probe; name the method when one method is.

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
5. Every closed claim has a `## Verification` stub, and no open claim has one.
6. The sections that are present appear in the fixed order.
7. Every bullet inside a feature block parses as a claim line.
8. Every non-blank line under `## Verification` parses as a stub.
9. `## Learning` is whole entries of four labels in order, and nothing else.

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
- nothing shipped that no claim asked for;
- a stub reads as a pointer rather than a paragraph. Advisory and not a character count, because
  the one honest exception has no test to point at: a claim closed on a hand run carries the run's
  numbers, since nothing else holds them and CI will never produce them again. A stub naming a
  test class has no such excuse — the class is the evidence, and the narrative belongs in the PR.
