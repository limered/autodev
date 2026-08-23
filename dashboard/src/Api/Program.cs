using System.Text.Json;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Runs");
var factoryToken = builder.Configuration["FACTORY_TOKEN"];

var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

var app = builder.Build();

// Fail fast if the DB connection string is missing.
if (string.IsNullOrWhiteSpace(connectionString))
{
    app.Logger.LogCritical("ConnectionStrings__Runs is not set. Set the Postgres connection string env var. Exiting.");
    return 1;
}

var dataSource = NpgsqlDataSource.Create(connectionString);

// Verify the DB connection and create the schema idempotently at boot.
try
{
    await using var conn = await dataSource.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        """
        CREATE TABLE IF NOT EXISTS runs (
            run_id            uuid        PRIMARY KEY,
            repo              text        NOT NULL,
            branch            text        NOT NULL,
            spec              text        NOT NULL,
            model             text        NOT NULL,
            vm_name           text,
            status            text        NOT NULL,
            started_at        timestamptz NOT NULL,
            finished_at       timestamptz,
            last_heartbeat_at timestamptz,
            pr_url            text,
            failure_reason    text,
            freeze_captured   boolean     NOT NULL DEFAULT false,
            freeze_local_path text,
            updated_at        timestamptz NOT NULL DEFAULT now()
        );
        CREATE INDEX IF NOT EXISTS runs_status_started_idx ON runs (status, started_at DESC);
        """, conn);
    await cmd.ExecuteNonQueryAsync();
    app.Logger.LogInformation("Postgres connection opened and schema ensured.");
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Failed to connect to Postgres or ensure schema at boot. Exiting.");
    return 1;
}

// --- Ingest: one event per call (ticket 02 handles run-started only) ---
app.MapPost("/runs/{runId:guid}/events", async (Guid runId, HttpRequest req) =>
{
    // Shared-secret auth on writes only.
    if (string.IsNullOrEmpty(factoryToken) ||
        req.Headers["X-Factory-Token"].ToString() != factoryToken)
    {
        return Results.Unauthorized();
    }

    RunEvent? ev;
    try
    {
        ev = await JsonSerializer.DeserializeAsync<RunEvent>(req.Body, jsonOpts);
    }
    catch (JsonException)
    {
        return Results.BadRequest();
    }
    if (ev is null || string.IsNullOrWhiteSpace(ev.Type))
    {
        return Results.BadRequest();
    }

    var at = ev.At ?? DateTimeOffset.UtcNow;

    switch (ev.Type)
    {
        case "run-started":
            // Upsert the row in launching status. Idempotent on run_id.
            await using (var conn = await dataSource.OpenConnectionAsync())
            await using (var cmd = new NpgsqlCommand(
                """
                INSERT INTO runs (run_id, repo, branch, spec, model, status, started_at, updated_at)
                VALUES (@id, @repo, @branch, @spec, @model, 'launching', @at, @at)
                ON CONFLICT (run_id) DO NOTHING;
                """, conn))
            {
                cmd.Parameters.AddWithValue("id", runId);
                cmd.Parameters.AddWithValue("repo", ev.Repo ?? "");
                cmd.Parameters.AddWithValue("branch", ev.Branch ?? "");
                cmd.Parameters.AddWithValue("spec", ev.Spec ?? "");
                cmd.Parameters.AddWithValue("model", ev.Model ?? "");
                cmd.Parameters.AddWithValue("at", at);
                await cmd.ExecuteNonQueryAsync();
            }
            break;

        case "agent-started":
            // → running, set vmName. Timestamp-guarded: only if newer than the
            // last applied change, so a stale retry can't move it backwards.
            await using (var conn = await dataSource.OpenConnectionAsync())
            await using (var cmd = new NpgsqlCommand(
                """
                UPDATE runs
                SET status = 'running', vm_name = @vm, updated_at = @at
                WHERE run_id = @id AND @at > updated_at;
                """, conn))
            {
                cmd.Parameters.AddWithValue("id", runId);
                cmd.Parameters.AddWithValue("vm", (object?)ev.VmName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("at", at);
                await cmd.ExecuteNonQueryAsync();
            }
            break;

        case "heartbeat":
            // Update liveness only; guarded against its own last value so an
            // out-of-order/duplicate heartbeat is a no-op. Does not touch status.
            await using (var conn = await dataSource.OpenConnectionAsync())
            await using (var cmd = new NpgsqlCommand(
                """
                UPDATE runs
                SET last_heartbeat_at = @at
                WHERE run_id = @id
                  AND (last_heartbeat_at IS NULL OR @at > last_heartbeat_at);
                """, conn))
            {
                cmd.Parameters.AddWithValue("id", runId);
                cmd.Parameters.AddWithValue("at", at);
                await cmd.ExecuteNonQueryAsync();
            }
            break;

        default:
            // Unknown/not-yet-implemented event types are accepted and ignored
            // (later tickets add fold logic). Keeps the host fire-and-forget.
            break;
    }

    return Results.Accepted();
});

// --- Read: all runs, newest first (public) ---
app.MapGet("/runs", async () =>
{
    var runs = new List<Run>();
    await using var conn = await dataSource.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        "SELECT run_id, repo, branch, spec, model, vm_name, status, started_at, finished_at, last_heartbeat_at, pr_url, failure_reason, freeze_captured, freeze_local_path, updated_at FROM runs ORDER BY started_at DESC",
        conn);
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        runs.Add(new Run(
            r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5),
            r.GetString(6),
            r.GetFieldValue<DateTimeOffset>(7),
            r.IsDBNull(8) ? null : r.GetFieldValue<DateTimeOffset>(8),
            r.IsDBNull(9) ? null : r.GetFieldValue<DateTimeOffset>(9),
            r.IsDBNull(10) ? null : r.GetString(10),
            r.IsDBNull(11) ? null : r.GetString(11),
            r.GetBoolean(12),
            r.IsDBNull(13) ? null : r.GetString(13),
            r.GetFieldValue<DateTimeOffset>(14)));
    }
    return Results.Json(runs, jsonOpts);
});

// Serve the built Vue SPA (wwwroot) with SPA fallback to index.html.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapFallbackToFile("index.html");

app.Run();
return 0;

record RunEvent(
    string Type,
    DateTimeOffset? At,
    string? Repo,
    string? Branch,
    string? Spec,
    string? Model,
    string? VmName);

record Run(
    Guid RunId,
    string Repo,
    string Branch,
    string Spec,
    string Model,
    string? VmName,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? LastHeartbeatAt,
    string? PrUrl,
    string? FailureReason,
    bool FreezeCaptured,
    string? FreezeLocalPath,
    DateTimeOffset UpdatedAt);
