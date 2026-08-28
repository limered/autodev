# Sync GitHub issues from watched repos to the backend

`wayfinder:research`

## Question

How does the local client read GitHub issues labelled `ready-for-agent` from a locally-configured set of repos and push them to the Render backend?

Resolve:
- GitHub API surface to list issues by label across multiple repos (REST vs GraphQL), and pagination.
- What token/scopes the client needs to read issues (public vs private repos), and where that token lives on the host (`.secrets/`?).
- Rate limits and a reasonable sync cadence for a poll loop.
- What issue fields the backend needs (repo, number, title, url, labels, updated_at, body?) to render the "eligible issues" list and later hand a startable spec to `start-job.ps1`.
- How the client detects removed/closed/relabelled issues so the backend projection stays in sync (full replace per repo vs delta).

Output: a concrete sync design the client-daemon and API-contract tickets can build on.

## Resolution (closed)

Full findings: [research/01-github-issue-sync-findings.md](../research/01-github-issue-sync-findings.md) (on branch `research/github-issue-sync`).

- **API**: REST, one call per repo — `GET /repos/{owner}/{repo}/issues?labels=ready-for-agent&state=open&sort=updated&per_page=100`, follow `Link` `rel="next"`. Drop items with a `pull_request` key (PRs). GraphQL rejected.
- **Token**: fine-grained PAT (Issues: Read + Metadata: Read) scoped to watched repos; public repos need none. Store at `.secrets/github-pat.txt`, read each cycle.
- **Cadence**: 5-min loop over ~5 repos ≈ 60 req/hr vs 5000/hr budget. Watch `X-RateLimit-*`.
- **Fields**: `id` (int64 PK), `repo`, `number`, `title`, `html_url`, `labels` (names), `updated_at`, `body`, `state`.
- **Sync strategy**: full-replace-per-repo snapshot each cycle; backend does transactional delete-not-present + upsert keyed on GitHub `id`, scoped to the repo. Handles closes/relabels/deletes/transfers for free.
