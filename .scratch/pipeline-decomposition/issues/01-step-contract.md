# 01 — Define the step contract

Labels: `wayfinder:grilling`

Blocked by: (none)

## Question

What is a "step" in the decomposed factory? Pin the contract every step conforms to so they compose:

- Inputs a step receives and outputs it produces (esp. how findings are passed downstream).
- How a finding is represented, and where AFK findings live (transient, in-run) vs HITL findings (persisted to tracker as `ready-for-human` issues).
- The boundary between orchestration (sequencing, the ≤3× loop, AFK/HITL branch) and a step's own responsibility.
- Which existing skills/agents map to which step, and what (if anything) is genuinely new.

Resolving this graduates the extraction, quality, architecture, and orchestration tickets into sharp form.
