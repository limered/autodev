# Shape of the local client daemon on the Windows host

`wayfinder:grilling`

**Closed** — resolution below.

## Question

What form does the persistent outbound client on this PC take, and how does it run?

Resolve:
- Process form: a long-running PowerShell script loop vs a scheduled task vs a small service. (Charting settled: **one daemon, single loop** — sync issues up, poll for start requests, invoke `start-job.ps1` unchanged.)
- How it's started and kept running on Windows (manual run, Task Scheduler at logon, etc.) and how it's stopped.
- The local **watched-repo config file**: location, format, what it holds (repo list, GitHub token ref, backend url, poll interval).
- Loop structure and cadence: sync interval vs poll interval, and how it serializes the one-at-a-time constraint (it must not pull a new start while a `start-job.ps1` run is in flight).
- Failure behaviour: what happens if the backend or GitHub is unreachable (retry, skip, log) — must never crash the loop.
- How it invokes `start-job.ps1` and threads the correlation id (coordinate with the correlation ticket).

## Blocked by

- [Sync GitHub issues from watched repos to the backend](01-github-issue-sync.md)

## Resolution (closed)

**Form & lifecycle** — a foreground `dispatch-client.ps1` script loop, started in a terminal and stopped with Ctrl-C. No service/scheduled-task for MVP (visible console + live logging to "feel it out"); wrapping in Task Scheduler-at-logon or a service is a trivial later step (map fog).

**Config** — a gitignored `dispatch-client.config.json` at repo root (beside `start-job.ps1`). Holds `repos[]`, `backendUrl`, `syncIntervalSec` (default ~300), `pollIntervalSec` (default ~15), `model` default. The GitHub token is **not** in config — it references `.secrets/github-pat.txt` (path fixed by ticket 01).

**Loop** — a single loop ticking at `pollIntervalSec`. Each tick: (1) run the GitHub issue sync only if `syncIntervalSec` has elapsed since last sync; (2) poll the backend for a claimed start. One thread, sequential within a tick — no concurrency.

**One-at-a-time** — `start-job.ps1` is invoked **synchronously inline**; it blocks until the VM is destroyed. The blocking call *is* the mutex — while a run executes the loop isn't polling, so no busy flag and no race. Issue sync also pauses during a run (10+ min) — acceptable for MVP. Backend independently rejects start-next while a run is `running` (charting); the client simply won't ask.

**Failure** — per-phase `try/catch` (sync, poll, start) inside each tick: log a warning to console and continue to the next tick. The loop's own cadence is the retry; no backoff machinery for MVP. A `start-job.ps1` failure is already reported as a `failed` run by `factory-report.ps1`. **A failed claim/start must be reported back so the queue item doesn't stick in `running`** — coordinate with ticket 04's claim protocol.

**Invoking start-job & correlation** — the daemon turns a claimed queue item (repo + issue) into `start-job.ps1 -RepoUrl <x> -Spec <y>` and owns threading the correlation id and reporting completion back to the backend. Direction: the daemon **generates the runId and passes it in** via a new `-RunId` param on `start-job.ps1` (today it self-generates at line 55). Exact field/plumbing deferred to ticket 05.

**Handoffs**
- Ticket 04 (API contract): the daemon needs a poll/claim endpoint returning the claimed item, and a way to report claim/start failure (so a stuck `running` can't happen).
- Ticket 05 (correlation): add `-RunId` param to `start-job.ps1`; daemon supplies it.
