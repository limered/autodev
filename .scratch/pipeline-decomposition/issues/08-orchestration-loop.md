# 08 — Design the orchestration loop

Labels: `wayfinder:grilling`

Status: closed

Blocked by: 05, 06, 07

## Question

Sequence the steps in the job flow:

```
implement → test-runner → loop ≤3×[ static-analysis → fix] → test-runner → agentic-review → PR
```

- Where the ≤3× loop and the stop condition (3 iterations / no findings / all-HITL) live in `start-job.ps1` / job orchestration.
- How the AFK/HITL branch is expressed.
- **Loop-continue signal** (from ticket 01): bash owns the ≤3× loop, but a bare exit code is binary. Decide how the static-analysis agent tells bash "found+fixed AFK faults, re-run me" vs "done / all remaining HITL / clean" — e.g. a sentinel file in the clone or a distinct exit code.
- Confirm the post-implementation "improve" pass and verify-time instant-fix are just the loop's first iteration — not built twice.

Produces the orchestration spec.

## Resolution (closed)

The orchestrator (`infrastructure/multipass/test-feature-builder.sh`) is a linear bash script running phases via `run_agent_phase <agent> <spec>`, gated on exit code + a commits-ahead check. The quality loop and agentic-review slot into that phase list. Final phase sequence:

```
implement (feature-builder) → test (test-runner) → quality-loop → test (test-runner) → agentic-review → PR (pr-author)
```

### Where the loop lives (Q1)

A new `run_quality_loop()` bash helper beside `run_agent_phase`, keeping the main phase list flat and legible — the loop's messiness is encapsulated the same way `run_agent_phase` already hides ticker/heartbeat detail.

### Control signal (Q2)

**Sentinel drives control flow; exit code means only "the agent itself broke".** static-analysis emits `.factory/static-analysis-result.json` (from ticket 06). Bash `jq`-reads `status`; non-zero exit → hard `fail()` (crash / tools missing), never findings. One convention across all scan agents (matches agentic-review 07/Q3): **exit code = agent health, sentinel/tracker = findings**.

### Loop shape (Q6, with Q3 fix-invocation)

```bash
run_quality_loop() {
  for i in 1 2 3; do
    run_agent_phase static-analysis "$QUALITY_SPEC" || fail "static-analysis agent crashed"
    status=$(tail -n1 .factory/static-analysis-result.json | jq -r .status)
    case "$status" in
      clean|hitl-only) return 0 ;;                      # nothing left to auto-fix
      fixed)  run_agent_phase feature-builder "$FIX_SPEC" || fail "fix pass crashed" ;;
    esac
  done
  # ponytail: 3× is a backstop, not the real exit. Ticket 06's self-escalation
  # converts any finding that survives one fix pass to HITL (already on the
  # tracker), so a run still emitting AFK fixes at iteration 3 is a genuinely
  # churning finding — vanishingly rare. We proceed rather than fail(); the
  # human reviewing the PR is the final backstop.
  return 0
}
```

- **Fix invocation (Q3):** same `feature-builder` agent, a `MODE: fix-findings` discriminator + `FINDINGS:` pointer in the SPEC block — smallest thing that works, no fourth agent file, keeps the fixer dumb and swappable (06).
- **Cap exhaustion (Q6):** never `fail()`. Lean on 06's self-escalation-to-HITL plus the human PR review as the ultimate backstop.

### Never block the PR (Q4)

Quality findings are advisory — HITL findings are already on the tracker as `ready-for-human` (06), agentic-review is always-exit-0 (07). The only red exits stay: agent crash, and the existing empty-implement / red-test gates. `hitl-only` and cap-exhaustion both proceed to PR.

### Re-run tests after the loop (Q5)

The loop commits autofixes + AFK fixes, mutating code the phase-2 test-runner already greened. A second `run_agent_phase test-runner` runs after `run_quality_loop` — a bad `--fix` or complexity refactor can break behaviour. One line; matches the map's destination diagram.

### `.factory/` scratch lifecycle (Q7, Q7a)

- **Appended JSON-lines** (not overwrite): `static-analysis-result.json` and `findings.json` accumulate one block per iteration, preserving an in-clone audit trail.
- **Gitignored** (`.factory/` added to `.gitignore`) so autofix commits and the PR diff never carry scratch.
- Bash reads **current** state with `tail -n1 | jq`; the feature-builder-fix pass consumes the **latest** findings block, not the whole history.

### Config (Q8)

**All hardcoded for v1.** The ≤3× cap lives in bash, not `AGENTS.md`. No loop-config ticket graduates — the `quality:` threshold block (complexity bands, ticket 03) is static-analysis-agent-internal config, not loop orchestration. The "AGENTS.md loop config" fog is dropped as YAGNI.

### Deltas / what this leaves for execution

This is the orchestration **spec**, not the edit. Executing it touches `test-feature-builder.sh` (new helper + two phases + post-loop test), `.gitignore`, and the `MODE`/`SPEC` contract for feature-builder's fix mode. The script filename still says `test-feature-builder` — a rename to reflect the full pipeline is a cosmetic follow-up, not a decision.
