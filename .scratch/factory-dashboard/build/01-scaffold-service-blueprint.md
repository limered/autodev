# 01 — Scaffold dashboard service + Render Blueprint (manual DB)

**What to build:** A `dashboard/` folder at the repo root containing a single same-origin service — ASP.NET Core that serves both the API and a built Vue SPA — plus the Docker and Render config to deploy it. After this ticket the service deploys to Render, serves an (empty) page at `/`, and successfully connects to a Postgres database. This is prefactor scaffolding: no run data yet, just a live, DB-connected shell for later slices to land in.

> **Deployment note (as built):** the `render.yaml` Blueprint conflicted with Render's Root-Directory handling and was removed; the service is configured **manually in the Render UI** (Root Directory `dashboard`, Dockerfile `Dockerfile`, health check `/health`). The web service runs on the **free tier** — all state lives in the separate paid Postgres, so a web-service purge/restart loses no data; Starter is optional only to avoid cold-start latency dropping the first event after idle. Postgres is created manually.

**Blocked by:** None — can start immediately.

**Status:** done

- [x] All dashboard code lives under a root `dashboard/` folder, separate from the host scripts.
- [x] Multi-stage `Dockerfile`: a Node stage builds the Vue app to `dist/`; a .NET aspnet stage runs the published app and serves `dist/` as static files with SPA fallback. App binds `0.0.0.0:$PORT`.
- [x] ASP.NET Core serves the Vue SPA at `/` (same origin — no CORS).
- [x] The service is deployable to Render via manual UI config (Root Directory `dashboard`) with a `/health` health check. (Blueprint approach dropped — conflicted with Root-Directory path handling.)
- [x] Postgres is **created manually** on Render; its connection string is supplied to the service via an env var (`ConnectionStrings__Runs`).
- [x] The deployed service starts, serves the page, and opens a working DB connection at boot.
- [x] `FACTORY_TOKEN` is defined as an env var for later use.
