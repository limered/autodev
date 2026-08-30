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

## Resolution (closed)

Ground truth: steps are ALREADY separate `mode: primary` opencode agents (`.opencode/agents/*.md`), each run one-shot headless via `opencode run --agent <name>` in a shared clone, sequenced by `infrastructure/multipass/test-feature-builder.sh` (feature-builder → gate → test-runner → pr-author) with exit code as the gate. The contract mostly exists; these are the deltas.

**Step contract (locked):**
- A step = a primary opencode agent, own model, one-shot run, text SPEC block input (`ISSUE`/`BRANCH`/`BASE`/`REPO`), shared git clone, communicates downstream via branch commits + exit code.
- **AFK findings** → fixed in-run as commits.
- **HITL findings** → the step writes them straight to the tracker as `ready-for-human` GitHub issues via the PAT already in the VM (Q1a). Scan steps therefore get tracker *write* access.
- **≤3× quality loop owned by the bash orchestrator**, not the agent — fresh agent context per iteration for determinism, bounded context, lower cost (Q2a). Requires a loop-continue signal richer than a binary exit code (→ ticket 08).
- **implement shrinks** to implementation + single-test run only. `/code-review` moves OUT of implement/feature-builder and INTO the quality agent (Q3).
- **Two new primary agents**: `quality.md`, `architecture.md`. Nothing else.
- **Security deferred** to v-next (Q4): it's HITL-advisory (per ticket 02), same shape as the architecture step, so near-free to add once ticket 07 builds the HITL-scanner mold. Not pulled into v1. (If later treated as a hard PR-blocking gate rather than a fixer, that's a separate decision.)
