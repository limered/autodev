# 06 — Design the static-analysis step

Labels: `wayfinder:grilling`

Blocked by: 01, 02, 03, 04

## Question

With the contract, tool survey, thresholds, and safe-AFK rule in hand, design the static-analysis step:

- How it discovers and runs the repo's analysers, parses faults, and classifies each per-finding AFK vs HITL.
- How AFK findings flow into implementation (feature-builder) for fixing within the run; how HITL findings become `ready-for-human` tracker issues.
- Its `AGENTS.md` config surface (tools, thresholds).

Produces the static-analysis-step spec.

## Resolution (closed)

A new primary agent `static-analysis` (peer of feature-builder / test-runner / pr-author in `.opencode/agents/`, run one-shot by the bash orchestrator). **Fix and scan are separated (Q1=B)** so combinations are swappable later: the static-analysis agent *runs tool-autofix with its own hands*, but every code change a tool can't make itself becomes a **finding** — AFK findings are handed to the **feature-builder** agent to fix; HITL findings are written to the tracker. The agent never reasons about refactors; it runs `--fix` and reports the residue.

### Responsibilities of the static-analysis agent

1. **Discover** the repo's own analysers (ticket 02 heuristics: declared script > committed config > installed dep > SDK built-in). Emit a `detected / not_detected` manifest. Absence is the common case — run what's declared, report nothing if nothing is declared, never hardcode tools.
2. **Autofix in place**: run each tool's `--fix` (`eslint --fix`, `stylelint --fix`, `dotnet format`). This is the agent's own hands, not a finding. **Commit the autofixes** (`chore: apply tool autofixes`) so the state is a rollback checkpoint if the VM dies (Q7). The agent therefore gets `edit` + `bash` permission — it is NOT read-only like test-runner.
3. **Re-scan (no fix)** and collect **residual faults** — everything `--fix` couldn't clear (non-autofixable lint rules, tsc/Roslyn diagnostics, npm-audit vulns, complexity faults).
4. **Classify each residual** into the findings schema: `route: afk` (feature-builder can fix) vs `route: hitl` (advisory / unsafe — per tickets 02/03/04). Two `family` axes carried: `lint` | `complexity` | `typecheck` | `security`.
5. **Emit** `.factory/findings.json` (AFK findings, transient) and write the **HITL subset as one roll-up `ready-for-human` GitHub issue** per run (Q3) via the PAT at `~/.github-pat.txt`.
6. **Write the sentinel** `.factory/static-analysis-result.json` (`{ afk_fixed, hitl_remaining, status: fixed|clean|hitl-only }`) for the bash orchestrator (ticket 08 consumes it).

### Loop body (locked — orchestration detail lands in ticket 08)

```
loop ≤3×:
  static-analysis agent:
     discover tools → run each with autofix → commit autofixes
     re-scan (no fix) → collect residual faults
     classify: afk-route → findings.json ; hitl-route → tracker roll-up issue
     write sentinel
  if AFK findings exist:
     feature-builder agent → consumes findings.json, applies fixes, commits
     continue        # rescan: a fix may reintroduce lint faults
  else:
     break           # clean, or only HITL remains
```

- **Scan and fix are different agents** (Q1=B). static-analysis emits findings; feature-builder gains a "fix these findings" mode alongside its existing "implement this issue" mode.
- **One scan agent, two finding families** (Q2) — the split is in the payload (`family`), not two agents; discovery runs once per iteration.
- **≥1 re-run is inherent**: fixing can reintroduce lint, so the loop rescans after every fix pass.

### Convergence — escalate on reappearance (Q8)

Findings carry a **stable `id`** (`tool:rule:file:line`). When an `id` survives a feature-builder fix attempt (reappears in the next iteration's scan), static-analysis **re-routes it `hitl`** with note "AFK fix attempted, did not resolve" — a fix already tried and failed is, by evidence, not safely automatable. This makes the loop self-correcting; ticket 08's ≤3× cap is a backstop, not the primary exit.

### Findings schema — `.factory/findings.json` (Q6)

```json
{
  "run": { "branch": "...", "base": "main", "iteration": 1 },
  "manifest": { "detected": ["eslint","tsc","dotnet-format"], "not_detected": ["stylelint"] },
  "findings": [
    {
      "id": "eslint:no-unused-vars:src/foo.ts:42",
      "tool": "eslint",
      "rule": "no-unused-vars",
      "family": "lint",            // lint | complexity | typecheck | security
      "route": "afk",              // afk | hitl
      "file": "src/foo.ts",
      "line": 42,
      "message": "…",
      "fix": {                     // present only when route=afk
        "kind": "tool-autofix",    // tool-autofix | refactor
        "instruction": "…"         // command or refactor directive feature-builder enacts
      }
    }
  ]
}
```

- **`route` is decided by the scan agent, not the fixer** — the AFK/HITL classification (03/04) lives here; feature-builder blindly applies `route=afk`, keeping the fixer dumb and swappable.
- **`fix.instruction` is imperative** — a command for lint-family, a refactor directive for complexity-family. This is *how* the separation works: the finding carries enough for a generic fixer to act.
- **`manifest` rides along** so the run is auditable (ticket 02) and reporting can surface "nothing configured".
- **SARIF is the ingest format** (02); `findings.json` is the normalised internal shape — feature-builder never parses raw per-tool SARIF.

### AGENTS.md config surface

The `quality:` block from ticket 03 (§3) — complexity bands + AFK gates — plus per-repo tool overrides. Every value has a documented default so the block is optional. Full schema graduates with ticket 08 (the AGENTS.md schema is still fog until the loop wiring is settled).

### Deltas from ticket 01

- static-analysis is **not** read-only (it commits autofixes) — differs from test-runner.
- feature-builder gains a second invocation mode: fix `.factory/findings.json` findings.
- Fixing lives in feature-builder (Q1=B), *not* in the scan agent — separated for reusable combinations, overriding ticket 01's abstract "flow into implementation" into a concrete two-agent hand-off.
