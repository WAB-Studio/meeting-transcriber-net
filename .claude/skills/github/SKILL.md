---
name: github
description: >-
  This repo's GitHub workflow — issues, the board, branches, PRs and review. Use when opening or
  closing an issue, starting a task, naming a branch, writing a PR or reviewing one. Triggers:
  "issue", "the board", "el board", "the PR", "el PR", "open a task", "abrí una tarea", "what do I
  do now", "qué hago ahora", "merge", "mergear", "review".
---

# GitHub — meeting-transcriber-net

Reading is free. Writing is not: a session with the user publishes what the user asked for in those
words, and an unattended agent writes what its own contract in `.claude/agents/` says it writes —
nothing either way that was not asked for.

## Two flows, not one

Shipping code and tracking work are separate. They meet only when an issue already existed.

```
ship:   branch  →  commits  →  the four commands  →  PR  →  review  →  merge commit
track:  issue   →  ...  →  closed when the thing it describes stopped being true
```

1. **A PR does not need an issue.** Never open one so the PR has something to close. An issue is
   opened when work has to be remembered — nobody is doing it yet, or it waits on a decision. Work
   being done right now goes straight to a branch.
2. **Branch from `main`**: `feat/`, `fix/`, `chore/` or `docs/` + a short slug —
   `feat/live-transcription-socket`, `fix/spool-export-partial`.
3. **The four commands green before the PR** — `dotnet restore`, `dotnet format
   --verify-no-changes`, `dotnet build --no-restore -warnaserror`, `dotnet test --no-build`, each on
   its own line. They are exactly what CI runs. A PR is never opened red.
4. **One PR, one issue.** If it would close two, either the issue was split wrong or the PR was.
5. **Merge commit**, always, and the branch goes with it — `--delete-branch`, because the repo does
   not delete it on its own. `main` is the only long-lived branch. There is no `develop`.

## Issues

**Title:** imperative, English, no ID prefix. `Split the recorder screen out of the diagnostic panel`.

**Body:** what is broken today and which claim it points at. Nothing else.

```markdown
Today the recorder opens on a diagnostic panel, and the meeting that stopped mid-recording is
announced nowhere.

**Claim:** `ISA.md` · ISC-142
```

When the work belongs to no claim — dependencies, cleanup, a formatting adjustment:

```markdown
**Claim:** none

**Done when:** <what has to be true, and how you know>
```

- **The issue points at the ISA, it does not copy it.** Cite the ID and nothing more; the text lives
  where it can change. If what has to be achieved should be a claim and is not, it gets written in
  `ISA.md` first — through the `isa` skill, never by hand. The ISA never points back: no issue
  number goes into `## Features`.
- **An issue chases leaves, not containers.** If the claim it cites gets split, the issue gets split
  or keeps one leaf.
- **The description is the present.** New information replaces the old; no "update:" stacked
  underneath. GitHub keeps the history.
- No research, no data tables, no account of the path walked — that goes in a comment. A dependency
  on another issue is a reference, not a paragraph: `**Depends on:** #N — <what it is waiting for>`
  when this card cannot close without that one, and a bare `#N` when it only came from there.

### Labels

**Two labels, and they answer different questions.** An issue is labelled by what is asked for, not
by the diff that will come out: the `feat:`/`fix:` prefix belongs to the branch and the PR.

| Label                               | When                                                      |
| ----------------------------------- | --------------------------------------------------------- |
| `enhancement` `bug` `documentation` | Type — exactly one                                        |
| `F0` … `F8`                         | The ISA feature it belongs to — exactly one               |
| `question`                          | Does not move until somebody decides. Usually this is fog |
| `help wanted`                       | Depends on somebody outside. Say who                      |

The `F` labels mirror `## Features` in `ISA.md` and are the only ones invented here; everything else
is what GitHub ships with. A claim's feature decides the label, so `F0` is cross-cutting work, and
an issue with no claim takes the feature its work lands in.

`duplicate`, `invalid` and `wontfix` go on at close, never on an open issue, and the comment
explaining the close matters more than the label.

## The board

Issues say what the work is. **The board says what order it goes in** — `WAB-Studio` project **1**,
`Meeting Transcriber`. What it shows is `Status` and the labels; the feature and the claim live on
the issue, not in a field that can drift from it.

Only issues are items. Never add a PR to the board. `Closes #N` couples them, and that link lives in
the repository, not in the project.

| Status        | Means                                      | Moves                          |
| ------------- | ------------------------------------------ | ------------------------------ |
| `Backlog`     | Exists so it is not forgotten. Not defined | Auto, when the issue is opened |
| `Ready`       | Defined. Taken from the top                | You, when you define it        |
| `In progress` | Has a branch, no PR yet                    | **You, when you branch**       |
| `In review`   | Has an open PR                             | Auto, on `Closes #N`           |
| `Testing`     | Merged. Nobody has run it and confirmed    | **You, when it merges**        |
| `Done`        | Confirmed by a person                      | Auto, when you close the issue |

Drag two cards, ever: `Ready → In progress` and `In review → Testing`. The rest moves itself.

Put `Closes #N` in every PR that has an issue.

Never move a card to `Done` for merging. Move it to `Testing`, confirm it works, then close the
issue. Nothing has shipped and there is no deploy, so confirming is running the built app, or
reading the probe that ran. The evidence that closes the card is the evidence that ticks its claim.

**The cost is paid on the way in.** `Backlog` is a title. `Ready` is this template filled in,
replacing the body rather than stacking under it.

```markdown
<what is wrong today, in a line or two>

**Claim:** ISC-N, or `none`. The id and nothing else.

**Delivers**

- <what somebody can do that they could not before>
- <one bullet each: the requirement, never how it is built — that is the PR's>

**Screen:** `docs/design/<Artboard>.dc.html`, or `none`.

**Proof:** <what a person does to see it work>, and the automated tests there should be. Few.

**Depends on:** #N. Drop the line when nothing blocks it.

**Decisions**

- **<the fork, and the answer>** (YYYY-MM-DD). Drop the section when no grill settled anything.
```

Those four — `Claim`, `Delivers`, `Screen`, `Proof` — are required, and `none` is a whole answer for
two of them. Never invent a screen to fill the line. A card missing one is not `Ready`, and the
picker sends it back.

**A block does not hold a card out of `Ready`.** `Ready` means defined and nothing else. What is
waiting rides on the card as `**Depends on:** #N`, and whoever takes it looks then at whether that
issue is closed — a block lifted last week should not have cost the card a column move nobody made.

**The order inside `Ready` is the user's judgment.** No agent invents it or slips a card ahead. An
agent may _propose_ an order with the context the user lacks — a dependency, a claim already closed
— and writes it once they give the word.

**Column moves follow a verifiable fact**, never an opinion: branch → `In progress`, PR → `In
review`, merge → `Testing`, a person confirming → `Done`. A PR closed without merging sends the card
back, out loud.

**A card comes back when it turns out not to be defined** — whoever takes it finds a structural
decision nobody made, or information the card does not carry. It returns to the top of `Backlog`,
its `Delivers` is replaced by what is missing, and it gets `question` if that is the user's
decision. **The branch survives with whatever landed on it**, and the card names it, so the work is
not redone when the decision arrives. Coming back is the queue correcting itself; working around the
gap is not.

One call gives the queue in order, each card with its `status`, its `labels` and its issue number.
Moving one is two: find the item, set the field.

```powershell
gh project item-list 1 --owner WAB-Studio --format json --limit 200
$item = (gh project item-list 1 --owner WAB-Studio --format json --limit 200 | ConvertFrom-Json).items |
        Where-Object { $_.content.number -eq <n> } | Select-Object -ExpandProperty id
gh project item-edit --id $item --project-id PVT_kwDOCo2sl84BhFA- `
  --field-id PVTSSF_lADOCo2sl84BhFA-zhgCKFM --single-select-option-id <option>
```

`Backlog` `f75ad846` · `Ready` `61e4505c` · `In progress` `47fc9ee4` · `In review` `df73e18b` ·
`Testing` `1811706d` · `Done` `98236657`

`Auto-add to project` puts every new issue in `Backlog` on its own. Never add one by hand.

## Pull requests

**Title:** one line, English, imperative, under 72 characters, saying what the change does.
`Open the recorder on the main screen, not the diagnostic panel`

```markdown
Closes #142.
Claims: ISC-142, ISC-143.

## What changed

`MainWindow` is the shell the app starts in, and the diagnostic panel is gone.

## Why

The door into the app was a panel built to debug capture, so the first thing a person saw was
something nobody outside this repo can read.
```

- **Two different lines.** `Closes #N` is the issue, and it only appears when the issue existed
  before the branch — no issue, no line. `Claims:` is the ISA, and is `none` when the PR closed no
  claim. Neither ever names something that is not there.
- A claim closes on its probe, recorded in `## Verification` through the `isa` skill, **in the same
  PR**. An ISA updated afterwards never gets updated.
- **Optional section:** `## Additional notes` — the riskiest part, what was left out, what has to
  happen next. There are no others. Keep it to a screen: the review, how it was diagnosed and what
  proved it stay out.

**Reviewing:** anything specific goes inline on the line; one summary comment with the verdict and
nothing else; `nit:` up front when it does not block. You do not approve your own PR, and design is
discussed in the issue — in the PR the code is already written.

## Commands

```bash
gh issue create --title "..." --label enhancement --label F4
gh issue list --label question           # what is stuck waiting on a decision
gh pr create --fill
gh pr checks
gh pr merge --merge --delete-branch
```

`gh project` needs a token with the `project` scope: `gh auth refresh -s project`, once per machine.
