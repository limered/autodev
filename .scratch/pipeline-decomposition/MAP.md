# Map: Decompose the factory agent into single-responsibility steps

Labels: `wayfinder:map`

## Destination

The monolithic factory agent is broken into **single-responsibility steps/agents** that the job orchestration sequences, each independently optimisable. The proving ground for the decomposition is the static-analysis + agentic-review pipeline:

```
implement issue (tdd: write test, run that one test/file)
  → test-runner (full tests)
  → loop ≤3×: [ static-analysis scan → fix AFK findings ]
      stop when: 3 iterations, OR no findings, OR all remaining findings are HITL
  → agentic-review scan → write HITL findings to tracker (no fix)
  → create PR
```

Reached when: the factory flow is composed of named steps with concrete, separate responsibilities; static-analysis findings are classified per-finding AFK vs HITL and AFK ones self-fix in the same VM run; agentic-review findings land in the tracker for later grilling. Config lives in `AGENTS.md`.

## Notes

- **Domain**: opencode agent/skill setup for the `autodev` factory. Steps are skills/agents; orchestration sequences them in a VM job (`start-job.ps1`, `start-issue` skill).
- **Tracker**: local markdown (`.scratch/<slug>/issues/`). AFK/HITL == existing triage vocabulary `ready-for-agent` / `ready-for-human`. Do not invent a second vocabulary.
- **Reuse, don't rebuild**: `implement`, `tdd`, `test-runner` (exists), `code-review`, `improve-codebase-architecture`, `triage`, `atomic-commit`. New construction only where a gap is proven.
- **Skills every session should consult**: `/grilling`, `/domain-modeling`, `/codebase-design` (for the extraction/deepening work).
- **Standing preference**: laziest thing that works. A step that duplicates an existing skill should not be born.
- **Generic**: static-analysis scan runs the *repo's own* configured analysers (discover, don't hardcode ESLint/Vue/dotnet).
- Many "needs testing" answers here are **prototype/research tickets** — they resolve a threshold or a safe/unsafe boundary that can't be reasoned into existence.

## Decisions so far

- [Decompose, don't extend — decomposition IS the destination](#) — the monolith becomes composable single-responsibility steps; quality/architecture are the first proving ground, not the whole point.
- [Architecture stays HITL, files tickets pre-PR](#) — never auto-fixes in the run; produces tracker issues to grill/refine/drop and restart manually.
- [Quality is objective + AFK](#) — runs static-analysis tools against thresholds; a fault vs a metric is machine-fixable and loops ≤3×.
- [Per-finding AFK/HITL classification](#) — not by source skill; the existing HITL definition (`ready-for-agent`/`ready-for-human`) is the gate.
- [No separate fix-issues agent](#) — `implement` consumes AFK findings directly; fixing is not a new agent.
- [02 — Static-analysis survey](issues/02-static-analysis-survey.md) — repo may have nothing configured; discover the repo's own analysers (scripts > config > dep > SDK), degrade gracefully. AFK = safe-autofix tools; HITL = advisory. SARIF as common format. ([findings](issues/02-findings.md))
- [03 — Complexity thresholds](issues/03-complexity-thresholds.md) — leave-it ≤30 CRAP/≤10 comp, fix-AFK ≤60/≤15, HITL above; never autofix under 0.5 coverage; in-class-vs-cross-class by dry-run patch shape. ([findings](issues/03-findings.md))
- [01 — Step contract](issues/01-step-contract.md) — a step = a primary opencode agent, one-shot, shared clone, commits+exit-code downstream; AFK→commits, HITL→tracker issues via PAT (Q1a); ≤3× loop owned by bash orchestrator (Q2a); implement shrinks to impl+test-run, /code-review moves into quality agent (Q3); two new agents (quality, architecture), security deferred (Q4).
- [04 — Safe-AFK refactorings](issues/04-safe-afk-refactorings.md) — AFK/HITL/CONDITIONAL table ([findings](issues/04-findings.md)); declared kind = fast filter, patch-shape gate = ground truth; unclassifiable→HITL, public rename→HITL for v1, table lives as a doc the quality agent reads.
- [05 — Extract the implement / test-runner boundary](issues/05-extract-implement-testrunner.md) — inline `/implement` into feature-builder step 2 (drop the code-review branch + full-test line); the `/implement` skill and test-runner stay untouched. `/code-review` relocates to the agentic-review step (07), running AFK and filing `ready-for-human` findings. **Renames**: quality step → static-analysis step (06); architecture step → agentic-review step (07, covering architecture + `/code-review`).
- [06 — Static-analysis step](issues/06-static-analysis-step.md) — new `static-analysis` agent: **scan and fix separated** (Q1=B) — it runs tool-autofix with its own hands + commits (rollback checkpoint), but emits residual faults as `.factory/findings.json`; AFK findings → feature-builder fixes, HITL → one roll-up `ready-for-human` issue/run. Loop: scan→commit-autofix→rescan→hand AFK to feature-builder→rescan (≥1×). Findings carry a stable `id`; a finding that survives a fix pass **escalates to HITL** (Q8). Full findings schema + convergence rule locked; AGENTS.md `quality:` schema graduates with 08.
- [07 — Agentic-review step](issues/07-agentic-review-step.md) — new read-only `agentic-review` agent, the **HITL-scanner mold**: single-pass, always exit 0, no fix/loop/sentinel. Runs `/code-review` (`BASE`/`ISSUE` pre-bound from SPEC block) + `improve-codebase-architecture` step-1-only (no HTML, no grilling). Emits **one `ready-for-human` issue per skill** (Q1=B; separate grilling conversations), native bodies under a shared envelope (title `[agentic-review] <skill> — <branch>`, new `agentic-review` marker label). Empty run → no issue. Security (01/Q4) reuses this mold verbatim.

- [08 — Orchestration loop](issues/08-orchestration-loop.md) — phases wire into `test-feature-builder.sh` as `implement → test → run_quality_loop() → test → agentic-review → PR`. **Sentinel-driven control** (`.factory/static-analysis-result.json` `status`), exit code = agent-health only (Q2). ≤3× loop in a bash helper; fix pass = same feature-builder via `MODE: fix-findings` SPEC line (Q3). **Never blocks the PR** — findings are advisory/on-tracker (Q4); test-runner re-runs after the loop (Q5); 3× is a backstop leaning on 06's self-escalation + human PR review (Q6). `.factory/` = gitignored appended JSON-lines, read `tail -n1` (Q7/Q7a). All config hardcoded for v1 (Q8).

## Not yet specified

<!-- fog: in-scope, not yet sharp enough to ticket -->

- Additional quality check families beyond lint + complexity (security scanning, dependency updates) — each is its own tool-discovery + AFK/HITL-threshold ticket, graduates once the static-analysis-step contract exists. Security specifically is now scoped (ticket 01/Q4) as HITL-advisory; the HITL-scanner mold it needs now exists (ticket 07) — implementation is `clone agentic-review.md, swap the two skills for the security scanner`. Graduates to a ticket after v1 proves the machine.

## Out of scope

- **Web-UI per-repo agent configuration** — a separate surface (dashboard web + API + persistence). `AGENTS.md` is the testing ground; the UI graduates to a fresh effort once real thresholds exist to configure.
