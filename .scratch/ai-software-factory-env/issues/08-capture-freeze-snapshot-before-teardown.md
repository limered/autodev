# 08 — Capture a freeze snapshot before teardown

**What to build:** When a stall is detected, the host captures the VM's state before the VM is destroyed, so a freeze can be debugged after the fact. The host collects a single JSON manifest — agent log tail, marker last-touched timestamp, job params (repo, branch, spec, model), and `ps aux` / `free -m` / `df -h` — and writes it under `.scratch/freezes/<job>-<timestamp>/`. The collection runs via a bounded `multipass exec` so a fully-wedged VM cannot re-hang the collector. The snapshot is always pulled to the host; the VM is destroyed unless `-KeepVmOnFailure` is set.

**Blocked by:** 07 — Detect a stalled run and fail the job

**Status:** done

- [x] On a detected stall, a JSON manifest is collected from the still-alive VM before teardown.
- [x] The manifest contains: agent log tail, marker timestamp, job params, and `ps aux` / `free -m` / `df -h` output.
- [x] The manifest is written to `.scratch/freezes/<job>-<timestamp>/` on the host and survives VM destruction.
- [x] The collection step is bounded by a short timeout so a wedged VM cannot re-hang the collector.
- [x] The VM is destroyed by default and kept only when `-KeepVmOnFailure` is passed.
