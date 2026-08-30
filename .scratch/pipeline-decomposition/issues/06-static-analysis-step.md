# 06 — Design the static-analysis step

Labels: `wayfinder:grilling`

Blocked by: 01, 02, 03, 04

## Question

With the contract, tool survey, thresholds, and safe-AFK rule in hand, design the static-analysis step:

- How it discovers and runs the repo's analysers, parses faults, and classifies each per-finding AFK vs HITL.
- How AFK findings flow into implementation (feature-builder) for fixing within the run; how HITL findings become `ready-for-human` tracker issues.
- Its `AGENTS.md` config surface (tools, thresholds).

Produces the static-analysis-step spec.
