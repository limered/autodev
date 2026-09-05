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
- **IssueResolver Seam** — Job finish path routes cross-domain issues/queue SQL through `IIssueResolver` (issues domain owns it; SQL + in-memory Adapters, queue + run callers). Superseded the review's RunCompletion Module proposal during rebase on origin/main.
- **Host liveness Module** — pure `IsOnline(lastSeen, now)` shared by real + fake Adapters; 20s threshold lives once.
