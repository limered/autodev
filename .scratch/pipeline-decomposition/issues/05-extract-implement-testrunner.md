# 05 — Extract the implement / test-runner boundary

Labels: `wayfinder:grilling`

Status: closed

Blocked by: 01

## Question

Ticket 01 already decided the direction (Q3): implement shrinks to implementation + single-test run only, and `/code-review` moves OUT into the quality agent. This ticket produces the concrete edit plan:

- Rewrite `.opencode/agents/feature-builder.md` step 2 so `/implement` no longer trails into `/code-review`; drop review from the implement skill's responsibility for this pipeline.
- Confirm `test-runner` (already existing, runs `test-harness.*` from AGENTS.md) is untouched and remains the "full tests" step.
- Identify anything else the shrink leaves stranded.

Use `/codebase-design` for the seam. Produces the extraction plan (not the code).

## Resolution

The ticket's original premise was stale: `feature-builder.md` step 2 already called `/implement` and skipped the full test run — the trailing `/code-review` actually lived inside `implement/SKILL.md:13`, not the agent. Decisions:

- **Inline, don't opt-out.** feature-builder step 2 no longer calls `/implement` as a skill. Its implementation instructions are inlined directly (TDD at agreed seams, typecheck + single-test runs during work, commit), with **no** code-review branch and **no** full-test-suite mention. The `/implement` skill itself is untouched — it keeps its `/code-review` line for human callers.
- **test-runner.md untouched** — remains the "full tests" phase.
- **`/code-review` relocates to the agentic-review step (ticket 07)**, not dropped. It's an agent-invoked skill by design; in the VM run it executes AFK and writes findings as `ready-for-human` tracker issues, same shape as `improve-codebase-architecture`.
- **Renames** (executed this session to stop old names leaking): quality step → **static-analysis step** (06); architecture step → **agentic-review step** (07, now covering both `improve-codebase-architecture` and `/code-review`). Map diagram, ticket 08, and ticket bodies updated.

Edit applied: `.opencode/agents/feature-builder.md` step 2 rewritten.
