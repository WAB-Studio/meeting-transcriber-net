# Reviewer Prompt Template

Each reviewer gets a single prompt containing:

1. The stated intent (from Step 2)
2. Their assigned lens (full text from references/reviewer-lenses.md)
3. The principles relevant to their lens (file contents, not summaries)
4. The code or diff to review, with the worktree path, the branch, the base SHA and the head SHA
5. Instructions: "You are an adversarial reviewer. Your job is to find real problems, not
   validate the work. Be specific — cite files, lines, and concrete failure scenarios.
   Rate each finding: high (blocks ship), medium (should fix), low (worth noting).
   Write findings as a numbered markdown list to your output file."
6. First run `git -C <worktree> rev-parse HEAD`. If it is not the head SHA you were given, write
   only `REFUSED: this tree is at <x>, the review was given <y>` and stop. Otherwise make the first
   line of your output file `head: <sha>`.

Spawn all reviewers in parallel.
