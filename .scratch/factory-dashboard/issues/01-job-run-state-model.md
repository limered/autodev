# Define the job-run state model

`wayfinder:grilling`

**Status:** closed

**Assignee:** driver (claimed)

**Blocked by:** none — frontier.

## Question

What is the canonical shape of a "job run" as the dashboard understands it? Define: the fields (repo, branch, spec, model, vm name, timestamps, PR url, freeze link, …), the set of statuses and the legal transitions between them (launching → running → stalled → failed / done, etc.), and which lifecycle events the host emits to move a run between them. This model is the keystone the ingest contract and storage schema are built on. Keep it minimal but don't design out a future dispatch feature (see map's Not-yet-specified).

## Resolution

### Identity

- **`runId`** — a host-generated GUID created at the start of `start-job.ps1`, sent on every event. Stable before the VM exists, immune to VmName reuse/collision, and safe for out-of-order/retried events.

### Statuses (5, coarse)

`launching` → `running` → (`done` | `failed`); `stalled` is a **transient** status shown during stall handling that always resolves to `failed`.

Legal transitions:

- `launching` → `running` (agent run begins)
- `launching` → `failed` (VM launch / cloud-init / transfer / clone error before the agent runs)
- `running` → `done` (agent finished, PR verified)
- `running` → `failed` (agent errored, or no PR found)
- `running` → `stalled` → `failed` (heartbeat went stale; freeze captured; teardown)

`done` and `failed` are terminal. Coarse by choice: VM-setup phases collapse into `launching`, agent work into `running`.

### Fields

| Field | Notes |
|---|---|
| `runId` | host GUID, primary key |
| `repo` | owner/name |
| `branch` | agent branch |
| `spec` | feature spec text |
| `model` | opencode model id |
| `vmName` | multipass VM name (may be absent at first event) |
| `status` | one of the 5 above |
| `startedAt` | run start (host clock) |
| `finishedAt` | set on done/failed |
| `lastHeartbeatAt` | timestamp; UI shows "last seen Ns ago" and flags staleness |
| `prUrl` | set when PR verified |
| `failureReason` | free text on failure, e.g. "heartbeat stale 317s", "no PR found", "clone failed" |
| `freezeCaptured` | bool |
| `freezeLocalPath` | host path under `.scratch/freezes/`; **not remotely viewable** (lives on the Windows host, not Render) |

### Event model

Host emits **discrete lifecycle events**; the backend folds each into the run's status. Conceptual events (wire format is the ingest-contract ticket's job):

- `run-started` — creates the run in `launching` with repo/branch/spec/model/runId/startedAt
- `provisioned` / `agent-started` — → `running`, sets vmName
- `heartbeat` — updates `lastHeartbeatAt`
- `stall-detected` — → `stalled`, sets failureReason
- `freeze-captured` — sets freezeCaptured + freezeLocalPath
- `pr-verified` — sets prUrl
- `run-finished` — → `done`, sets finishedAt
- `run-failed` — → `failed`, sets finishedAt + failureReason

Fire-and-forget from the scripts; backend is last-writer/fold, tolerant of missing/duplicate/out-of-order events.

### Dispatch not designed out

`runId` as an opaque host-generated key and a discrete-event model both leave room for a future "dispatch" origin (a run created by the UI/backend rather than the host) without reshaping the model.
