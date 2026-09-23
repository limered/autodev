# Factory Dashboard

Single same-origin service: ASP.NET Core (net8.0) serves both the API and a built Vue 3 (Vite) SPA. Deployed to Render as one Docker web service on the Starter plan, connecting to a manually-created Postgres.

## Layout

```
dashboard/
  Dockerfile          multi-stage: Node builds SPA -> dist/, .NET publishes, dist/ copied to wwwroot
  README.md
  src/
    Api/              ASP.NET Core web app (serves API + static SPA)
    web/              Vue 3 + Vite SPA
```

`render.yaml` lived at the **repo root** when the Blueprint existed (since removed; the service is configured **manually in the Render UI** with Root Directory empty/repo root and Dockerfile Path `dashboard/Dockerfile`, so the repo-root `agents.json` factory catalog ships into the image).

## What this ticket delivers (scaffolding only)

- Binds `0.0.0.0` on `$PORT` (default 10000) via `ASPNETCORE_URLS`.
- Serves the built Vue `dist/` as static files with SPA fallback to `index.html`.
- Health check at **`/health`** (returns `{ "status": "ok" }`) — used as the Render health check path.
- Opens a Postgres connection at boot via `ConnectionStrings__Runs` (Npgsql). Verifies the connection only; **no schema creation** (that's ticket 02). Fails fast with a clear log if the connection string is absent or unreachable.
- `FACTORY_TOKEN` is read at startup but not yet used.

Deliberately deferred to later tickets: the `runs` table + migrations, the ingest endpoint, event handling, and the run table UI / polling.

## Env vars (set on Render)

| Var | How | Notes |
|-----|-----|-------|
| `ASPNETCORE_URLS` | value (in Blueprint) | `http://0.0.0.0:10000` |
| `ConnectionStrings__Runs` | **manual** (`sync: false`) | Npgsql connection string of the manually-created Postgres |
| `FACTORY_TOKEN` | **manual** (`sync: false`) | shared secret for later ingest auth |

## Manual Postgres steps (Render)

1. In the Render dashboard, create a **basic (paid) Postgres** instance manually (not via the Blueprint).
2. Copy its **internal connection string** (Npgsql/ADO.NET format, e.g. `Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true`).
3. Create the Blueprint from this repo; on first apply, set `ConnectionStrings__Runs` to that connection string and `FACTORY_TOKEN` to a generated secret.

## Local development

```powershell
# frontend
cd dashboard/src/web ; npm ci ; npm run build   # produces dist/

# backend (needs a reachable Postgres)
cd dashboard/src/Api
$env:ConnectionStrings__Runs = "Host=localhost;Database=runs;Username=postgres;Password=postgres"
dotnet run
```
