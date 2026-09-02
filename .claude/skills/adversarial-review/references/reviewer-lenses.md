# Reviewer Lenses

Three distinct adversarial perspectives. Each reviewer adopts one lens exclusively.

## Architect

Challenge structural fitness. Ask:

- Does the design actually serve the stated goal, or does it serve a goal the author assumed?
- Where are the coupling points that will hurt when requirements shift?
- What boundary violations exist? Where does responsibility leak between components?
- What implicit assumptions about scale, concurrency, or ordering will break first?

Map findings to: boundary-discipline, foundational-thinking, redesign-from-first-principles.

## Skeptic

Challenge correctness and completeness. Ask:

- What inputs, states, or sequences will break this?
- What error paths are unhandled or silently swallowed?
- What race conditions or ordering dependencies exist?
- What does the author believe is true that isn't proven?
- Where is "it works on my machine" masquerading as verification?
- Where does the diff move the bar rather than clear it? An `ISA.md` claim this change ticks whose
  words were narrowed to fit — in the diff, or ahead of the branch and so in no diff — proves
  nothing, whatever its probe says. `git log -L '/^- \[.\] ISC-N: /,+1:ISA.md' origin/main` prints
  what the claim used to say.

Map findings to: prove-it-works, fix-root-causes, serialize-shared-state-mutations.

## Minimalist

Challenge necessity and complexity. Ask:

- What can be deleted without losing the stated goal?
- Where is the author solving problems they don't have yet?
- What abstractions exist for a single call site?
- Where is configuration or flexibility added without a concrete second use case?
- Is this the simplest possible path to the outcome, or is it the path that felt most thorough?

Map findings to: subtract-before-you-add, outcome-oriented-execution, cost-aware-delegation.
