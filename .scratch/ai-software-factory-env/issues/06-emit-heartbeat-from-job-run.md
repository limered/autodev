# 06 — Emit heartbeat from the job run

**What to build:** The feature-builder run inside the job VM touches a marker file (`/tmp/heartbeat`) every time the agent emits a new line of output, so the marker's freshness reflects agent liveness. A stuck agent emits nothing, so the marker goes stale. No host-side consumer yet — this ticket only produces the signal.

**Blocked by:** None — can start immediately

**Status:** done

Implemented by the factory itself as the issue 05 smoke-test payload — PR #4: https://github.com/limered/autodev/pull/4

- [x] The job run wraps `opencode run --print-logs` so a marker file is touched on every new output line.
- [x] Agent output still reaches the harness output as before (the wrapper is transparent).
- [x] Watching the marker's mtime shows it advancing while the agent produces output and stopping when the agent goes silent.
- [x] The marker file path is documented so the host-side poller (ticket 07) can rely on it as a contract.
  - `/tmp/heartbeat` documented in the script with a contract comment.
