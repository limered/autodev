# Map: Decompose the factory agent into single-responsibility steps

Labels: `wayfinder:map`

## Destination

The monolithic factory agent is broken into **single-responsibility steps/agents** that the job orchestration sequences, each independently optimisable. The proving ground for the decomposition is the quality + architecture pipeline:

```
implement issue (tdd: write test, run that one test/file)
  → test-runner (full tests)
  → loop ≤3×: [ quality scan (static analysis) → fix AFK findings ]
      stop when: 3 iterations, OR no findings, OR all remaining findings are HITL
  → architecture scan → write HITL findings to tracker (no fix)
  → create PR
```

Reached when: the factory flow is composed of named steps with concrete, separate responsibilities; quality findings are classified per-finding AFK vs HITL and AFK ones self-fix in the same VM run; architecture findings land in the tracker for later grilling. Config lives in `AGENTS.md`.

## Notes

- **Domain**: opencode agent/skill setup for the `autodev` factory. Steps are skills/agents; orchestration sequences them in a VM job (`start-job.ps1`, `start-issue` skill).
- **Tracker**: local markdown (`.scratch/<slug>/issues/`). AFK/HITL == existing triage vocabulary `ready-for-agent` / `ready-for-human`. Do not invent a second vocabulary.
- **Reuse, don't rebuild**: `implement`, `tdd`, `test-runner` (exists), `code-review`, `improve-codebase-architecture`, `triage`, `atomic-commit`. New construction only where a gap is proven.
- **Skills every session should consult**: `/grilling`, `/domain-modeling`, `/codebase-design` (for the extraction/deepening work).
- **Standing preference**: laziest thing that works. A step that duplicates an existing skill should not be born.
- **Generic**: quality scan runs the *repo's own* configured analysers (discover, don't hardcode ESLint/Vue/dotnet).
- Many "needs testing" answers here are **prototype/research tickets** — they resolve a threshold or a safe/unsafe boundary that can't be reasoned into existence.

## Decisions so far

- [Decompose, don't extend — decomposition IS the destination](#) — the monolith becomes composable single-responsibility steps; quality/architecture are the first proving ground, not the whole point.
- [Architecture stays HITL, files tickets pre-PR](#) — never auto-fixes in the run; produces tracker issues to grill/refine/drop and restart manually.
- [Quality is objective + AFK](#) — runs static-analysis tools against thresholds; a fault vs a metric is machine-fixable and loops ≤3×.
- [Per-finding AFK/HITL classification](#) — not by source skill; the existing HITL definition (`ready-for-agent`/`ready-for-human`) is the gate.
- [No separate fix-issues agent](#) — `implement` consumes AFK findings directly; fixing is not a new agent.
- [02 — Static-analysis survey](issues/02-static-analysis-survey.md) — repo may have nothing configured; discover the repo's own analysers (scripts > config > dep > SDK), degrade gracefully. AFK = safe-autofix tools; HITL = advisory. SARIF as common format. ([findings](issues/02-findings.md))
- [03 — Complexity thresholds](issues/03-complexity-thresholds.md) — leave-it ≤30 CRAP/≤10 comp, fix-AFK ≤60/≤15, HITL above; never autofix under 0.5 coverage; in-class-vs-cross-class by dry-run patch shape. ([findings](issues/03-findings.md))

## Not yet specified

<!-- fog: in-scope, not yet sharp enough to ticket -->

- How the orchestration loop is expressed (where the ≤3× loop and the AFK/HITL branch physically live in `start-job.ps1` / job flow) — graduates once the step boundaries are settled.
- The `AGENTS.md` config schema for per-step thresholds and tool selection — graduates once we know which thresholds exist to configure.
- Post-implementation "improve" pass and verify-time instant-fix — likely just the first iteration of the quality loop; confirm it's not built twice once the loop shape is settled.
- Additional quality check families beyond lint + complexity (security scanning, dependency updates) — each is its own tool-discovery + AFK/HITL-threshold ticket, graduates once the quality-step contract exists.

## Out of scope

- **Web-UI per-repo agent configuration** — a separate surface (dashboard web + API + persistence). `AGENTS.md` is the testing ground; the UI graduates to a fresh effort once real thresholds exist to configure.
