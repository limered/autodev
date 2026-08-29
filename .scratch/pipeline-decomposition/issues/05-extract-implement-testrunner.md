# 05 — Extract the implement / test-runner boundary

Labels: `wayfinder:grilling`

Blocked by: 01

## Question

Given the step contract, decide the exact split for the first extraction:

- `implement` keeps the tdd skill: write the test and run that *one* test/file only.
- `test-runner` (already in the loop) owns the "full tests" step.
- What, concretely, moves out of today's monolithic agent, and what stays.

Use `/codebase-design` for the seam. Produces the extraction plan (not the code).
