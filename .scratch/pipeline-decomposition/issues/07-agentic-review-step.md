# 07 — Design the agentic-review step

Labels: `wayfinder:grilling`

Blocked by: 01

## Question

Design the pre-PR agentic-review step. It houses two agent-invoked review skills, both run AFK, neither fixes in the run — each writes findings as `ready-for-human` tracker issues to grill/refine/drop later:

- `improve-codebase-architecture` — architecture findings.
- `/code-review` (relocated here from `/implement`, per ticket 05) — Standards + Spec review of the changes since the base branch, against the issue. It is an agent-invoked skill by design; in the VM run there is no live human, so its findings are written to the tracker rather than discussed.

Decide:
- The common finding format both skills emit.
- How they reuse vs diverge from the existing interactive HTML-report skill (which is HITL by design).

Produces the agentic-review-step spec.
