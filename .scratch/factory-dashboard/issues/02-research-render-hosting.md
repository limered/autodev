# Research Render hosting for .NET API + Vue static frontend

`wayfinder:research`

**Status:** closed

**Blocked by:** none — frontier.

## Resolution

- **.NET = Docker only** on Render (no native runtime); own a `Dockerfile`, listen on `0.0.0.0:$PORT` (default 10000).
- **Free web service spins down after 15 min idle** → ~1 min cold start on next request; **in-memory state is lost** on spin-down/restart/redeploy. First host POST after idle may be slow/time out → client must tolerate/retry.
- **Free Postgres expires in 30 days, no backups** → unusable to keep; durable option is paid `basic-256mb` Postgres (or SQLite on a paid persistent disk, which disables zero-downtime deploys). Free tier = 512MB/0.1CPU.
- **Vue static site** is free (CDN, TLS); needs SPA rewrite `/*`→`/index.html`. Two API-wiring options: **separate services (needs CORS)** or **same-origin single Docker service serving `dist`** (no CORS). Same-origin simplifies the deploy.
- **Secrets/env** via Dashboard, env groups, or `render.yaml` Blueprint (`sync:false` for secrets, `fromDatabase` for DB conn). Blueprint can define web+static+Postgres+routes together.
- **Outbound POST from NAT host is trivial** — public `https://<name>.onrender.com`, no inbound ports/config on host side.

## Question

What does Render offer for hosting a .NET (ASP.NET Core) web service and a Vue static site, and what constraints shape the spec? Surface: supported .NET deployment paths (Docker vs native), free-tier limits and cold-start/spin-down behaviour, how a static Vue site is hosted and pointed at the API (CORS, same-origin options), managed Postgres availability/pricing, how secrets/env vars are set, and outbound-only reachability (host behind NAT posting in). Findings feed the storage, deployment, and API-contract tickets.
