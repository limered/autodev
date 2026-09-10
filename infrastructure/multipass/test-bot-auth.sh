#!/usr/bin/env bash
# Bot Git authentication test for the slop-factory job VM.
# Verifies the bot SSH key, GitHub host key, shallow clone over SSH,
# branch push, and PR creation via the GitHub API.
set -euo pipefail

REPO_SSH="git@github.com:limered/autodev.git"
REPO_API="limered/autodev"
SSH_DIR="$HOME/.ssh"
KEY_NAME="bot-github"
PAT_FILE="/tmp/github-pat.txt"
WORK_DIR="/tmp/autodev-auth-test"
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
BRANCH="bot-auth-test-${TIMESTAMP}"
PR_TITLE="Bot authentication test ${TIMESTAMP}"
PR_BODY="This pull request was created automatically inside a disposable slop-factory VM to verify bot Git authentication. It should be closed and the branch deleted after review."

fail() {
  echo "FAIL: $1" >&2
  exit 1
}

pass() {
  echo "PASS: $1"
}

echo "== Bot Git authentication test =="

# 1. SSH private key injected by the host must land in ~/.ssh with tight permissions.
mkdir -p "${SSH_DIR}"
if [[ ! -f "${SSH_DIR}/${KEY_NAME}" ]]; then
  [[ -f "/tmp/${KEY_NAME}" ]] || fail "SSH private key not found at /tmp/${KEY_NAME} or ${SSH_DIR}/${KEY_NAME}"
  mv "/tmp/${KEY_NAME}" "${SSH_DIR}/${KEY_NAME}"
fi
chmod 600 "${SSH_DIR}/${KEY_NAME}"
pass "SSH private key present in ~/.ssh with permissions 600"

# Move the public key alongside the private key if it was injected.
if [[ ! -f "${SSH_DIR}/${KEY_NAME}.pub" && -f "/tmp/${KEY_NAME}.pub" ]]; then
  mv "/tmp/${KEY_NAME}.pub" "${SSH_DIR}/${KEY_NAME}.pub"
fi

# 2. GitHub must be a known host so StrictHostKeyChecking can stay on.
if ! ssh-keygen -F github.com >/dev/null 2>&1; then
  ssh-keyscan -H github.com >> "${SSH_DIR}/known_hosts" 2>/dev/null
fi
[[ -f "${SSH_DIR}/known_hosts" ]] || fail "known_hosts file not created"
pass "GitHub is in ~/.ssh/known_hosts"

# 3. PAT injected by the host must be readable.
[[ -f "${PAT_FILE}" ]] || fail "PAT file not found at ${PAT_FILE}"
PAT=$(tr -d '\n\r ' < "${PAT_FILE}")
[[ -n "${PAT}" ]] || fail "PAT is empty"
pass "GitHub PAT present"

# 4. Git needs an identity to commit.
git config --global user.email "bot@slop-factory.local"
git config --global user.name "slop-factory Bot"
pass "Git identity configured"

# 5. Shallow clone over SSH using the bot key only.
rm -rf "${WORK_DIR}"
export GIT_SSH_COMMAND="ssh -i ${SSH_DIR}/${KEY_NAME} -o IdentitiesOnly=yes -o StrictHostKeyChecking=yes"
git clone --depth 1 "${REPO_SSH}" "${WORK_DIR}"
pass "Shallow-cloned ${REPO_SSH}"

# 6. Create a unique branch, commit a marker file, and push.
cd "${WORK_DIR}"
git checkout -b "${BRANCH}"
echo "Bot authentication test marker created at ${TIMESTAMP}." > bot-auth-test.md
git add bot-auth-test.md
git commit -m "test: bot authentication verification ${TIMESTAMP}"
git push origin "${BRANCH}"
pass "Pushed branch ${BRANCH}"

# 7. Create a pull request via the GitHub API using the PAT.
PR_RESPONSE=$(curl -sS -X POST \
  -H "Authorization: Bearer ${PAT}" \
  -H "Accept: application/vnd.github.v3+json" \
  -H "Content-Type: application/json" \
  -d "{\"title\":\"${PR_TITLE}\",\"body\":\"${PR_BODY}\",\"head\":\"${BRANCH}\",\"base\":\"main\"}" \
  "https://api.github.com/repos/${REPO_API}/pulls")

PR_URL=$(python3 -c "import sys, json; print(json.load(sys.stdin).get('html_url', ''))" <<< "${PR_RESPONSE}")

[[ -n "${PR_URL}" ]] || fail "GitHub API PR creation failed: ${PR_RESPONSE}"

pass "Created pull request ${PR_URL}"
echo "PR_URL=${PR_URL}"
