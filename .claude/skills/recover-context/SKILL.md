---
name: recover-context
description: >-
  Rebuild what a previous session did on one card, from what it left behind, so the next subagent
  starts where that one stopped instead of from nothing. Use when a card is already in progress,
  when its PR is open, or when a stage came back holding nothing. Triggers: "recover the context",
  "qué se hizo en esta card", "pick up where it died".
---

# recover-context — what the last session left

One question: **what has already been done on this card, and where did it stop?** You build nothing,
decide nothing and touch neither the board nor the tree. What you emit is a briefing another
subagent can start from.

You are given a card id, and sometimes a PR number. Everything else you find.

## 1 · The artefacts, in this order

They are ordered by how much they can be trusted. A commit is what happened; a comment is what
somebody said happened.

**1. The branch and its commits.** A branch for a card is named after it and cut from `main`.

```powershell
git branch -a --list "*<slug>*"
git log --oneline origin/main..<branch>
git diff --stat origin/main...<branch>
```

Read the commit messages whole — this repo writes them long, and they say what was decided and why,
which is most of what you are here to recover. Then the diff itself: what it touches names the
shape of the work far better than any summary of it.

**2. The PR, if there is one.** `gh pr view <n>` for the body, `gh pr view <n> --comments` for what
a review or an audit said, `gh pr checks <n>` for whether CI was ever green.

**3. The card.** `python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" task <id>` gives the
description, the `**Grilled.**` comment with the decisions already settled, and every comment a
previous cycle left — including the one saying why it was parked, if it was.

**4. The previous session's transcript, if you can find one.** This is the extra, never the
foundation: it is the only place the reasoning behind a discarded approach survives, and it is also
the thing most likely to be missing or truncated. Transcripts live under
`~/.claude/projects/<slug>/*.jsonl`, newest last. Search for the card id; take what it says about
what was tried and abandoned, and believe the git history over it wherever the two disagree.

**Never wait on a transcript and never fail for want of one.** The first three are enough to hand
over a working session, and they are the only three that are durable.

## 2 · Read the state, do not infer it

Three questions have to be answered from what you read, and each of them has been got wrong before
by assuming:

- **Is the work in `main` already?** A card in progress whose PR was merged is finished, not
  half-done. `gh pr list --search "<task_id>" --state merged` answers it in one call.
- **Does the branch have commits that were never pushed?** `git log origin/<branch>..<branch>`.
  Work sitting in a local commit with no PR is the case that most looks like nothing happened.
- **Is CI green on what is there?** A branch with a red build is not a branch to continue from
  without saying so.

## 3 · What you emit

Prose, not JSON: what you produce is read by a model that is about to work, and a briefing is the
right shape for that. Keep it to a screen and lead with the state.

Say, in this order:

1. **Where it stands** — one sentence. Branch, commits, PR and its state, CI, card status.
2. **What was built**, off the commits and the diff. Name files and what changed in them.
3. **What was decided**, off the commit messages, the PR body and the card's comments — above all
   anything settled by a grill, which the next session must not reopen.
4. **What is left**, as far as the artefacts say it. Be plain about the boundary between what you
   read and what you are inferring.
5. **What was tried and abandoned**, if a transcript gave it to you. Mark it as coming from there.

**Say what you could not find.** A briefing that quietly omits the PR because `gh` failed reads
exactly like one for a card that never had a PR, and the session that acts on it opens a second one.
