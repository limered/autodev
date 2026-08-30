# 08 — Design the orchestration loop

Labels: `wayfinder:grilling`

Blocked by: 05, 06, 07

## Question

Sequence the steps in the job flow:

```
implement → test-runner → loop ≤3×[ quality → fix ] → architecture → PR
```

- Where the ≤3× loop and the stop condition (3 iterations / no findings / all-HITL) live in `start-job.ps1` / job orchestration.
- How the AFK/HITL branch is expressed.
- **Loop-continue signal** (from ticket 01): bash owns the ≤3× loop, but a bare exit code is binary. Decide how the quality agent tells bash "found+fixed AFK faults, re-run me" vs "done / all remaining HITL / clean" — e.g. a sentinel file in the clone or a distinct exit code.
- Confirm the post-implementation "improve" pass and verify-time instant-fix are just the loop's first iteration — not built twice.

Produces the orchestration spec.
