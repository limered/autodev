#!/usr/bin/env bash
# End-to-end feature-builder test for the AI Software Factory job VM.
# Sets up the bot SSH key, GitHub PAT, opencode API key, clones the project
# repo, injects the .opencode agent configuration, and runs opencode headlessly
# against a tiny spec. The calling PowerShell harness verifies the resulting
# branch/PR.
set -euo pipefail

BRANCH="$1"
ISSUE="$(printf '%s' "${ISSUE_B64:?ISSUE_B64 env var must be set by the host launcher}" | base64 -d)"
REPO="$2"                       # owner/name, e.g. limered/autodev

REPO_SSH="git@github.com:${REPO}.git"
SSH_DIR="$HOME/.ssh"
KEY_NAME="bot-github"
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

# 1. SSH private key injected by the host must land in ~/.ssh with tight permissions.
mkdir -p "$SSH_DIR"
[[ -f "/tmp/$KEY_NAME" ]] || fail "SSH private key not found at /tmp/$KEY_NAME"
mv "/tmp/$KEY_NAME" "$SSH_DIR/$KEY_NAME"
chmod 600 "$SSH_DIR/$KEY_NAME"
pass "SSH private key installed in ~/.ssh"

# Move the public key alongside the private key if it was injected.
[[ -f "/tmp/$KEY_NAME.pub" ]] && mv "/tmp/$KEY_NAME.pub" "$SSH_DIR/$KEY_NAME.pub"

# 2. GitHub must be a known host so StrictHostKeyChecking can stay on.
if ! ssh-keygen -F github.com >/dev/null 2>&1; then
  ssh-keyscan -H github.com >> "$SSH_DIR/known_hosts" 2>/dev/null
fi
[[ -f "$SSH_DIR/known_hosts" ]] || fail "known_hosts file not created"
pass "GitHub is in ~/.ssh/known_hosts"

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

# 6. Shallow clone over SSH using the bot key only.
rm -rf "$WORK_DIR"
export GIT_SSH_COMMAND="ssh -i $SSH_DIR/$KEY_NAME -o IdentitiesOnly=yes -o StrictHostKeyChecking=yes"
git clone --depth 1 "$REPO_SSH" "$WORK_DIR"
pass "Shallow-cloned $REPO_SSH"

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

# 9. Run opencode headlessly against the issue token.
cd "$WORK_DIR"
FULL_SPEC="ISSUE: $ISSUE
BRANCH: $BRANCH
BASE: main
REPO: $REPO"

echo "Running: OPENCODE_API_KEY=*** $OPENCODE_BIN run --model $MODEL --agent feature-builder --auto --print-logs \"...\""
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
touch /tmp/heartbeat
$OPENCODE_BIN run --model "$MODEL" --agent feature-builder --auto --print-logs "$FULL_SPEC" </dev/null &
OC_PID=$!
( while kill -0 "$OC_PID" 2>/dev/null; do sleep 30; touch /tmp/heartbeat 2>/dev/null; done ) &
TICKER_PID=$!
trap 'kill "$TICKER_PID" 2>/dev/null' EXIT

OC_RC=0
wait "$OC_PID" || OC_RC=$?
kill "$TICKER_PID" 2>/dev/null
[[ "$OC_RC" -eq 0 ]] || fail "opencode run exited $OC_RC"

pass "opencode run completed"
