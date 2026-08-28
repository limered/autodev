# Research findings: Sync GitHub issues from watched repos to the backend

Ticket: `.scratch/remote-dispatch/issues/01-github-issue-sync.md`
Verified against docs.github.com REST issues docs (API version 2022-11-28), Aug 2026.

## Summary / recommendation

Poll `GET /repos/{owner}/{repo}/issues?labels=ready-for-agent&state=open&sort=updated&per_page=100`
per watched repo, on a **5-minute loop**, using a **fine-grained PAT** with read-only
Issues + Metadata, stored at `.secrets/github-pat.txt`. Filter out anything with a
`pull_request` key. Push a **full replacement snapshot per repo** each cycle; the
backend replaces its projection for that repo atomically. This is simpler than delta
tracking and correctly handles closes/relabels/deletes for free.

---

## 1. API surface: REST vs GraphQL

**Use REST, one call per repo:**

```
GET /repos/{owner}/{repo}/issues?labels=ready-for-agent&state=open&sort=updated&direction=desc&per_page=100
Accept: application/vnd.github+json
Authorization: Bearer <pat>
X-GitHub-Api-Version: 2022-11-28
```

- `labels` takes a comma-separated list; server-side filter, so we only pull eligible issues.
- `state=open` — closed/reopened is handled server-side, so a closed issue simply drops out of the result set. This is what makes full-replace trivial.
- One request per watched repo. For "a handful" of repos this is a handful of requests — no orchestration needed.

**Pull-request filtering (mandatory):** the issues endpoint returns PRs too. Every
returned object that is a PR has a `"pull_request"` key. Drop any item where
`pull_request` is present. Do this even though PRs are unlikely to carry
`ready-for-agent` — it's one predicate and protects against surprises.

**Pagination:** `per_page` max is 100 (default 30). Follow the `Link` response header
`rel="next"` until absent. In practice a `ready-for-agent` queue per repo will be well
under 100, so pagination is a safety net, not a hot path. Do NOT hand-build `page=N`
loops off a guessed count — follow `Link`.

**Why not GraphQL:** GraphQL `search` can query across repos in one call
(`is:issue is:open label:"ready-for-agent" repo:a/b repo:c/d`), but:
- Search results are eventually-consistent (indexing lag) and capped at 1000 results.
- GraphQL rate limiting is point-cost based and harder to reason about than REST's flat
  per-request budget.
- For a handful of repos the multi-repo win is negligible.
REST per-repo is more predictable and the response shape is directly the projection we want.

---

## 2. Token / scopes and location

**Public repos only:** a token with no scopes (or a fine-grained PAT with `Metadata: read`)
can read public issues. Even unauthenticated works but see rate limits (§3) — always auth.

**Private repos — recommended: fine-grained PAT**
- Permissions: **Issues: Read-only** and **Metadata: Read-only** (Metadata is a mandatory
  baseline dependency for almost every fine-grained permission).
- Scope the PAT to exactly the watched repos (resource owner + selected repositories).
  Least privilege — it cannot write, cannot touch code, cannot see unlisted repos.

**Classic PAT alternative:** the `repo` scope grants private-repo issue read — but `repo`
is all-or-nothing (full read/write to code, issues, everything, on every repo the user can
access). Avoid unless fine-grained PATs are unavailable for the target org. For public-only,
classic `public_repo` is narrower but still write-capable.

**Location:** follow the existing `.secrets/` convention — store at
`.secrets/github-pat.txt`, single line, no trailing newline handling assumptions (trim on
read). Matches the existing `.secrets/github-pat.txt` reference and sits alongside the
`X-Factory-Token` secret handling. `.secrets/` must remain gitignored. The client reads it
at startup and on each cycle (so rotation doesn't require a restart — cheap).

---

## 3. Rate limits and cadence

- Authenticated REST: **5,000 requests/hour** per user token (per PAT).
- Unauthenticated: 60/hour — unusable, hence always authenticate.
- Response carries `X-RateLimit-Remaining` / `X-RateLimit-Reset`; the loop should read these
  and back off if remaining gets low (defensive, will never trigger at this volume).

**Cadence:** with N repos and ≤ a couple pages each, a cycle costs ~N–2N requests.
A **5-minute poll** = 12 cycles/hr × (say 5 repos) ≈ 60 req/hr — ~1% of budget. Even a
1-minute loop is fine (~720/hr). Recommend **5 minutes** as the default: responsive enough
for a human-in-the-loop dispatch UI, trivially within limits, leaves headroom for growth
and for the outbound `factory-report` traffic.

Optional optimization (not needed now): `If-None-Match` with the previous `ETag` per repo
returns `304 Not Modified` and **does not count against the rate limit**. Skip until repo
count or poll frequency actually pressures the budget — YAGNI.

---

## 4. Minimal issue field set

The backend stores a projection row per eligible issue. Two consumers:

**(a) Render the "eligible issues" list (web frontend):**
| field | source | why |
|---|---|---|
| `repo` (owner/name) | request context / `repository.full_name` | grouping + identity |
| `number` | `number` | identity + display (#42) |
| `title` | `title` | list display |
| `html_url` | `html_url` | click-through to GitHub |
| `labels` | `labels[].name` (names only) | show/confirm eligibility, future filtering |
| `updated_at` | `updated_at` | sort/staleness, and delta key if ever needed |
| `state` | `state` | sanity/debug; always `open` given the query |

**(b) Hand a startable spec to `start-job.ps1` later:**
- `repo` + `number` + `body` are what a run actually needs. `start-job.ps1` clones/targets
  the repo and the agent needs the issue text (`body`) as the task spec. `html_url` is a
  useful human reference in run logs.

**Recommended stored set:**
`repo`, `number`, `title`, `html_url`, `labels` (string[] of names), `updated_at`, `body`,
plus GitHub's stable `id` (int64) as the natural primary key for the projection row.

**Notes:**
- Store `labels` as an array of names only — the full label objects (color, id, description)
  are noise for this use case.
- `body` can be large. It's needed for (b) but not for list rendering — acceptable to store
  it, or fetch on-demand per issue when queuing. Default: store it (one column, avoids a
  second GitHub round-trip at dispatch time). `body` may be `null` — handle it.
- Default media type returns raw markdown `body`, which is exactly what the agent wants as a
  spec. No custom `Accept` media type needed.

Everything else in the (very large) issue payload — reactions, milestone, sub-issues,
assignees, user, repository object — is not needed and should be dropped at the client
before pushing, to keep the outbound payload small.

---

## 5. Keeping the projection in sync — recommend FULL REPLACE per repo

**Recommendation: full-replace-per-repo snapshot each cycle.**

Each cycle, for each repo, the client fetches the complete current set of open
`ready-for-agent` issues and pushes that whole set. The backend, in one transaction,
replaces all projection rows for that repo with the pushed set (delete-not-present +
upsert-present, keyed on GitHub `id`).

**Why full replace:**
- **Closes, relabels, deletes, and transfers are handled for free.** An issue that was
  closed, had the label removed, or was deleted simply isn't in the new snapshot, so it's
  gone from the projection. A delta-by-`updated_at` approach (`since=` param) tells you what
  *changed* but a **label removal or delete does not reliably give you a tombstone** you can
  act on via the list endpoint — you'd have to separately detect disappearance, which
  reintroduces exactly the full-set comparison you were trying to avoid.
- The dataset is tiny (a per-repo agent queue), so re-sending the full set every 5 minutes is
  cheap on both wire and DB.
- Idempotent and self-healing: a missed or partial cycle fully corrects on the next cycle. No
  drift, no cursor state to persist or corrupt.

**Contract shape (for the API-contract ticket):**
- Endpoint (outbound from client, mirroring `factory-report.ps1` auth): e.g.
  `POST /api/repos/{owner}/{repo}/eligible-issues` with `X-Factory-Token` header.
- Body: `{ "repo": "owner/name", "syncedAt": "<iso>", "issues": [ { id, number, title, htmlUrl, labels, updatedAt, body } ] }`
- Semantics: **authoritative snapshot** — backend replaces the full set for that repo.
- Scope replacement to the repo in the request so one repo's push never touches another's rows.

**Delta (`since=updated_at`) — rejected** for the projection sync. It's a valid *bandwidth*
optimization but the tiny data volume means there's nothing to optimize, and it cannot express
deletions cleanly. If volume ever explodes, reach for ETag/`304` (§3) first — same benefit,
no deletion-detection problem.

---

## Open items for downstream tickets
- **client-daemon ticket:** the 5-min loop, per-repo fetch + PR filter + field projection,
  `.secrets/github-pat.txt` read, rate-limit header backoff, push per repo.
- **api-contract ticket:** the snapshot POST endpoint, `X-Factory-Token` auth, transactional
  full-replace-per-repo upsert keyed on GitHub `id`.
- Watched-repo config format (list of `owner/name`) — where it lives on the host (alongside
  existing config; not a secret).
