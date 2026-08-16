# ADR 002: Agent Heartbeat and Freeze Snapshot

## Status
Proposed

## Context
In issue 03 (headless job agent), a job silently hung for ~60s. The VM was
healthy and the network was fine; the **agent** inside it was stuck on a model
that errored every request. `start-job.ps1` runs the job as a single
synchronous `multipass exec` (`start-job.ps1:130`), so a stalled agent blocks
the host indefinitely with no liveness signal and no forensics — when the VM is
later destroyed, all state needed to debug the freeze is gone.

The failure we need to catch is **agent liveness**: a job that is running but
making no forward progress. VM-level death is out of scope — in practice the VM
stays up and only the agent stalls.

The consumer today is `start-job.ps1` on the Windows host. A future
orchestrator / web UI (deferred in ADR 001) will watch many parallel jobs; the
marker-file path and manifest schema are defined as a contract so that consumer
can adopt them later without rework, but only the host-poll consumer is wired
now.

## Decision

**Heartbeat = agent-output freshness, expressed as a marker file.**
A wrapper around `opencode run --print-logs --format json` touches a marker file
in the VM (e.g. `/tmp/heartbeat`) **only when the agent emits a new output
line**. A stuck agent emits nothing, so the marker goes stale — absence of
progress is the signal. Mechanically:

```
opencode run ... --print-logs --format json \
  | while IFS= read -r line; do printf '%s\n' "$line"; touch /tmp/heartbeat; done
```

The marker is driven by real agent output rather than a fixed timer, so it
cannot report a stalled agent as alive.

**Detection = host-side polling.**
The host polls the marker's freshness via `multipass exec <vm> -- stat`. The
host is the healthy vantage point; a stuck agent cannot be trusted to report on
itself. **Staleness threshold: 5 minutes.** A single threshold is sufficient —
a dead model never recovers, so a longer wait only costs a few minutes on a rare
failure while avoiding false-kills of legitimately long steps (large model
completions, `dotnet test`, Docker pulls).

**On stall = collect a freeze snapshot, then tear down.**
A stall becomes a normal job failure (reuses the existing `$jobFailed` /
`finally` teardown in `start-job.ps1:154`). Before teardown, the host collects a
**freeze snapshot** from the still-alive VM via `multipass exec`:

- last N lines of the `opencode` output (what the agent said before going silent)
- the marker file's last-touched timestamp
- job params (repo, branch, spec, model)
- `ps aux`, `free -m`, `df -h` (catch OOM / full disk as silent-hang causes)

The collection step is itself **bounded by a short timeout** so a fully-wedged
VM cannot re-hang the very tooling built to catch hangs.

**Snapshot = single JSON manifest.**
The snapshot is one JSON manifest written to
`.scratch/freezes/<job>-<timestamp>/`. It is **always pulled to the host** (so
it survives VM destruction), and the VM is destroyed unless `-KeepVmOnFailure`
is passed — reusing the existing flag (`start-job.ps1:155`), no new flag. The
default remains "destroy VM," but a stall now always leaves a debuggable
artifact behind.

**Location of the logic.**
Host-poll and snapshot logic live in a **sibling script** that `start-job.ps1`
calls, not inline — the `try/finally` in `start-job.ps1` is already dense.

## Consequences

### Positive
- The host stops blocking forever; a stuck agent is detected and killed within 5 minutes.
- Every stall leaves a forensic manifest, so freezes can be debugged after the VM is gone.
- Reuses existing plumbing: `--print-logs` output, the `$jobFailed` teardown path, and `-KeepVmOnFailure`.
- No cooperation required from the (broken) agent; the heartbeat is derived from output it already produces.

### Negative / Risks
- A legitimately long single step over 5 minutes with zero interim output would be false-killed; tune the threshold if this occurs.
- Snapshot collection depends on `multipass exec` still working; a VM wedged at the hypervisor level (not just the agent) would yield only a partial snapshot even with the bounded timeout.

## Open Questions
1. Exact marker path and manifest schema (field names) — to be pinned when the sibling script is written.
2. How many log lines to capture in the snapshot (default assumption: last ~50).
