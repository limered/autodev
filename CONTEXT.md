# CONTEXT

Product: AI Software Factory — Jobs run opencode inside a Disposable VM to produce a branch/PR.

## Domain terms (from docs/glossary.md)

- **Job** — checkout + opencode run + branch/PR.
- **Disposable VM** — one Linux VM per Job, destroyed afterwards.
- **Heartbeat** — agent-output freshness marker; stale = stalled agent.
- **Freeze snapshot** — forensic manifest captured before teardown.

## Deepened Modules (architecture review 2026-09-06)

- **HostVm Module** — Disposable-VM lifecycle behind one Seam (`lib/HostVm.ps1`): launch, cloud-init wait, file transfer, multipass exec, teardown. Native calls behind `-Executor` scriptblock Seam for Pester.
- **Dispatch mapping Modules** — pure Job intake logic (`ConvertTo-IssueSnapshot`, `Get-StaleSeconds`, `ConvertTo-IssueNumber`): co-located in `lib/`, Pester-checked without HTTP/VM.
- **RunCompletion Module** — Job finish path (host stamp + queue-slot release + GitHub close, swallow-to-warn) behind one Seam; RunFold stays pure.
- **Host liveness Module** — pure `IsOnline(lastSeen, now)` shared by real + fake Adapters; 20s threshold lives once.
