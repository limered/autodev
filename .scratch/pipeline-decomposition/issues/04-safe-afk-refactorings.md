# 04 — Which refactorings are safe AFK

Labels: `wayfinder:prototype`

Blocked by: (none)

## Question

Draw the line between refactorings the fixer can apply AFK vs those that must become HITL tickets:

- Candidates that feel safe AFK: extract-method, reduce-params, rename, dead-code delete.
- Candidates that feel HITL: cross-class moves, interface reshaping, anything changing a seam.

Make a cheap concrete artifact (a rough classification table + a couple of worked examples on real code) to react to. The output is the classification rule the quality step applies per finding. Link the artifact here.
