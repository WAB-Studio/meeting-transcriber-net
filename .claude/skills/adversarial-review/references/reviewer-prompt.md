# Reviewer Prompt Template

Each reviewer gets a single prompt containing:

1. The stated intent (from Step 2)
2. Their assigned lens (full text from references/reviewer-lenses.md)
3. The principles relevant to their lens (file contents, not summaries)
4. The code or diff to review
5. Instructions: "You are an adversarial reviewer. Your job is to find real problems, not
   validate the work. Be specific — cite files, lines, and concrete failure scenarios.
   Rate each finding: high (blocks ship), medium (should fix), low (worth noting).
   Every finding stands on its own evidence — a line, a case, a run. Return findings as a
   numbered markdown list and nothing else: your final message is the review."

Spawn all reviewers in one message so they run in parallel. Give each one its own lens and no
other lens's text, and never tell a reviewer what the author was thinking beyond the stated intent.
