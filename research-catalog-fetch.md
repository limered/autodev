# Research: fetching target-repo catalogs with the backend PAT

Context pointer: wayfinder research ticket
[limered/autodev#233](https://github.com/limered/autodev/issues/233),
child of map [limered/autodev#231](https://github.com/limered/autodev/issues/231).
Schema already locked in
[#232](https://github.com/limered/autodev/issues/232):
`workflows` = ordered stage-id lists + top-level `defaultWorkflow` marker,
legacy `stages`-only file reads as `default`.

Question answered: how can the backend (which holds a GitHub PAT) fetch,
cache, and revalidate a target repo's `agents.json` at Issue-sync time and
again at claim/start time.

No code lands in this effort (map is planning-only). This file is the whole
finding; the issue gets a link, not a paste.

## 1. Raw-file endpoint: use the Contents API, not raw.githubusercontent

- `GET /repos/{owner}/{repo}/contents/{path}` (`path` = `agents.json`,
  optional `?ref=<branch>`; default = default branch). Source:
  [REST API endpoints for repository contents](https://docs.github.com/en/rest/repos/contents?apiVersion=2022-11-28#get-repository-content).
- Two response shapes from the same endpoint:
  - Default (`Accept: application/vnd.github+json`): JSON envelope with
    `content` (base64), `encoding`, `size`, `sha` (blob sha of the file),
    `download_url`, `_links`. The `sha` is the cheap identity token.
  - Raw (`Accept: application/vnd.github.raw+json`): raw file bytes. Use
    this for the body actually executed/validated; use the JSON shape (or
    its headers) for the `sha`.
  - Files 1–100 MB support raw/object only with `content` emptied; files
    >100 MB are unsupported by this endpoint. `agents.json` is bytes, so
    this is a non-constraint, but validation should cap size anyway.
- `download_url`s expire and are single-use; re-resolve via the Contents
  API per download rather than storing them. Source: same contents doc,
  "Download URLs expire" note.
- Follow redirects: the API uses 302/307 temporary redirects (e.g. archive
  endpoints, `download_url`); treat redirect as normal, repeat the request
  at `Location` without rewriting stored code. Source:
  [Best practices – Follow redirects](https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api).
- Why not `raw.githubusercontent.com`: it needs a token-in-header that the
  existing `HttpClient` (`dashboard/src/Api/Program.cs`, base
  `https://api.github.com/`, `Accept: application/vnd.github.v3+json`,
  `User-Agent: slop-factory-api`) does not speak uniformly, it returns no
  `sha`/`etag` identity, and supports no conditional revalidation. The
  Contents API reuses the PAT plumbing the backend already owns
  (`ReadGitHubPat` from `GITHUB_PAT` env / `/etc/secrets/github-pat.txt` /
  `.secrets/github-pat.txt`).
- Requests without a valid `User-Agent` are rejected; the backend already
  sends one — keep it on the new client. Source:
  [Troubleshooting – User agent required](https://docs.github.com/en/rest/using-the-rest-api/troubleshooting-the-rest-api).

## 2. Auth scopes (minimal)

- Classic PAT: `(no scope)` reads public repos only; `repo` grants full
  read/write to public + private code; `public_repo` limits that to public.
  Source:
  [Scopes for OAuth apps](https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/scopes-for-oauth-apps#available-scopes)
  (classic PAT scopes share this table).
- Fine-grained PAT: repository permission `Contents` = `read` is the
  content-fetch permission (`X-Accepted-GitHub-Permissions: contents=read`
  pattern). Source:
  [Troubleshooting – Resource not accessible](https://docs.github.com/en/rest/using-the-rest-api/troubleshooting-the-rest-api)
  plus the fine-grained permission model in
  [Managing your PATs](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens).
  Grant per target repo; `Metadata: read` rides along implicitly.
- Fine-grained limits that matter here: one token is bound to a single
  resource owner, optionally to selected repos; it cannot span multiple
  orgs at once, and outside/cross-org collaborator scenarios still need
  classic. Source: same "Managing your PATs" doc, fine-grained
  limitations section. For the factory (few known target repos under one
  owner) fine-grained `Contents:read + Issues:read/write` per repo is the
  least-privilege fit; the existing backend PAT already needs Issues
  write (label removal + close in `GitHubIssuesClient.cs`), so catalog
  fetch adds only `Contents:read` on the same repos.
- The backend must send `Authorization: Bearer <PAT>` (already does in
  `Program.cs`); conditional revalidation only earns the free-304 benefit
  when correctly authorized. Source: best-practices "Use conditional
  requests" section.

## 3. Private-repo and missing-file behaviour

- Private repo without access (bad/missing token, out-of-scope PAT,
  SSO-blocked) returns **`404 Not Found`, not 403** — deliberate, to avoid
  confirming private existence. Source:
  [Troubleshooting – 404 for an existing resource](https://docs.github.com/en/rest/using-the-rest-api/troubleshooting-the-rest-api).
- Same 404 for: repo genuinely absent, `agents.json` absent in an
  accessible repo, typo'd URL/trailing slash, wrong HTTP method. So the
  backend **cannot distinguish "no file" from "no access" on status alone**.
- Consequence (matches map standing preference "factory = fallback when
  absent/unreadable"): treat 404 as *catalog-unavailable → factory fallback*,
  never as a hard sync failure. Log `repo + ref` only (no token), surface a
  "using factory default" badge in the picker. Reserve hard errors for 401
  (bad credentials), 403-with-rate-limit-headers (see §4), and 422
  (validation).
- True 403s to expect: rate-limit exceeded (`x-ratelimit-remaining: 0`),
  secondary-limit abuse, or org PAT-policy blocks classic tokens (orgs can
  block classic; then the call 403s — switch that repo to fine-grained or a
  GitHub App). Source: PAT doc "Organization owners can restrict classic".
- Do not hot-loop a 404: "repeatedly requesting a missing resource wastes
  your rate limit and can trigger a secondary rate limit" — back off
  missing catalogs to the slow sync cadence, recheck only when there is
  reason (new commit, manual resync). Source:
  [Best practices – Do not ignore errors](https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api).

## 4. Rate limits (numbers the picker/runner tickets can budget)

- Authenticated PAT: **5,000 req/hour** (GHEC app-owned callers 15,000;
  `GITHUB_TOKEN` 1,000/repo/hr — not our path). Unauthenticated: 60/hr.
  Source:
  [Rate limits for the REST API](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api).
- Secondary: ≤100 concurrent; ≤900 REST points/min (≈1 GET = 1 pt);
  ≤90 CPU-s/min; content-creation bursts capped. Make catalog calls
  **serially** (the dispatch loop already is) and pause ≥1s between
  mutative calls. Source: same rate-limit doc + best-practices "Avoid
  concurrent requests / Pause between mutative requests".
- Budget: today's dispatch cadence is slow-sync 60 s + fast-claim 5 s
  (`dispatch-client.ps1`: `syncIntervalSeconds` default 60,
  `claimIntervalSeconds` 5; sync = `PUT /issues/{repo}` full-replace via
  `IssuesStore.SyncRepo`). Adding one Contents GET per repo per sync plus
  one conditional GET per claim is ~2 req/repo/min worst case — noise
  against 5,000/hr even with dozens of repos.
- Headers `x-ratelimit-limit/remaining/used/reset/resource` are
  authoritative; `GET /rate_limit` is free (does not count) for periodic
  overview. On 403/429: honor `retry-after` if present, else wait until
  `x-ratelimit-reset`, else ≥60 s + exponential backoff, then throw after N
  retries; never keep hammering while limited (risk of integration ban).
  Source: rate-limit doc "Checking the status / Exceeding the rate limit".

## 5. Cache location and invalidation (fits existing conventions)

- Convention in this repo: durable backend state lives in Postgres, schema
  created idempotently at boot (`RunsSchema/IssuesSchema/QueueSchema/
  HostSchema.EnsureAsync`, called from `Program.cs`), rows read/written
  through a `*Store` class (`IssuesStore.SyncRepo`,
  `QueueStore.ClaimNext`). Follow it: new table, e.g. `repo_catalogs`,
  plus `CatalogSchema.EnsureAsync` wired next to the others.
- Suggested row (narrow): `repo TEXT PRIMARY KEY, sha TEXT, etag TEXT,
  fetched_at TIMESTAMPTZ NOT NULL DEFAULT now(), body TEXT (raw file),
  error TEXT (last fetch failure), ref TEXT (branch pinned, default
  'default-branch')`. JSONB works too, but TEXT keeps the diff/validation
  story identical to how `IssuesStore` upserts snapshots.
- Write path (Issue-sync time): the dispatcher already syncs each
  configured repo's `ready-for-agent` issues on the slow cadence
  (`Get-GitHubIssues` → `Sync-IssuesToBackend` → `PUT /issues/{repo}`).
  Piggyback there: after issues sync, the **backend** (PAT holder — never
  the browser) GETs `contents/agents.json?ref=<default-or-pinned>` for that
  repo, validates against the #232 schema (fail-fast: `workflows`
  non-empty, known stage ids, no dupes, `defaultWorkflow` present when
  `workflows` present, legacy `stages`-only synthesizes `default`), and
  upserts the row. Invalid/unreadable → store `error`, serve factory
  fallback to the picker.
- Invalidation: overwrite per sync (TTL = sync interval, default 60 s);
  delete or mark-stale when the repo's issue snapshot empties or the repo
  leaves config. Missing-file 404s back off to slow cadence only (§3).
- Why server-side, not browser fetch: private repos need the PAT, and the
  PAT must never reach the browser. The picker reads the cached catalog
  from the backend (`GET /issues`-style joined read); the runner receives
  the frozen catalog inside the claim payload (see §6).

## 6. "Still valid" check at claim/start (cheap)

- Mechanism: **conditional GET with `If-None-Match: <stored etag>`**
  (or `If-Modified-Since` from stored `last-modified`). Unchanged →
  **`304 Not Modified`, which does not count against the primary rate
  limit when correctly authorized**. Source: best-practices "Use
  conditional requests" (explicit: "Making a conditional request does not
  count against your primary rate limit if a 304 response is returned and
  the request was made while correctly authorized").
- Keep the revalidation request byte-stable (same `?ref`, same `Accept`)
  so 304s hit; varying params/filters yields a new etag. Source:
  best-practices "Make requests that can be cached".
- Flow at `POST /queue/claim-next` (today: atomic rank-order claim,
  `FOR UPDATE … SKIP LOCKED` in `QueueStore.ClaimNext`, payload derived by
  `IssueClaimRules.PayloadFor` = repo URL + spec): before stamping
  `run_id`, revalidate the pick — send conditional GET for that repo's
  `agents.json`, compare returned/current `sha` (or 304 = equal) against
  the `sha` frozen with the picker's selection. Match → freeze
  `(workflow, catalogSha)` into the claim payload (runner seeds stage
  lights from the claimed workflow only, per map). Mismatch/200-with-new-
  sha → **fail the claim, refresh the cache row, surface resync-on-retry**
  (exactly the map's "stale workflow pick fails the claim and resyncs on
  retry"). Missing (404) → factory fallback, claim proceeds on `default`.
- Fallback if etags are ever absent: compare the JSON envelope's `sha`
  field directly (one unconditional GET, 1 rate-limit point, only on the
  claim path — still cheap). `sha` equality is the identity; body bytes
  never need diffing.
- Runner double-check at start (`start-job.ps1` already copies
  `agents.json` to `/tmp/agents.json` for the VM): the claim payload's
  `(workflow, catalogSha)` travels with the job; `start-job` asserts the
  checked-out repo's file still parses to the same `sha`/selection before
  first phase, else aborts to resync. No extra GitHub call on the hot path
  unless the checkout disagrees.

## 7. Survey: current `agents.json` shapes in the wild (here: one)

- Exactly one `agents.json` exists in the factory repo (repo root). No
  target repo has one yet, so every Issue today runs the factory catalog —
  the map's "factory = fallback" is already the de-facto behavior.
- Factory shape (legacy, pre-`workflows`): five stages in map order —
  `implementation` (sequential: feature-builder, test-runner),
  `quality-loop` (loop ×3: static-analysis, feature-builder),
  `test-rerun` (sequential: test-runner), `agentic-review` (sequential:
  agentic-review), `pr-author` (sequential: pr-author). Owned by
  `lib/AgentsConfig.ps1` per ADR 003; seeded one stage per category in map
  order (`ConvertTo-SeededStages`); slot rule in `Get-StepCategory`;
  relay expansion in `Get-PhaseStepCandidates`; VM consumes
  `/tmp/agents.json` (`infrastructure/multipass/test-feature-builder.sh`
  runs 6 phases — the expanded loop count).
- Per #232 this exact file must keep reading as the `default` workflow
  unchanged (all stages, map order); `workflows`-bearing files add the
  named ordered-subset layer with strict stage-id references, no inline
  redefinition, no per-workflow stage overrides, gates generic.
- Grounding for the spec: the picker lists `default` (+ named workflows
  when present) per queue row, frozen at claim; `RunView` seeds lights
  from the claimed workflow only; `start-job.ps1` + `AgentsConfig.ps1`
  stay the shape owners — the backend cache is a validated copy, never a
  second schema.

## 8. Recommended seams (for the picker/runner spec, not this ticket)

1. `GET /repos/{owner}/{repo}/contents/agents.json` (+ raw accept) as the
   sole fetch; store `(sha, etag, body, error)`.
2. Sync-time fetch in the backend Issue-sync path; picker reads cache.
3. Claim-time conditional revalidation (`If-None-Match` → 304 = still
   valid); `sha` compare as fallback; stale → fail claim + resync.
4. `repo_catalogs` Postgres table + `CatalogSchema.EnsureAsync`, mirroring
   `IssuesSchema`/`QueueSchema`.
5. 404 → factory fallback + badge; 403/429 → honor `retry-after`/
   `x-ratelimit-reset` + backoff; never PAT-in-browser.

## Sources (primary, in citation order)

- Get repository content (endpoint, raw/object media types, `sha`,
  size limits, expiring `download_url`, 200/302/304/403/404):
  https://docs.github.com/en/rest/repos/contents?apiVersion=2022-11-28#get-repository-content
- Rate limits (5,000/hr PAT, 60/hr unauth, secondary caps, headers,
  `GET /rate_limit`, exceed/backoff rules):
  https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api
- Scopes (classic `(no scope)`/ `repo` / `public_repo`):
  https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/scopes-for-oauth-apps#available-scopes
- PAT types (fine-grained single-owner/selected-repos, `Contents`
  permission model, classic org-policy blocks):
  https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens
- Best practices (conditional `etag`/`If-None-Match` 304 free when
  authorized, stable requests for 304s, serial requests, don't hot-loop
  404s, follow redirects):
  https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api
- Troubleshooting (private-resource 404-vs-403, auth checklist,
  `User-Agent` required, `X-Accepted-GitHub-Permissions`):
  https://docs.github.com/en/rest/using-the-rest-api/troubleshooting-the-rest-api
- Repo-local (read before outward research per map Notes):
  `CONTEXT.md`, `docs/glossary.md`, `docs/adr/003-pipeline-shape.md`,
  `lib/AgentsConfig.ps1`, `agents.json`,
  `dashboard/src/Api/Program.cs` (PAT load + HttpClient),
  `dashboard/src/Api/Issues/GitHubIssuesClient.cs`,
  `dashboard/src/Api/Issues/IssuesStore.cs` +
  `IssuesSchema.cs` + `IssuesEndpoints.cs`,
  `dashboard/src/Api/Queue/QueueStore.cs` + `QueueEndpoints.cs`,
  `dispatch-client.ps1` (60 s sync / 5 s claim cadence),
  `start-job.ps1` (`/tmp/agents.json` handoff).
