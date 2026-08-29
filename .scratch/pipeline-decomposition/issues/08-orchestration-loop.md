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
- Confirm the post-implementation "improve" pass and verify-time instant-fix are just the loop's first iteration — not built twice.

Produces the orchestration spec.
