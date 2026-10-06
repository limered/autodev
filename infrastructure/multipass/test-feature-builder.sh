#!/usr/bin/env bash
# End-to-end feature-builder test for the slop-factory job VM.
# Sets up the GitHub PAT, opencode API key, clones the project
# repo, injects the .opencode agent configuration, then runs the frozen
# workflow headlessly in the same clone, dispatched by stage id from
# WORKFLOW_STAGES_B64 (the host launcher rechecks the claim at start and
# hands over the picked, ordered stage list): implementation (feature-builder
# agent: issue token in, implemented branch pushed; test-runner test agent
# inside the same stage), review loop (<=3 iterations of code-review scans,
# each followed by a feature-builder fix-findings pass while AFK findings
# remain - never a red gate, so the review sees the implementation before
# static-analysis touches it), quality loop (<=3 iterations of
# static-analysis scans, each followed by a fix-findings pass - never a red
# gate), test re-run (fixes and autofixes must keep every harness green),
# architecture-review (agentic-review agent, findings filed as a
# ready-for-human tracker issue) and PR (pr-author agent: PR authored from
# the branch diff and POSTed). Generic gates run only when their stage is
# present; an unknown stage id fails fast rather than being skipped.
# The calling PowerShell harness verifies the resulting branch/PR.
set -euo pipefail

rm -f /tmp/factory-done
trap 'rc=$?; printf "%s" "$rc" > /tmp/factory-done 2>/dev/null || true' EXIT

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

# Category set (run categories): the host transfers the repo-root agents.json
# to /tmp/agents.json before this script runs. The VM builds its category list
# on startup from that file and reports liveness by category (phase_category
# below) over the existing heartbeat channel.
# Frozen workflow: the host launcher rechecks the claimed pick at start against
# the same catalog and hands the ordered stage-id list over base64-encoded
# (WORKFLOW_STAGES_B64, comma-joined). The guest runs exactly those stages, in
# that order, in this clone — a stage-id list that is missing, empty, or unrun
# by this guest fails fast and never falls back to a hardcoded phase order.
AGENTS_JSON="/tmp/agents.json"
CATEGORY_SPECS=()  # entries "id|type|member1,member2" in stages-map order

load_category_specs() {
  CATEGORY_SPECS=()
  [[ -f "$AGENTS_JSON" ]] || return 0
  command -v jq >/dev/null 2>&1 || return 0
  local ids
  ids=$(jq -r '.stages | keys_unsorted[]' "$AGENTS_JSON" 2>/dev/null) || return 0
  [[ -n "$ids" ]] || return 0
  local id type members
  while IFS= read -r id; do
    [[ -n "$id" ]] || continue
    type=$(jq -r --arg id "$id" '.stages[$id].type // empty' "$AGENTS_JSON" 2>/dev/null) || continue
    members=$(jq -r --arg id "$id" '(.stages[$id].agents // []) | join(",")' "$AGENTS_JSON" 2>/dev/null) || continue
    [[ -n "$type" && -n "$members" ]] || continue
    [[ "$type" == "parallel" ]] && type="sequential"
    CATEGORY_SPECS+=("$id|$type|$members")
  done <<< "$ids"
}

load_category_specs

fail() {
  echo "FAIL: $1" >&2
  exit 1
}

pass() {
  echo "PASS: $1"
}

# The frozen workflow stage list, base64-encoded by the host (WORKFLOW_STAGES_B64,
# comma-joined). It is authoritative: the guest loops it, and a missing, empty,
# or undecodable list fails fast with a stale-workflow reason rather than
# falling back.
FROZEN_STAGES=()
WORKFLOW_STAGES_B64="${WORKFLOW_STAGES_B64:-}"
if [[ -n "$WORKFLOW_STAGES_B64" ]]; then
  _frozen_csv="$(printf '%s' "$WORKFLOW_STAGES_B64" | base64 -d)" || fail "stale-workflow: frozen stage list is not decodable base64"
  IFS=',' read -r -a FROZEN_STAGES <<< "$_frozen_csv"
fi
if [[ "${#FROZEN_STAGES[@]}" -eq 0 ]] || [[ -z "${FROZEN_STAGES[0]}" && "${#FROZEN_STAGES[@]}" -eq 1 ]]; then
  fail "stale-workflow: no frozen workflow stage list was handed to the guest; refusing to fall back to any default pipeline"
fi
for _stage in "${FROZEN_STAGES[@]}"; do
  _stage_trim="${_stage//[[:space:]]/}"
  [[ -n "$_stage_trim" ]] || fail "stale-workflow: the frozen workflow contains an empty stage id"
done

# Upfront runner check, mirroring the dispatch case arms below: an unknown
# stage id fails fast here, so a resume skip below the RESUME_IDX cut can
# never silently pass over a stage the guest cannot run.
for _stage in "${FROZEN_STAGES[@]}"; do
  _stage_trim="${_stage//[[:space:]]/}"
  case "$_stage_trim" in
    implementation|review-loop|static-loop|test-rerun|architecture-review|pr-author) ;;
    *) fail "unknown stage id at start: '$_stage_trim' is in the frozen workflow but has no guest runner" ;;
  esac
done

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
git config --global user.email "bot@slop-factory.local"
git config --global user.name "slop-factory Bot"
pass "Git identity configured"

# 6. Shallow clone over HTTPS using the bot PAT. The token is embedded in the
#    remote URL so both the clone and later `git push` authenticate without SSH.
rm -rf "$WORK_DIR"
REPO_HTTPS="https://x-access-token:${PAT}@github.com/${REPO}.git"
git clone --depth 1 "$REPO_HTTPS" "$WORK_DIR"
pass "Shallow-cloned https://github.com/${REPO}.git"

# Same-branch resume: RESUME_BRANCH + RESUME_STAGE arrive as guest
# environment from the Runner passthrough. When both name a remote branch that
# exists and is ahead of base, check it out, log the cloned commit for
# auditability (a fixup pushed too late is obvious), and skip implement —
# stages below jump to the start of the enclosing stage. When the branch is
# absent or not ahead, fall back to the normal full pipeline so implement
# crashes stay restartable through the same button.
#
# The resume entry point is resolved against the frozen stage list above, not
# the catalog: the stage's position in the picked workflow sets the stage
# threshold, so a stage the workflow does not run can never resume into it.
# Pre-rename labels resolve only when the frozen list holds the renamed stage;
# anything else falls back to the full frozen pipeline.
RESUME_BRANCH="${RESUME_BRANCH:-}"
RESUME_STAGE="${RESUME_STAGE:-}"
RESUME_ACTIVE=0
RESUME_IDX=0
# Zero-based position of a stage id in the frozen workflow order; fails when absent.
stage_index() {
  local _want="$1" _i
  for _i in "${!FROZEN_STAGES[@]}"; do
    if [[ "${FROZEN_STAGES[$_i]}" == "$_want" ]]; then printf '%s' "$_i"; return 0; fi
  done
  return 1
}
# Resolves a resume label to a frozen stage id. Exact matches win; legacy
# pre-rename labels map to their successor only when the frozen list holds it.
resolve_resume_stage() {
  local _want="$1" _alias="" _id
  for _id in "${FROZEN_STAGES[@]}"; do
    if [[ "$_id" == "$_want" ]]; then printf '%s' "$_want"; return 0; fi
  done
  case "$_want" in
    quality-loop) _alias="static-loop" ;;
    agentic-review) _alias="architecture-review" ;;
  esac
  if [[ -n "$_alias" ]]; then
    for _id in "${FROZEN_STAGES[@]}"; do
      if [[ "$_id" == "$_alias" ]]; then printf '%s' "$_alias"; return 0; fi
    done
  fi
  return 1
}
if [[ -n "$RESUME_BRANCH" && -n "$RESUME_STAGE" ]]; then
  RESOLVED_STAGE=""
  RESUME_POS=""
  if RESOLVED_STAGE="$(resolve_resume_stage "$RESUME_STAGE")" && RESUME_POS="$(stage_index "$RESOLVED_STAGE")"; then
    RESUME_STAGE="$RESOLVED_STAGE"
    RESUME_IDX=$((RESUME_POS + 1))
  fi
  if [[ "$RESUME_IDX" -eq 0 ]]; then
    echo "resume: unknown resume stage '$RESUME_STAGE'; running the full frozen pipeline"
  elif git ls-remote --heads "$REPO_HTTPS" "$RESUME_BRANCH" | grep -q "$RESUME_BRANCH"; then
    git -C "$WORK_DIR" fetch origin "$RESUME_BRANCH:$RESUME_BRANCH"
    git -C "$WORK_DIR" checkout "$RESUME_BRANCH"
    RESUME_COMMIT="$(git -C "$WORK_DIR" rev-parse HEAD)"
    echo "resume: checked out existing branch $RESUME_BRANCH at $RESUME_COMMIT (resume from $RESUME_STAGE)"
    RESUME_BASE="${BASE:-main}"
    if ! RESUME_AHEAD="$(git -C "$WORK_DIR" rev-list --count "origin/$RESUME_BASE..HEAD" 2>/dev/null)"; then
      echo "resume: cannot count commits ahead of origin/$RESUME_BASE; falling back to full pipeline"
      RESUME_ACTIVE=0
    elif [[ "$RESUME_AHEAD" -eq 0 ]]; then
      echo "resume: branch $RESUME_BRANCH is not ahead of origin/$RESUME_BASE; falling back to full pipeline"
      RESUME_ACTIVE=0
    else
      echo "resume: HEAD is $RESUME_AHEAD commit(s) ahead of origin/$RESUME_BASE; skipping implement"
      RESUME_ACTIVE=1
    fi
  else
    echo "resume: branch $RESUME_BRANCH absent on remote; falling back to full pipeline"
  fi
fi

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

# 9. clone workspace + implement spec: the feature-builder agent implements
#    headlessly against the issue token, commits, and pushes the branch. It
#    does NOT create the PR; that is the PR stage's job.
cd "$WORK_DIR"
BASE="${BASE:-main}"
BASE_REF="origin/$BASE"
IMPL_SPEC="ISSUE: $ISSUE
BRANCH: $BRANCH
BASE: $BASE
REPO: $REPO"

trap 'rc=$?; printf "%s" "$rc" > /tmp/factory-done 2>/dev/null || true' EXIT

# Maps a reporting worker to the seeded category slot it lights.
phase_category() {
  local agent="$1"
  local iteration="${2:-0}"
  if [[ "${#CATEGORY_SPECS[@]}" -gt 0 ]]; then
    config_phase_category "$agent" "$iteration" && return 0
  fi
  # Degraded fallback: the catalog was unreadable (no jq or no
  # /tmp/agents.json), so heartbeat categories fall back to the last known
  # shape instead of going dark. Loop phases always pass their slot
  # explicitly, so this only classifies single-run phases here.
  if [[ "$agent" == "code-review" ]]; then
    printf 'review-loop'
  elif [[ "$agent" == "static-analysis" ]]; then
    printf 'static-loop'
  elif [[ "$agent" == "feature-builder" && "$iteration" != "0" ]]; then
    # Fix passes in both loops share this worker: review-loop owns 1..3,
    # static-loop continues at 4..6 (mirrors Get-PhaseStepCandidates).
    if [[ "$iteration" -ge 4 ]]; then printf 'static-loop'; else printf 'review-loop'; fi
  else
    printf '%s' "$agent"
  fi
}

# Config-driven slot lookup for phase_category.
config_phase_category() {
  local agent="$1"
  local iteration="$2"
  local entry id type members
  if [[ "$iteration" != "0" ]]; then
    for entry in "${CATEGORY_SPECS[@]}"; do
      IFS='|' read -r id type members <<< "$entry"
      if [[ "$type" == "loop" && ",$members," == *",$agent,"* ]]; then
        printf '%s' "$id"
        return 0
      fi
    done
  fi
  local seq=()
  for entry in "${CATEGORY_SPECS[@]}"; do
    IFS='|' read -r id type members <<< "$entry"
    if [[ "$type" != "loop" && ",$members," == *",$agent,"* ]]; then
      seq+=("$id")
    fi
  done
  if [[ "${#seq[@]}" -gt 0 && "$iteration" -ge 0 && "$iteration" -lt "${#seq[@]}" ]]; then
    printf '%s' "${seq[$iteration]}"
    return 0
  fi
  if [[ "${#seq[@]}" -eq 0 ]]; then
    for entry in "${CATEGORY_SPECS[@]}"; do
      IFS='|' read -r id type members <<< "$entry"
      if [[ ",$members," == *",$agent,"* ]]; then
        printf '%s' "$id"
        return 0
      fi
    done
  fi
  return 1
}

# Runs one agent phase headlessly and returns the opencode exit code.
# stdin from /dev/null so opencode never blocks waiting on a TTY.
# /tmp/heartbeat is the liveness marker contract consumed by the host-side poller.
# /tmp/current-phase is the phase marker: each phase writes its agent name here
# as it begins, so the host heartbeat poller can relay the current phase up to
# the backend without any new secrets crossing into the VM.
# /tmp/current-category is the category marker: each phase writes the seeded
# slot it fills here as it begins (review-loop / static-loop for each loop's
# scan and fix workers), so the host relays liveness by category and the
# dashboard lights the seeded slot even when the worker name differs from it.
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
# Liveness = "the agent emitted a new output line" (ADR 002 output-progress
# semantics), not "the opencode process is alive". The marker is touched only
# inside the output loop below, so a deadlocked-but-alive agent that prints
# nothing lets the marker go stale and the host stall verdict can fire for it.
# Long-silence tolerance is explicit on the host: the watcher allows 10
# minutes without output (a silent model completion emits no lines) before
# declaring a stall, so legitimately quiet steps are not false-killed.
run_agent_phase() {
  local agent="$1"
  local prompt="$2"
  local iteration="${3:-0}"
  local category_override="${4:-}"
  local jsonl="/tmp/phase-${agent}-${iteration}.jsonl"
  local meta="/tmp/phase-${agent}-${iteration}.meta.json"
  # Write the phase marker before touching the heartbeat so the host reads a
  # consistent (phase, heartbeat-mtime) pair when it observes the mtime advance.
  # The category marker rides alongside it for the same read. Loops pass
  # their slot explicitly (a worker shared by two loops cannot be resolved
  # from its name alone); other phases fall back to the config lookup.
  printf '%s' "$agent" > /tmp/current-phase
  if [[ -n "$category_override" ]]; then
    printf '%s' "$category_override" > /tmp/current-category
  else
    phase_category "$agent" "$iteration" > /tmp/current-category
  fi
  touch /tmp/heartbeat
  # Truncate first so a retry never mixes two runs. stdout (the NDJSON stream)
  # flows through the loop so each emitted line lands verbatim on disk, echoes
  # live to the console, and touches the heartbeat; stderr stays on the console.
  : > "$jsonl"
  # No --model: each agent resolves its own model from the unpacked opencode
  # config (~/.config/opencode), so feature-builder, test-runner,
  # code-review, static-analysis, agentic-review and pr-author can differ. $MODEL is
  # reporting-only.
  local start_ms end_ms rc=0
  start_ms=$(date +%s%3N)
  set +e
  set +o pipefail
  $OPENCODE_BIN run --agent "$agent" --auto --format json "$prompt" </dev/null \
    | while IFS= read -r line; do
        printf '%s\n' "$line" >>"$jsonl"
        printf '%s\n' "$line"
        touch /tmp/heartbeat
      done
  rc=${PIPESTATUS[0]:-$?}
  set -o pipefail
  set -e
  end_ms=$(date +%s%3N)
  local status="done"
  [[ "$rc" -eq 0 ]] || status="failed"
  printf '{"agent":"%s","iteration":%d,"durationMs":%d,"status":"%s"}\n' \
    "$agent" "$iteration" "$((end_ms - start_ms))" "$status" > "$meta"
  return "$rc"
}

# Review loop (phase 3): up to 3 iterations of code-review scan ->
# feature-builder fix-findings pass. The control signal is the `status`
# sentinel the code-review agent appends to .factory/code-review-result.json;
# the agent's exit code means only that the agent itself broke -> hard fail().
# Never fail() on findings: `hitl-only` (everything left is for a human) and
# cap exhaustion both proceed to the next phase. Reads $REVIEW_SPEC for the
# scan and $REVIEW_FIX_SPEC for the fix pass, both defined above the
# dispatch loop.
run_review_loop() {
  for i in 1 2 3; do
    run_agent_phase code-review "$REVIEW_SPEC" "$i" "review-loop" || fail "code-review agent crashed"
    status=$(tail -n1 .factory/code-review-result.json | jq -r .status) || fail "code-review agent crashed: cannot read status sentinel from .factory/code-review-result.json"
    case "$status" in
      clean|hitl-only) return 0 ;;                      # nothing left to auto-fix
      fixed)  run_agent_phase feature-builder "$REVIEW_FIX_SPEC" "$i" "review-loop" || fail "fix pass crashed" ;;
      *) fail "code-review returned unknown status sentinel: $status" ;;
    esac
  done
  # ponytail: 3x is a backstop, not the real exit. Self-escalation converts a
  # finding that survives one fix pass to HITL (already on the tracker), so
  # still-AFK at iteration 3 is a genuinely churning finding, vanishingly
  # rare. Proceed rather than fail(); the human reviewing the PR is the final
  # backstop.
  return 0
}

# Quality loop (phase 4): up to 3 iterations of static-analysis scan ->
# feature-builder fix-findings pass. The control signal is the `status`
# sentinel the static-analysis agent appends to .factory/static-analysis-result.json;
# the agent's exit code means only that the agent itself broke -> hard fail().
# Never fail() on findings: `hitl-only` (everything left is for a human) and
# cap exhaustion both proceed to the next phase. Reads $QUALITY_SPEC for
# the scan and $FIX_SPEC for the fix pass, both defined above the dispatch loop.
run_quality_loop() {
  for i in 1 2 3; do
    run_agent_phase static-analysis "$QUALITY_SPEC" "$i" "static-loop" || fail "static-analysis agent crashed"
    status=$(tail -n1 .factory/static-analysis-result.json | jq -r .status) || fail "static-analysis agent crashed: cannot read status sentinel from .factory/static-analysis-result.json"
    case "$status" in
      clean|hitl-only) return 0 ;;                      # nothing left to auto-fix
      # Fix passes continue the shared worker's numbering at 4..6 (the review
      # loop's fix passes own 1..3) so relay files never collide — mirrors
      # Get-PhaseStepCandidates' next-free assignment on the host.
      fixed)  run_agent_phase feature-builder "$FIX_SPEC" "$((i+3))" "static-loop" || fail "fix pass crashed" ;;
      *) fail "static-analysis returned unknown status sentinel: $status" ;;
    esac
  done
  # ponytail: 3x is a backstop, not the real exit. Ticket 06 self-escalation
  # converts a finding that survives one fix pass to HITL (already on tracker),
  # so still-AFK at iteration 3 is a genuinely churning finding, vanishingly
  # rare. Proceed rather than fail(); the human reviewing the PR is the final
  # backstop.
  return 0
}

# --------------------------------------------------------------------------
# The frozen workflow pipeline: stage runners dispatched by stage id from
# FROZEN_STAGES in the order the claim froze, inside this same clone. Each
# generic catalog stage has one runner; a stage id the guest has no runner
# for fails fast instead of being skipped silently. Generic gates are
# attached to their owning stage (the forward gate below lives inside
# implementation), so a gate runs only when its stage is present in the
# frozen workflow; per-workflow custom gates are out of scope here.
# --------------------------------------------------------------------------

TEST_SPEC="BRANCH: $BRANCH
BASE: $BASE
REPO: $REPO"

REVIEW_SPEC="ISSUE: $ISSUE
BRANCH: $BRANCH
BASE: $BASE
REPO: $REPO"

REVIEW_FIX_SPEC="MODE: fix-findings
FINDINGS: .factory/code-review-findings.json
BRANCH: $BRANCH
BASE: $BASE
REPO: $REPO"

QUALITY_SPEC="BRANCH: $BRANCH
BASE: $BASE
REPO: $REPO"

FIX_SPEC="MODE: fix-findings
FINDINGS: .factory/findings.json
BRANCH: $BRANCH
BASE: $BASE
REPO: $REPO"

AR_SPEC="BRANCH: $BRANCH
BASE: $BASE
REPO: $REPO"

PR_SPEC="BRANCH: $BRANCH
BASE: $BASE
REPO: $REPO"

# Tests run right after the implement phase inside the implementation stage.
# The test-runner reads the target repo's AGENTS.md for
# `test-harness.<name>: <command>` entries and runs every one. If no harness
# is declared, or any harness exits non-zero, the run fails here.
run_test_phase() {
  local iter="$1" label="$2"
  if ! run_agent_phase test-runner "$TEST_SPEC" "$iter"; then
    fail "$label: a declared harness is red or no harness was declared (see log above)"
  fi
  pass "$label completed (test-runner exited 0, harness green)"
}

# Forward gate, attached to the implementation stage: later stages run only
# if the implement agent exited cleanly AND the branch has commits ahead of
# base. A clean-but-empty implement (exit 0, nothing committed) stops here
# and is recorded as a failed run - fail() exits 1, which fails the exec on
# the host, which marks the run failed (run-failed event, freeze snapshot,
# teardown). A resumed run already verified ahead-of-base at checkout, so it
# skips this gate.
run_forward_gate() {
  if [[ "$RESUME_ACTIVE" -eq 1 ]]; then
    pass "gate skipped: resume already verified HEAD ahead of base"
    return 0
  fi
  if ! AHEAD_COUNT=$(git rev-list --count "$BASE_REF..HEAD" 2>/dev/null); then
    fail "gate: cannot count commits ahead of $BASE_REF - is the base ref present in the clone?"
  fi
  if [[ "$AHEAD_COUNT" -eq 0 ]]; then
    fail "clean-but-empty implement: HEAD has no commits ahead of $BASE_REF; stopping before the next stage"
  fi
  pass "gate passed: HEAD is $AHEAD_COUNT commit(s) ahead of $BASE_REF"
}

run_architecture_review_phase() {
  if ! run_agent_phase agentic-review "$AR_SPEC" 0; then
    fail "agentic-review phase failed: opencode run exited non-zero (operational failure, see log above)"
  fi
  pass "agentic-review phase completed (agentic-review exited 0)"
}

run_pr_phase() {
  if ! run_agent_phase pr-author "$PR_SPEC" 0; then
    fail "pr phase failed: opencode run exited non-zero (see log above)"
  fi
  pass "pr phase completed (pr-author exited 0, PR created)"
}

echo "Frozen workflow stages, in picked order: ${FROZEN_STAGES[*]}"
stage_count=${#FROZEN_STAGES[@]}
for _idx in "${!FROZEN_STAGES[@]}"; do
  stage="${FROZEN_STAGES[$_idx]}"
  stage_no=$((_idx + 1))
  if [[ "$RESUME_ACTIVE" -eq 1 && "$stage_no" -lt "$RESUME_IDX" ]]; then
    echo "Skipping stage $stage_no/$stage_count ($stage): resume starts at '$RESUME_STAGE'"
    continue
  fi
  case "$stage" in
    implementation)
      # Implement and the first test agent are the implementation stage's
      # members; the forward gate sits between them. On a resumed run the
      # checked-out branch already holds the implementation (verified above),
      # so only the gate skip and the test agent run.
      if [[ "$RESUME_ACTIVE" -eq 1 ]]; then
        echo "Skipping implement: resumed branch already holds the implementation"
      else
        echo "Running stage $stage_no/$stage_count ($stage): $OPENCODE_BIN run --agent feature-builder --auto --format json \"...\""
        if ! run_agent_phase feature-builder "$IMPL_SPEC" 0; then
          fail "implement phase failed: opencode run exited non-zero (see log above)"
        fi
        pass "implement phase completed (feature-builder exited 0)"
      fi
      run_forward_gate
      echo "Running test agent of stage $stage_no/$stage_count ($stage): $OPENCODE_BIN run --agent test-runner --auto --format json \"...\""
      run_test_phase 0 "test phase"
      ;;
    review-loop)
      echo "Running stage $stage_no/$stage_count ($stage): $OPENCODE_BIN run --agent code-review --auto --format json \"...\""
      run_review_loop
      pass "review stage completed"
      ;;
    static-loop)
      echo "Running stage $stage_no/$stage_count ($stage): $OPENCODE_BIN run --agent static-analysis --auto --format json \"...\""
      run_quality_loop
      pass "quality stage completed"
      ;;
    test-rerun)
      # Re-run every harness after the loops' fix commits. Iteration 1 (the
      # implementation stage's test used 0): the backend keys steps on
      # (agent, iteration), so the re-run lands as its own step row.
      echo "Running stage $stage_no/$stage_count ($stage): $OPENCODE_BIN run --agent test-runner --auto --format json \"...\""
      run_test_phase 1 "test re-run phase"
      ;;
    architecture-review)
      # Runs the improve-codebase-architecture skill headless (explore-only)
      # and files its candidates as one ready-for-human tracker issue. It
      # never fixes and never fails the run for findings - a non-zero exit
      # here means an operational failure (PAT missing, tracker unreachable),
      # not findings.
      echo "Running stage $stage_no/$stage_count ($stage): $OPENCODE_BIN run --agent agentic-review --auto --format json \"...\""
      run_architecture_review_phase
      ;;
    pr-author)
      echo "Running stage $stage_no/$stage_count ($stage): $OPENCODE_BIN run --agent pr-author --auto --format json \"...\""
      run_pr_phase
      ;;
    *)
      fail "unknown stage id at start: '$stage' is in the frozen workflow but has no guest runner"
      ;;
  esac
done

echo "Frozen workflow sequence complete"

