#!/usr/bin/env bash
# End-to-end feature-builder test for the AI Software Factory job VM.
# Sets up the GitHub PAT, opencode API key, clones the project
# repo, injects the .opencode agent configuration, then runs the opencode
# phases headlessly in the same clone: implement (feature-builder agent:
# issue token in, implemented branch pushed), test (test-runner agent),
# quality loop (<=3 iterations of static-analysis scans, each followed by a
# feature-builder fix-findings pass while AFK findings remain - never a red
# gate), test again (autofixes and fix refactors can break behaviour),
# agentic-review (review findings filed as ready-for-human tracker issues)
# and - after a gate plus a hook point - PR (pr-author agent: PR authored
# from the branch diff and POSTed).
# The calling PowerShell harness verifies the resulting branch/PR.
set -euo pipefail

BRANCH="$1"
ISSUE="$(printf '%s' "${ISSUE_B64:?ISSUE_B64 env var must be set by the host launcher}" | base64 -d)"
REPO="$2"                       # owner/name, e.g. limered/autodev

PAT_TMP="/tmp/github-pat.txt"
PAT_FILE="$HOME/.github-pat.txt"
API_KEY_TMP="/tmp/opencode-api-key.txt"
WORK_DIR="$HOME/${REPO##*/}"    # clone dir = repo name
OPENCODE_DIR="/tmp/.opencode"
# deepseek-v4-flash needs region opt-in; grok-4.5 works over the API today.
MODEL="${MODEL:-opencode-go/grok-4.5}"

fail() {
  echo "FAIL: $1" >&2
  exit 1
}

pass() {
  echo "PASS: $1"
}

echo "== Feature builder end-to-end test =="

# 3. PAT injected by the host must be readable and copied to ~/.github-pat.txt.
[[ -f "$PAT_TMP" ]] || fail "PAT file not found at $PAT_TMP"
PAT=$(tr -d '\n\r ' < "$PAT_TMP")
[[ -n "$PAT" ]] || fail "PAT is empty"
cp "$PAT_TMP" "$PAT_FILE"
chmod 600 "$PAT_FILE"
pass "GitHub PAT copied to ~/.github-pat.txt"

# 4. opencode API key injected by the host must be exported as OPENCODE_API_KEY.
[[ -f "$API_KEY_TMP" ]] || fail "opencode API key file not found at $API_KEY_TMP"
OPENCODE_API_KEY=$(tr -d '\n\r ' < "$API_KEY_TMP")
[[ -n "$OPENCODE_API_KEY" ]] || fail "opencode API key is empty"
export OPENCODE_API_KEY
pass "OPENCODE_API_KEY configured"

# 5. Git needs an identity to commit.
git config --global user.email "bot@ai-software-factory.local"
git config --global user.name "AI Software Factory Bot"
pass "Git identity configured"

# 6. Shallow clone over HTTPS using the bot PAT. The token is embedded in the
#    remote URL so both the clone and later `git push` authenticate without SSH.
rm -rf "$WORK_DIR"
REPO_HTTPS="https://x-access-token:${PAT}@github.com/${REPO}.git"
git clone --depth 1 "$REPO_HTTPS" "$WORK_DIR"
pass "Shallow-cloned https://github.com/${REPO}.git"

# 7. Copy the .opencode configuration into the cloned project repo.
[[ -d "$OPENCODE_DIR" ]] || fail ".opencode directory not found at $OPENCODE_DIR"
cp -r "$OPENCODE_DIR" "$WORK_DIR/"
pass "Copied .opencode into cloned repo"

# 8. Find the opencode binary.
OPENCODE_BIN=$(command -v opencode || true)
if [[ -z "$OPENCODE_BIN" ]]; then
  for candidate in /usr/local/bin/opencode /usr/bin/opencode; do
    [[ -x "$candidate" ]] && OPENCODE_BIN="$candidate" && break
  done
fi
[[ -n "$OPENCODE_BIN" ]] || fail "opencode binary not found in PATH"
pass "opencode binary: $OPENCODE_BIN"

# 9. Phase 1 - implement: run the feature-builder agent headlessly against the
#    issue token. It implements, commits, and pushes the branch. It does NOT
#    create the PR; that is the PR phase's job.
cd "$WORK_DIR"
BASE="${BASE:-main}"
BASE_REF="origin/$BASE"
IMPL_SPEC="ISSUE: $ISSUE
BRANCH: $BRANCH
BASE: $BASE
REPO: $REPO"

TICKER_PID=""
stop_ticker() {
  if [[ -n "$TICKER_PID" ]]; then
    kill "$TICKER_PID" 2>/dev/null || true
    TICKER_PID=""
  fi
}
trap stop_ticker EXIT

# Runs one agent phase headlessly and returns the opencode exit code.
# stdin from /dev/null so opencode never blocks waiting on a TTY.
# /tmp/heartbeat is the liveness marker contract consumed by the host-side poller.
# /tmp/current-phase is the phase marker: each phase writes its agent name here
# as it begins, so the host heartbeat poller can relay the current phase up to
# the backend without any new secrets crossing into the VM.
#
# Per-step instrumentation (dev-loop detail): wall time is measured in-VM around
# the opencode call and the `--format json` NDJSON is redirected to
# /tmp/phase-<agent>-<iteration>.jsonl, so the host can sum per-phase token
# usage from `step_finish` events and relay duration + tokens as a
# phase-finished event. A small meta sidecar
# (/tmp/phase-<agent>-<iteration>.meta.json) carries durationMs + status for the
# same relay. Iteration is 0 for single-run phases; the quality loop passes its
# loop index, so each scan/fix pass gets its own file pair. The model is never
# written here: the host attaches it from the agent frontmatter at relay time,
# so no secret crosses into the VM.
#
# Liveness = "the opencode process is alive", not "it printed a line this minute".
# A long silent model turn (final commit/PR generation) emits no lines for minutes
# and was false-killing healthy runs. So: a ticker touches the marker every 30s
# while opencode's pid is alive. The ADR's real failure (a dead model that errors
# every request) makes opencode exit fast -> pid gone -> ticker stops -> marker
# goes stale as intended.
# ponytail: process-liveness, not output-progress. A deadlocked-but-alive opencode
# would keep the marker fresh forever; ADR 002 scopes the failure to a dead model
# that *exits*, and VM-level hangs out of scope, so this is the right signal today.
run_agent_phase() {
  local agent="$1"
  local prompt="$2"
  local iteration="${3:-0}"
  local jsonl="/tmp/phase-${agent}-${iteration}.jsonl"
  local meta="/tmp/phase-${agent}-${iteration}.meta.json"
  # Write the phase marker before touching the heartbeat so the host reads a
  # consistent (phase, heartbeat-mtime) pair when it observes the mtime advance.
  printf '%s' "$agent" > /tmp/current-phase
  touch /tmp/heartbeat
  # Truncate first so a retry never mixes two runs; a tail follows the file so
  # the NDJSON stays visible live on the console while landing verbatim on disk.
  # stdout (the NDJSON stream) lands in the file; stderr stays on the console.
  : > "$jsonl"
  tail -n +1 -F "$jsonl" 2>/dev/null &
  local tail_pid=$!
  # No --model: each agent resolves its own model from the unpacked opencode
  # config (~/.config/opencode), so feature-builder, test-runner,
  # static-analysis, agentic-review and pr-author can differ. $MODEL is
  # reporting-only.
  local start_ms end_ms
  start_ms=$(date +%s%3N)
  $OPENCODE_BIN run --agent "$agent" --auto --format json "$prompt" </dev/null >>"$jsonl" &
  local phase_pid=$!
  ( while kill -0 "$phase_pid" 2>/dev/null; do sleep 30; touch /tmp/heartbeat 2>/dev/null; done ) &
  TICKER_PID=$!
  local rc=0
  wait "$phase_pid" || rc=$?
  end_ms=$(date +%s%3N)
  stop_ticker
  kill "$tail_pid" 2>/dev/null || true
  wait "$tail_pid" 2>/dev/null || true
  local status="done"
  [[ "$rc" -eq 0 ]] || status="failed"
  printf '{"agent":"%s","iteration":%d,"durationMs":%d,"status":"%s"}\n' \
    "$agent" "$iteration" "$((end_ms - start_ms))" "$status" > "$meta"
  return "$rc"
}

# Quality loop (phase 3): up to 3 iterations of static-analysis scan ->
# feature-builder fix-findings pass. The control signal is the `status`
# sentinel the static-analysis agent appends to .factory/static-analysis-result.json;
# the agent's exit code means only that the agent itself broke -> hard fail().
# Never fail() on findings: `hitl-only` (everything left is for a human) and
# cap exhaustion both proceed to the next phase. Reads the SPECs defined at
# the phase 3 call site below ($QUALITY_SPEC for the scan, $FIX_SPEC for the
# fix pass).
run_quality_loop() {
  for i in 1 2 3; do
    run_agent_phase static-analysis "$QUALITY_SPEC" "$i" || fail "static-analysis agent crashed"
    status=$(tail -n1 .factory/static-analysis-result.json | jq -r .status) || fail "static-analysis agent crashed: cannot read status sentinel from .factory/static-analysis-result.json"
    case "$status" in
      clean|hitl-only) return 0 ;;                      # nothing left to auto-fix
      fixed)  run_agent_phase feature-builder "$FIX_SPEC" "$i" || fail "fix pass crashed" ;;
    esac
  done
  # ponytail: 3x is a backstop, not the real exit. Ticket 06 self-escalation
  # converts a finding that survives one fix pass to HITL (already on tracker),
  # so still-AFK at iteration 3 is a genuinely churning finding, vanishingly
  # rare. Proceed rather than fail(); the human reviewing the PR is the final
  # backstop.
  return 0
}

echo "Running phase 1/6 (implement): OPENCODE_API_KEY=*** $OPENCODE_BIN run --agent feature-builder --auto --format json \"...\""
if ! run_agent_phase feature-builder "$IMPL_SPEC" 0; then
  fail "implement phase failed: opencode run exited non-zero (see log above)"
fi
pass "implement phase completed (feature-builder exited 0)"

# 10. Gate between phases: the PR phase runs only if the implement agent exited
#     cleanly AND the branch has commits ahead of base. A clean-but-empty
#     implement (exit 0, nothing committed) stops here, before the PR phase,
#     and is recorded as a failed run - fail() exits 1, which fails the
#     multipass exec on the host, which marks the run failed (run-failed
#     event, freeze snapshot, VM teardown).
if ! AHEAD_COUNT=$(git rev-list --count "$BASE_REF..HEAD" 2>/dev/null); then
  fail "gate: cannot count commits ahead of $BASE_REF - is the base ref present in the clone?"
fi
if [[ "$AHEAD_COUNT" -eq 0 ]]; then
  fail "clean-but-empty implement: HEAD has no commits ahead of $BASE_REF; stopping before the PR phase"
fi
pass "gate passed: HEAD is $AHEAD_COUNT commit(s) ahead of $BASE_REF"

# 11. -----------------------------------------------------------------------
#     Phase 2 - test: run the test-runner agent in the same clone.
#
#     The test-runner reads the target repo's AGENTS.md for
#     `test-harness.<name>: <command>` entries and runs every one. If no
#     harness is declared, or any harness exits non-zero, the run fails here:
#     the branch stays pushed, but the quality loop below does not run. This
#     is the first of two test phases; phase 4 re-runs every harness after
#     the quality loop's autofix/fix commits.
#     -----------------------------------------------------------------------
TEST_SPEC="BRANCH: $BRANCH
BASE: $BASE
REPO: $REPO"

echo "Running phase 2/6 (test): OPENCODE_API_KEY=*** $OPENCODE_BIN run --agent test-runner --auto --format json \"...\""
if ! run_agent_phase test-runner "$TEST_SPEC" 0; then
  fail "test phase failed: a declared harness is red or no harness was declared (see log above)"
fi
pass "test phase completed (test-runner exited 0, every harness green)"

# 12. -----------------------------------------------------------------------
#     Phase 3 - quality loop: up to 3 iterations of static-analysis ->
#     feature-builder (fix-findings mode) in the same clone.
#
#     Each static-analysis run discovers the repo's own analysers, applies
#     tool autofixes as a `chore: apply tool autofixes` checkpoint commit,
#     re-scans, classifies residual faults afk/hitl (self-escalating any
#     finding that survived an earlier fix pass to hitl), files the HITL
#     roll-up issue, and appends the `status` sentinel the loop reads:
#     `clean`/`hitl-only` -> nothing left to auto-fix, loop stops;
#     `fixed` -> feature-builder applies the afk fixes, then re-scan.
#     A completed scan is always a success - findings are data, not a red
#     gate - so fail() fires only on an agent crash. Cap exhaustion (still
#     `fixed` after 3 iterations) proceeds like `hitl-only` does: never
#     block the PR on findings; the human reviewing it is the backstop.
#     .factory/ scratch (findings.json, static-analysis-result.json) is
#     gitignored, so it never enters autofix commits or the PR diff.
#     -----------------------------------------------------------------------
QUALITY_SPEC="BRANCH: $BRANCH
BASE: $BASE
REPO: $REPO"

FIX_SPEC="MODE: fix-findings
FINDINGS: .factory/findings.json
BRANCH: $BRANCH
BASE: $BASE
REPO: $REPO"

echo "Running phase 3/6 (quality-loop): OPENCODE_API_KEY=*** $OPENCODE_BIN run --agent static-analysis --auto --format json \"...\" (then --agent feature-builder in fix-findings mode while status is fixed)"
run_quality_loop
pass "quality loop completed (status clean/hitl-only, or 3-iteration cap exhausted - never a red gate)"

# 13. -----------------------------------------------------------------------
#     Phase 4 - test (re-run): run the test-runner agent again in the same
#     clone, after the quality loop. The loop's autofix checkpoint commits
#     and fix-findings refactors can change behaviour, so every declared
#     harness must be green again before review and PR. Same failure
#     semantics as phase 2: a red harness fails the run here. Iteration 1
#     (phase 2 used 0): the backend keys steps on (agent, iteration), so the
#     re-run lands as its own row instead of overwriting the phase-2 step.
#     -----------------------------------------------------------------------
echo "Running phase 4/6 (test re-run): OPENCODE_API_KEY=*** $OPENCODE_BIN run --agent test-runner --auto --format json \"...\""
if ! run_agent_phase test-runner "$TEST_SPEC" 1; then
  fail "test re-run phase failed: a declared harness is red after the quality loop (see log above)"
fi
pass "test re-run phase completed (test-runner exited 0, every harness still green)"

# 14. Phase 5 - agentic-review: run the agentic-review agent in the same clone,
#     after the quality loop (and its test re-run) and before the PR phase. It
#     runs the two review skills headless (/code-review on the BASE...HEAD diff
#     against the issue, improve-codebase-architecture explore-only) and files
#     each skill's findings as one ready-for-human tracker issue. It never
#     fixes and never fails the run for findings - a non-zero exit here means
#     an operational failure (PAT missing, tracker unreachable), not review
#     findings.
AR_SPEC="ISSUE: $ISSUE
BRANCH: $BRANCH
BASE: $BASE
REPO: $REPO"

echo "Running phase 5/6 (agentic-review): OPENCODE_API_KEY=*** $OPENCODE_BIN run --agent agentic-review --auto --format json \"...\""
if ! run_agent_phase agentic-review "$AR_SPEC" 0; then
  fail "agentic-review phase failed: opencode run exited non-zero (operational failure, see log above)"
fi
pass "agentic-review phase completed (agentic-review exited 0)"

# 15. Phase 6 - PR: run the pr-author agent as a distinct opencode run in the
#     same clone. It authors the PR title and body from the branch diff and
#     POSTs the pull request.
PR_SPEC="BRANCH: $BRANCH
BASE: $BASE
REPO: $REPO"

echo "Running phase 6/6 (PR): OPENCODE_API_KEY=*** $OPENCODE_BIN run --agent pr-author --auto --format json \"...\""
if ! run_agent_phase pr-author "$PR_SPEC" 0; then
  fail "pr phase failed: opencode run exited non-zero (see log above)"
fi
pass "pr phase completed (pr-author exited 0, PR created)"
