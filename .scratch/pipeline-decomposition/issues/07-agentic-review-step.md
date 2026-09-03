# 07 — Design the agentic-review step

Labels: `wayfinder:grilling`
Status: closed

Blocked by: 01

## Question

Design the pre-PR agentic-review step. It houses two agent-invoked review skills, both run AFK, neither fixes in the run — each writes findings as `ready-for-human` tracker issues to grill/refine/drop later:

- `improve-codebase-architecture` — architecture findings.
- `/code-review` (relocated here from `/implement`, per ticket 05) — Standards + Spec review of the changes since the base branch, against the issue. It is an agent-invoked skill by design; in the VM run there is no live human, so its findings are written to the tracker rather than discussed.

Decide:
- The common finding format both skills emit.
- How they reuse vs diverge from the existing interactive HTML-report skill (which is HITL by design).

Produces the agentic-review-step spec.

## Resolution (closed)

A new primary agent `agentic-review` (peer of feature-builder / static-analysis / test-runner / pr-author in `.opencode/agents/`, run one-shot by the bash orchestrator, before pr-author). It is the **HITL-scanner mold** — the inverse of static-analysis (06): it *never* fixes, *never* loops, *never* branches the pipeline. Its only side effect is `ready-for-human` tracker issues.

### Agent shape (locked)

- **Single-pass**, run once. No ≤3× loop (Q2).
- **Read-only on the repo** — no `edit`, no autofix, no commit. Differs from static-analysis, matches test-runner. Gets tracker *write* via the PAT at `~/.github-pat.txt` (Q2).
- **Always exits 0** — findings are advisory by definition and never block the PR. Non-zero is reserved for the agent *itself* failing (PAT missing, tracker unreachable) (Q3).
- **No sentinel, no `findings.json`, no feature-builder hand-off** — nothing downstream branches on it; the pipeline proceeds straight to pr-author (Q2, Q3).

### The two skills, run headless

Both are HITL-by-design; agentic-review runs only their *analysis* halves and redirects their *presentation* halves to the tracker.

- **`/code-review`** — SPEC-block bindings replace its two interactive prompts: **fixed point = `BASE`** (diff `BASE...HEAD`), **spec source = `ISSUE`**. Its "ask the user" branches (steps 1–2) never fire. Standards + Spec sub-agents run as designed (Q4).
- **`improve-codebase-architecture`** — run **step 1 (Explore) only**. Steps 2 (HTML report) and 3 (grilling loop) are dropped; the issue body carries each candidate's card fields (Files / Problem / Solution / Benefits / Recommendation strength). The deferred grilling happens later, when a human picks the issue off the tracker (Q5).

### The sink — one issue per skill (Q1=B)

Per run, **one `ready-for-human` issue per skill** — a code-review issue and an architecture issue, kept separate because they are separate grilling conversations (code-review itself insists on the two-axis separation). Not per-finding (floods the tracker), not one roll-up (forces grilling both together).

- **Empty run → no issue** for that skill. Silence = clean; the VM run log is the audit trail (Q8).

### Common finding format = shared envelope, native bodies (Q6)

The two skills produce structurally different findings (architecture cards vs rule-citation/spec-line findings); forcing one schema strips the detail a human needs to grill. "Common format" is a shared **issue envelope**, not a flat finding row:

- **Title**: `[agentic-review] <skill> findings — <branch>`
- **Labels**: `ready-for-human` (routing gate) + `agentic-review` (provenance/queryable marker — a label, not an HTML comment, so the human can filter the tracker UI) (Q7).
- **Body**: header block (`Branch` / `Base` / `Issue #` / `Generated <timestamp>`) then the skill-native findings verbatim.
- New label `agentic-review` joins the tracker vocabulary.

### Deltas from ticket 01 / 06

- agentic-review is read-only (unlike static-analysis, which commits autofixes).
- No fix agent, no loop, no sentinel — the whole AFK half of 06 is absent by design.
- Security (deferred, ticket 01/Q4) reuses *this* mold: clone `agentic-review.md`, swap the two skills for the security scanner. That is the "HITL-scanner mold" ticket 01 named.
