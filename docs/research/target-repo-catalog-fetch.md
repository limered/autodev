# Research: fetching target-repo catalogs with the backend PAT

For wayfinder ticket #233 (part of map #231).
Question: how can the backend (which holds a GitHub PAT) fetch, cache, and
revalidate a target repo's `agents.json` at Issue-sync time and again at
claim/start time?

## 1. Endpoint

- `GET /repos/{owner}/{repo}/contents/agents.json` returns the file's
  metadata plus content. Default JSON body carries `sha`, `size`, `path`,
  base64 `content`, and `download_url`.
  Source: `docs.github.com/en/rest/repos/contents#get-repository-content`
  (response schema: Content File with `sha`, `content`, `download_url`).
- `?ref=<branch|tag|sha>` pins the revision; omitted it defaults to the
  repo's default branch. Verified live: `limered/paper` and
  `limered/show-me-your-cards` both use `main` as default branch.
- `Accept: application/vnd.github.raw` returns raw bytes instead of the
  JSON envelope — verified live against `limered/autodev` (raw body is the
  525-byte `agents.json`). Use raw for the runner payload, JSON envelope
  when only the `sha` is needed.
- Conditional requests are supported: the endpoint documents
  `200 / 302 / 304 / 403 / 404` statuses, so `If-None-Match` revalidation
  is first-party behaviour, not a hack.
  Source: same contents doc, "HTTP response status codes".

## 2. Auth and scopes (backend already holds the PAT)

- The backend reads the PAT from `GITHUB_PAT` env, `/etc/secrets/github-pat.txt`,
  or `.secrets/github-pat.txt`, and attaches it as `Authorization: Bearer`
  on a shared `HttpClient` pointed at `https://api.github.com/`.
  Source: `dashboard/src/Api/Program.cs` (`ReadGitHubPat`, `AddHttpClient`).
  Today that client only closes issues (`Issues/GitHubIssuesClient.cs`);
  catalog fetch reuses the same client — no new secret plumbing.
- The dispatch client (host-side, `dispatch-client.ps1`) holds the same PAT
  from `.secrets/github-pat.txt` for the issues-list sync
  (`Get-GitHubIssues` → `PUT /issues/{repo}`). Catalog fetch belongs in the
  backend rather than the dispatch client so the claim/start re-check stays
  server-side and single-sourced.
- Scopes: read-only file access. Classic PAT needs `repo` scope for private
  repos (public repos readable even unauthenticated); fine-grained PAT needs
  repository `Contents: read-only`. The PAT already closes/issues-labels via
  the issues endpoints, so it necessarily carries at least issues write —
  verify `Contents: read` (or classic `repo`) is granted before the picker
  ticket lands, else private-repo catalogs 404/403.
  Source: `docs.github.com/en/rest/authentication/authenticating-to-the-rest-api`
  (token-in-`Authorization`-header model) and
  `.../permissions-required-for-fine-grained-personal-access-tokens`
  (per-endpoint permission table; `GET /repos/{owner}/{repo}/contents/{path}`
  is a Contents read).

## 3. Private-repo and missing-file behaviour

- Verified live (2026-09-21, backend PAT via `gh api`/`curl`):
  - `limered/autodev/contents/agents.json` → `200`, `sha
    6590d439f2b8e8e36aad0151d17ed6bf70523d9d`, `size 525`.
  - `limered/paper/contents/agents.json` → `404 {"message":"Not Found"}`
    (repo itself is public, `main` branch — the file is simply absent).
  - `limered/show-me-your-cards/contents/agents.json` → same `404`.
  - Raw-media-type fetch of the autodev file returns the JSON body verbatim.
- Rules for the picker/runner tickets:
  - `404` = absent OR private-without-access (GitHub deliberately does not
    distinguish); both map to the map's standing decision "factory catalog
    is the fallback when absent/unreadable".
  - `403` with `X-GitHub-SSO` header = SAML SSO authorization missing —
    re-authorize the PAT, do not treat as "no catalog".
  - `401` = invalid/revoked PAT — surface, do not silently fall back
    forever (else a dead PAT masquerades as "every repo has no catalog").
  - Oversize guard: files ≤1 MB return full content; 1–100 MB only via raw
    media type; >100 MB unsupported. `agents.json` is ~0.5 KB, so cap
    acceptance at a small limit (e.g. 256 KB) and reject larger as
    unreadable → fallback. Source: contents doc "If the requested file's
    size is…" note.

## 4. Rate limits and fetch cadence

- Authenticated REST: 5,000 req/hour; unauthenticated: 60 req/hour.
  Secondary: ≤100 concurrent, ≤900 points/min (plain GET = 1 point).
  Headers `x-ratelimit-{limit,remaining,used,reset,resource}` on every
  response are authoritative; `GET /rate_limit` is free.
  Source: `docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api`.
- Observed live: `X-RateLimit-Limit: 5000, Remaining: 4989` on contents GETs.
- Cost math: Issue-sync cadence is 60 s (`dispatch-client.config.json`
  `syncIntervalSeconds`) over 3 repos → ~180 catalog GETs/hour worst case,
  ~4% of the 5,000 budget, shared with the existing issues-list sync and
  issue-close calls. Claim/start re-checks add ~1 GET per claim. No limit
  pressure; still honor `x-ratelimit-remaining: 0` by backing off until
  `x-ratelimit-reset`, and `Retry-After` on 403/429 secondary-limit hits.

## 5. Cache location and invalidation (no catalog cache exists today)

- Current state: Postgres holds `issues` (`Issues/IssuesSchema.cs`,
  `Issues/IssuesStore.cs` upsert-per-sync), `queue` (`Queue/QueueSchema.cs`),
  `runs`, `host` — no catalog/sha column anywhere. Schema convention is one
  `*Schema.EnsureAsync` (`CREATE TABLE IF NOT EXISTS`) per domain plus a
  store behind an interface. Verified by reading `IssuesStore.cs`,
  `QueueStore.cs`, `IssuesSchema.cs`, `QueueSchema.cs`, `Program.cs`.
- Recommendation for the spec ticket: new `repo_catalogs` table following
  the same convention —
  `repo text PRIMARY KEY, sha text NOT NULL, body jsonb NOT NULL,
  etag text, fetched_at timestamptz NOT NULL DEFAULT now()`,
  owned by a small catalog store + `CatalogSchema.EnsureAsync` wired in
  `Program.cs` next to the existing four.
- Invalidation:
  - Write-through at Issue-sync time: each repo sync fetches `agents.json`
    (conditional GET, §6) and upserts the row; sync already runs per repo
    per 60 s slow cadence, so staleness is bounded by one sync interval.
  - Missing/unreadable → upsert a negative row (`sha = ''`, `body = NULL`,
    `fetched_at`) so "fallback to factory" is a cached decision, not an
    error path retried every picker render; refresh the negative row on the
    same cadence.
  - Claim payload freezes the picked `sha`; the queue row need not carry the
    body, only the `sha` it was picked against.

## 6. Cheap "still valid" check at claim/start

- Observed live: the contents response `ETag` equals the blob `sha`
  (`ETag: "6590d439…"` for `sha 6590d439…`). So the stored `sha` doubles as
  the validator — no extra version column needed.
- Two equivalent cheap checks (both 1 GET, no body on success):
  1. `GET …/contents/agents.json` with `If-None-Match: "<cached sha>"` →
     `304 Not Modified` means still valid; `200` carries the new `sha` +
     body. Counts against primary limit but transfers ~0 bytes on hit.
  2. Plain JSON-envelope GET and compare the `sha` field only (skip base64
     decode unless changed). Simpler client code; same request cost.
- Either runs at most once per claim (`QueueStore.ClaimNext`/
  `StartNext` path) and once per start-job handoff. On mismatch: fail the
  claim with "stale workflow pick", resync the catalog row, and let retry
  re-pick — exactly the map's standing "stale pick fails the claim and
  resyncs on retry" rule.

## 7. Survey: current `agents.json` shapes in known repos

Repos enumerated from `dispatch-client.config.json` (`limered/autodev`,
`limered/paper`, `limered/show-me-your-cards`).

- `limered/autodev` (only catalog in existence, `sha 6590d439…`, 525 bytes):
  single top-level `stages` map, 5 entries — `implementation` (sequential:
  feature-builder, test-runner), `quality-loop` (loop ×3: static-analysis,
  feature-builder), `test-rerun`, `agentic-review`, `pr-author`
  (all sequential, single agent). No `workflows` key, no `default` marker.
  Per the map's standing decision this file reads as the `default` workflow
  unchanged, so the schema ticket must accept a bare-`stages` file as
  `{ default: <stages> }`.
- `limered/paper` — no `agents.json` (404). Public, default branch `main`.
- `limered/show-me-your-cards` — no `agents.json` (404). Public, default
  branch `main`, "A simple Scrum Poker Game".
- Shape rules the schema ticket inherits (from `lib/AgentsConfig.ps1`,
  owner of pipeline shape per ADR 003): each stage needs non-empty id,
  `type ∈ {sequential, loop}` (`parallel` currently coerced to sequential
  in code but rejected per ADR 003 — flag the drift), ≥1 member agent,
  loops need a positive integer `iterations`, ≥1 stage total. A
  multi-workflow file must preserve these per workflow plus one `default`
  name; stage ids stay unique by map-key construction.

## 8. What the picker/runner tickets still need (not decided here)

- Exact `workflows`-file JSON shape (schema ticket owns it; §7 grounds it).
- Whether catalog fetch lives in the backend sync endpoint or a new
  backend background loop — either way behind the existing PAT `HttpClient`.
- PAT scope audit (confirm Contents read on the real backend PAT).
- Negative-cache TTL tuning and picker UX for "repo has no catalog".
