# 05 — Extract the implement / test-runner boundary

Labels: `wayfinder:grilling`

Blocked by: 01

## Question

Ticket 01 already decided the direction (Q3): implement shrinks to implementation + single-test run only, and `/code-review` moves OUT into the quality agent. This ticket produces the concrete edit plan:

- Rewrite `.opencode/agents/feature-builder.md` step 2 so `/implement` no longer trails into `/code-review`; drop review from the implement skill's responsibility for this pipeline.
- Confirm `test-runner` (already existing, runs `test-harness.*` from AGENTS.md) is untouched and remains the "full tests" step.
- Identify anything else the shrink leaves stranded.

Use `/codebase-design` for the seam. Produces the extraction plan (not the code).
