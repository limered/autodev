# Dev-loop tokens: where per-phase usage lives (#134, map #133)

## 1. How phases run today
`infrastructure/multipass/test-feature-builder.sh` `run_agent_phase()` (L117-136):
`/tmp/current-phase` <- agent name, then `opencode run --agent <a> --auto
--print-logs "<prompt>" </dev/null` (+30s heartbeat ticker while pid alive).
6 runs: feature-builder, test-runner, quality-loop (static-analysis [+fix
passes], sentinel `.factory/static-analysis-result.json`), test re-run,
agentic-review, pr-author. No `--format json`, no `--model` (per-agent model
from unpacked config). `--print-logs` -> stderr; stdout = formatted text.

## 2. Token output per run
Default format prints NO token/cost line (opencode #3307). Structured channel:
`opencode run --format json` -> stdout NDJSON; usage lives there:
`opencode run "fix the failing test" --format json | jq 'select(.type=="step_finish") | .part'`
`step_finish` = cost + token usage; per-turn input/output counts also on the
stream. Post-hoc: `session list -n1 --format json`, `export <sessionID>`,
`db "SELECT ..."`, `stats [--days --models --project]` (aggregate only).

## 3. Attribution (yes, possible)
Each `run` = new session (no `--continue/--session` passed), so sessions are
1:1 with phases; quality loop = up to 6 sessions, index by loop counter, and
name fix passes by iteration (agent alone is ambiguous). Method: tee
`--format json` to `/tmp/phase-<agent>-<i>.jsonl` per phase, sum `step_finish`
usage; fallback: newest session id right after `wait` returns, then `export`.
`stats` cannot attribute phases (shared clone dir) — reject. Tee, don't swap,
the stream (decision-extraction reads default stdout today).

## 4. Host path today + smallest emission
Host sees `/tmp/heartbeat` + `/tmp/current-phase` via `Get-HeartbeatSample`
(`lib/Heartbeat.ps1`), relayed as `heartbeat{currentPhase}` by
`watch-heartbeat.ps1` through `Send-FactoryEvent` (`factory-report.ps1`: POST
`/runs/{id}/events`, fire-and-forget, no-op w/o `.secrets/`). `RunEvent.cs` +
`RunEventJsonConverter` degrade unknown types to no-op, so a new
`phase-tokens{phase,inputTokens,outputTokens,cost}` event is backward-safe.
Smallest path: in-VM write `/tmp/phase-tokens.json` after each phase; host
`Get-VmPhaseTokens` (same multipass-cat seam) + relay; backend fold. v1 needs
final values only — pull before `Remove-Vm`, no streaming.

## 5. Open questions
Exact `step_finish`/`export` field names (verify on live VM, pin `opencode-ai`
npm version); `--format json` exit-code parity; session retention
(`OPENCODE_DISABLE_PRUNE`); cost field for non-OpenRouter providers.
