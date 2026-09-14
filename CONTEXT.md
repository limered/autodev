# CONTEXT

Product: slop-factory — Jobs run opencode inside a Disposable VM to produce a branch/PR.

## Domain terms (from docs/glossary.md)

- **Job** — checkout + opencode run + branch/PR.
- **Disposable VM** — one Linux VM per Job on Windows, destroyed afterwards.
- **Runner** — host-side Job executor (`start-job` + dispatch client loop).
- **Issue** — GitHub issue labeled `ready-for-agent`; _Avoid_: ticket.
- **Runtime Environment** — isolated Job context: Linux VM on Windows, ephemeral container on Linux.
- **Heartbeat** — agent-output freshness marker; stale = stalled agent.
- **Freeze snapshot** — forensic manifest captured before teardown.

## Deepened Modules (architecture review 2026-09-06)

- **HostVm Module** — Disposable-VM lifecycle behind one Seam (`lib/HostVm.ps1`): launch, cloud-init wait, file transfer, multipass exec, teardown. Native calls behind `-Executor` scriptblock Seam for Pester.
- **Dispatch mapping Modules** — pure Job intake logic (`ConvertTo-IssueSnapshot`, `Get-StaleSeconds`, `ConvertTo-IssueNumber`): co-located in `lib/`, Pester-checked without HTTP/VM.
- **IssueResolver Seam** — Job finish path routes cross-domain issues/queue SQL through `IIssueResolver` (issues domain owns it; SQL + in-memory Adapters, queue + run callers). Superseded the review's RunCompletion Module proposal during rebase on origin/main.
- **Host liveness Module** — pure `IsOnline(lastSeen, now)` shared by real + fake Adapters; 20s threshold lives once.
- **Heartbeat Module** — staleness verdict + VM poll behind one Seam (`lib/Heartbeat.ps1`): epoch/marker/phase reads via `-Executor`, pure `Test-HeartbeatStall`; Pester drives scripted epochs, no live VM.
- **Freeze capture Seam** — `Save-FreezeSnapshot` takes `-Capture` (command→string) plus injectable timestamp/dir; per-capture failures degrade to `<unavailable>` strings so the manifest still lands.
- **Job launch slimming** — VM provision via parametrized `New-VmFromBlueprint` (HostVm Module), owner/name parse + PR poll (`ConvertTo-OwnerRepo`, `Wait-ForPullRequest`) as pure Job intake Modules with fakeable poll.
- **JSON transport Module** — secret-file reads + UTF-8 JSON bytes behind one Seam (`lib/HttpJson.ps1`): Dispatch + Report share it; fake transport in Pester proves non-ASCII survives.
