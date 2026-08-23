# Specify the Render deployment and config

`wayfinder:grilling`

**Status:** closed

**Assignee:** driver (claimed)

**Blocked by:** Choose the backend state storage; Design the ingest API contract; Specify host-side reporting integration.

## Question

Pull the pieces into a deployment spec: how the .NET API and Vue static site are deployed on Render (render.yaml / Docker / build commands), how the frontend is pointed at the API (CORS/origin), what env vars and secrets each service needs (shared token, DB connection, API base url), and the manual steps left to a human (creating the Render services, setting secrets). This is the final assembly ticket — after it, the way to a buildable spec is clear.

## Resolution

### Repo location

All dashboard code (the .NET service, the Vue frontend, the `Dockerfile`, and `render.yaml`) lives under a **`dashboard/`** folder at the repo root — kept separate from the existing host scripts (`start-job.ps1`, `watch-heartbeat.ps1`, `infrastructure/`). The Blueprint's `dockerfilePath`/build context roots at `dashboard/`.

### Topology — single same-origin Docker service

One Render **Docker web service** on the **free tier (Starter optional)**:

- ASP.NET Core serves the API under `/runs...` **and** serves the built Vue `dist/` as static files with SPA fallback (`UseStaticFiles` + fallback to `index.html`).
- **No CORS** (same origin), **one URL**, one service to operate. The `/runs` read endpoints and the built SPA share the origin.
- Multi-stage `Dockerfile`: stage 1 `node` builds the Vue app (`npm ci && npm run build` → `dist/`); stage 2 `mcr.microsoft.com/dotnet/aspnet` runs the published .NET app and includes the `dist/` as `wwwroot`. App binds `0.0.0.0:$PORT` (default 10000) via `ASPNETCORE_URLS`.

### Deployment — manual Render UI config

> **Note (superseded during build):** the original plan was a committed `render.yaml` Blueprint, but it conflicted with Render's Root-Directory handling (the `dashboard/` path doubled). During ticket 01 the Blueprint was removed and the service is configured **manually in the Render UI**. Postgres is also created **manually** (not from a `databases:` block).

Web Service (Docker) settings:

- **Root Directory:** `dashboard` (roots the build context inside `dashboard/` so the Dockerfile's `COPY src/...` resolves).
- **Dockerfile Path:** `Dockerfile` (relative to Root Directory).
- **Instance Type:** free tier (Starter optional — see storage decision).
- **Health Check Path:** `/health`.
- Env vars set in the UI:
  - `ASPNETCORE_URLS = http://0.0.0.0:10000`
  - `ConnectionStrings__Runs` = Npgsql-format connection string of the manually-created Postgres (`Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true`)
  - `FACTORY_TOKEN` = generated secret — the value the ingest endpoint checks `X-Factory-Token` against.

### Manual steps left to a human

1. Create a paid **basic Postgres** on Render manually; copy its connection string (convert the `postgresql://` URL to Npgsql key/value form).
2. Create the Web Service from the repo with the settings above; set `ConnectionStrings__Runs` and a generated `FACTORY_TOKEN`.
3. Copy that same token and the service URL (`https://<name>.onrender.com`) to the **host** `.secrets/factory-dashboard-token.txt` and `.secrets/factory-dashboard-url.txt` (per the host-reporting decision).
4. Nothing else — outbound POSTs from the NAT host need no inbound/port config.

### Build-side note

- DB migration: the single `runs` table (schema in the storage ticket) is created via EF Core migration or an idempotent startup script on boot (ticket 02).
- This is the final assembly decision; the way to a buildable spec is now clear.
