#!/usr/bin/env bash
# Smoke test for the AI Software Factory job VM.
# Verifies that every required tool is on PATH and runnable.
set -euo pipefail

failures=0

expect() {
  local name="$1"
  local cmd="$2"
  echo -n "Checking ${name}... "
  if eval "$cmd" >/dev/null 2>&1; then
    echo "OK"
  else
    echo "FAIL"
    failures=$((failures + 1))
  fi
}

expect "Git"          "git --version"
expect "Docker"       "docker --version"
expect "opencode"     "opencode --version"
expect ".NET 8 SDK"   "dotnet --version"
expect "Node.js"      "node --version"
expect "SSH client"   "ssh -V"

# Godot is skipped in the MVP environment. Re-enable once a reliable download
# source inside multipass VMs is found. See ADR-001.
if command -v godot >/dev/null 2>&1; then
  expect "Godot 4" "godot --version"
else
  echo "Checking Godot 4... SKIPPED (not installed)"
fi

if [[ $failures -eq 0 ]]; then
  echo "All smoke tests passed."
  exit 0
else
  echo "${failures} tool(s) failed the smoke test."
  exit 1
fi
