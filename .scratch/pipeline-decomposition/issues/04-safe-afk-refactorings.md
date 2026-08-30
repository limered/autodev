# 04 — Which refactorings are safe AFK

Labels: `wayfinder:prototype`

Blocked by: (none)

## Question

Draw the line between refactorings the fixer can apply AFK vs those that must become HITL tickets:

- Candidates that feel safe AFK: extract-method, reduce-params, rename, dead-code delete.
- Candidates that feel HITL: cross-class moves, interface reshaping, anything changing a seam.

Make a cheap concrete artifact (a rough classification table + a couple of worked examples on real code) to react to. The output is the classification rule the quality step applies per finding. Link the artifact here.

## Resolution (closed)

Artifact: [04-findings.md](04-findings.md) — a 15-row AFK / HITL / CONDITIONAL table + two worked examples on real `runView.js` code.

**Rule:** declared refactoring kind is the fast pre-filter; the ticket-03 patch-shape gate (1 file, no public-signature delta, no new import, cov ≥ 0.5) is ground truth and always wins. HITL kinds never touch code — they write `ready-for-human` issues.

Knobs decided:
1. **Unclassifiable kind → default HITL** (cheaper to under-fix than ship a wrong refactor).
2. **Public-symbol rename stays flatly HITL for v1** — don't special-case the multi-file gate for LSP rename yet; revisit with telemetry.
3. **The table lives as a referenced doc the quality agent reads**, not baked into the prompt — tune the table without editing the agent.
