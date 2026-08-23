# 01 — Scaffold dashboard service + Render Blueprint (manual DB)

**What to build:** A `dashboard/` folder at the repo root containing a single same-origin service — ASP.NET Core that serves both the API and a built Vue SPA — plus the Docker and Render config to deploy it. After this ticket the service deploys to Render on a Starter web instance, serves an (empty) page at `/`, and successfully connects to a Postgres database. This is prefactor scaffolding: no run data yet, just a live, DB-connected shell for later slices to land in.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] All dashboard code lives under a root `dashboard/` folder, separate from the host scripts.
- [ ] Multi-stage `Dockerfile`: a Node stage builds the Vue app to `dist/`; a .NET aspnet stage runs the published app and serves `dist/` as static files with SPA fallback. App binds `0.0.0.0:$PORT`.
- [ ] ASP.NET Core serves the Vue SPA at `/` (same origin — no CORS).
- [ ] A committed `render.yaml` Blueprint defines one `type: web`, `runtime: docker`, `plan: starter` service with a health check.
- [ ] Postgres is **created manually** on Render (not by the Blueprint); its connection string is supplied to the service via an env var (`ConnectionStrings__Runs`).
- [ ] The deployed service starts, serves the page, and opens a working DB connection at boot.
- [ ] `FACTORY_TOKEN` is defined as a secret env var (`sync: false`) for later use.
