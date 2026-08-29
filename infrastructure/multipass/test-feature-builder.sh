#!/usr/bin/env bash
# End-to-end feature-builder test for the AI Software Factory job VM.
# Sets up the GitHub PAT, opencode API key, clones the project
# repo, injects the .opencode agent configuration, then runs two opencode
# phases headlessly in the same clone: implement (feature-builder agent:
# issue token in, implemented branch pushed) and - after a gate plus a hook
# point - PR (pr-author agent: PR authored from the branch diff and POSTed).
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
#    create the PR; that is phase 2's job.
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
  touch /tmp/heartbeat
  # No --model: each agent resolves its own model from the unpacked opencode
  # config (~/.config/opencode), so feature-builder, test-runner and pr-author
  # can differ. $MODEL is reporting-only.
  $OPENCODE_BIN run --agent "$agent" --auto --print-logs "$prompt" </dev/null &
  local phase_pid=$!
  ( while kill -0 "$phase_pid" 2>/dev/null; do sleep 30; touch /tmp/heartbeat 2>/dev/null; done ) &
  TICKER_PID=$!
  local rc=0
  wait "$phase_pid" || rc=$?
  stop_ticker
  return "$rc"
}

echo "Running phase 1/3 (implement): OPENCODE_API_KEY=*** $OPENCODE_BIN run --agent feature-builder --auto --print-logs \"...\""
if ! run_agent_phase feature-builder "$IMPL_SPEC"; then
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
#     the branch stays pushed, but the PR phase below does not run.
#     -----------------------------------------------------------------------
TEST_SPEC="BRANCH: $BRANCH
BASE: $BASE
REPO: $REPO"

echo "Running phase 2/3 (test): OPENCODE_API_KEY=*** $OPENCODE_BIN run --agent test-runner --auto --print-logs \"...\""
if ! run_agent_phase test-runner "$TEST_SPEC"; then
  fail "test phase failed: a declared harness is red or no harness was declared (see log above)"
fi
pass "test phase completed (test-runner exited 0, every harness green)"

# 12. Phase 3 - PR: run the pr-author agent as a distinct opencode run in the
#     same clone. It authors the PR title and body from the branch diff and
#     POSTs the pull request.
PR_SPEC="BRANCH: $BRANCH
BASE: $BASE
REPO: $REPO"

echo "Running phase 3/3 (PR): OPENCODE_API_KEY=*** $OPENCODE_BIN run --agent pr-author --auto --print-logs \"...\""
if ! run_agent_phase pr-author "$PR_SPEC"; then
  fail "pr phase failed: opencode run exited non-zero (see log above)"
fi
pass "pr phase completed (pr-author exited 0, PR created)"
