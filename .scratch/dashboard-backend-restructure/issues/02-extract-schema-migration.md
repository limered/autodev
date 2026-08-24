# 02 — Extract schema migration into RunsSchema.EnsureAsync

**What to build:** The boot-time schema migration stops living inline in `Program.cs`. The idempotent `CREATE TABLE IF NOT EXISTS runs` / `CREATE INDEX IF NOT EXISTS` DDL moves into a `RunsSchema.EnsureAsync(NpgsqlDataSource)` static in the `Runs/` folder, called once from bootstrap. Behavior is identical — still raw idempotent DDL run at startup, still fails the boot (exit 1) if it can't connect or ensure the schema. No migrations framework.

**Blocked by:** 01 — Move files into Runs/ feature folder.

**Status:** ready-for-agent

- [ ] `Runs/RunsSchema.cs` exposes `EnsureAsync(NpgsqlDataSource)` containing the table + index DDL.
- [ ] `Program.cs` calls `RunsSchema.EnsureAsync` at boot instead of holding inline SQL; the connectivity check + fail-fast (exit code 1, critical log) behavior is preserved.
- [ ] No migrations framework or new dependency introduced.
- [ ] `dotnet test dashboard/src/Api.Tests` stays green.
