using System.Text.Json;
using Api.Folding;
using Api.Models;
using Api.Store;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Runs");
var factoryToken = builder.Configuration["FACTORY_TOKEN"];

// Fail fast if the DB connection string is missing.
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("ConnectionStrings__Runs is not set. Set the Postgres connection string env var. Exiting.");
    return 1;
}

var dataSource = NpgsqlDataSource.Create(connectionString);
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<IRunStore, RunStore>();

var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

var app = builder.Build();

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
app.MapPost("/runs/{runId:guid}/events", async (Guid runId, HttpRequest req, IRunStore store) =>
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

    ev = ev with { RunId = runId };

    var current = await store.Get(runId);
    var next = RunFold.Apply(current, ev);
    if (next is not null)
    {
        await PersistRun(current, next);
    }

    return Results.Accepted();
});

async Task PersistRun(RunState? current, RunState next)
{
    if (current is null)
    {
        // run-started: insert the initial row idempotently.
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO runs (run_id, repo, branch, spec, model, status, started_at, updated_at)
            VALUES (@id, @repo, @branch, @spec, @model, @status, @startedAt, @updatedAt)
            ON CONFLICT (run_id) DO NOTHING;
            """, conn);
        cmd.Parameters.AddWithValue("id", next.RunId);
        cmd.Parameters.AddWithValue("repo", next.Repo);
        cmd.Parameters.AddWithValue("branch", next.Branch);
        cmd.Parameters.AddWithValue("spec", next.Spec);
        cmd.Parameters.AddWithValue("model", next.Model);
        cmd.Parameters.AddWithValue("status", next.Status);
        cmd.Parameters.AddWithValue("startedAt", next.StartedAt);
        cmd.Parameters.AddWithValue("updatedAt", next.UpdatedAt);
        await cmd.ExecuteNonQueryAsync();
        return;
    }

    // Heartbeat is special: it does not bump updated_at and must guard only against
    // its own previous value, so concurrent status updates are not clobbered.
    var onlyHeartbeatChanged =
        next.LastHeartbeatAt != current.LastHeartbeatAt &&
        next.UpdatedAt == current.UpdatedAt &&
        next.VmName == current.VmName &&
        next.Status == current.Status &&
        next.FinishedAt == current.FinishedAt &&
        next.PrUrl == current.PrUrl &&
        next.FailureReason == current.FailureReason &&
        next.FreezeCaptured == current.FreezeCaptured &&
        next.FreezeLocalPath == current.FreezeLocalPath;

    if (onlyHeartbeatChanged)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE runs
            SET last_heartbeat_at = @lastHeartbeatAt
            WHERE run_id = @id
              AND (last_heartbeat_at IS NULL OR @lastHeartbeatAt > last_heartbeat_at);
            """, conn);
        cmd.Parameters.AddWithValue("id", next.RunId);
        cmd.Parameters.AddWithValue("lastHeartbeatAt", next.LastHeartbeatAt!.Value);
        await cmd.ExecuteNonQueryAsync();
        return;
    }

    var changes = new List<(string column, string param, object? value)>();
    if (next.VmName != current.VmName) changes.Add(("vm_name", "vm", (object?)next.VmName ?? DBNull.Value));
    if (next.Status != current.Status) changes.Add(("status", "status", next.Status));
    if (next.FinishedAt != current.FinishedAt) changes.Add(("finished_at", "finishedAt", (object?)next.FinishedAt ?? DBNull.Value));
    if (next.PrUrl != current.PrUrl) changes.Add(("pr_url", "prUrl", (object?)next.PrUrl ?? DBNull.Value));
    if (next.FailureReason != current.FailureReason) changes.Add(("failure_reason", "failureReason", (object?)next.FailureReason ?? DBNull.Value));
    if (next.FreezeCaptured != current.FreezeCaptured) changes.Add(("freeze_captured", "freezeCaptured", next.FreezeCaptured));
    if (next.FreezeLocalPath != current.FreezeLocalPath) changes.Add(("freeze_local_path", "freezeLocalPath", (object?)next.FreezeLocalPath ?? DBNull.Value));
    if (next.UpdatedAt != current.UpdatedAt) changes.Add(("updated_at", "updatedAt", next.UpdatedAt));

    if (changes.Count == 0)
    {
        return;
    }

    var setClauses = changes.Select(c => $"{c.column} = @{c.param}");
    var sql = $"UPDATE runs SET {string.Join(", ", setClauses)} WHERE run_id = @id AND @updatedAt > updated_at";

    await using (var conn = await dataSource.OpenConnectionAsync())
    await using (var cmd = new NpgsqlCommand(sql, conn))
    {
        cmd.Parameters.AddWithValue("id", next.RunId);
        foreach (var (_, param, value) in changes)
        {
            cmd.Parameters.AddWithValue(param, value ?? DBNull.Value);
        }
        await cmd.ExecuteNonQueryAsync();
    }
}

// --- Read: all runs, newest first (public) ---
app.MapGet("/runs", async (IRunStore store) =>
    Results.Json(await store.All(), jsonOpts));

// --- Read: only non-terminal runs, newest first (public) ---
app.MapGet("/runs/active", async (IRunStore store) =>
    Results.Json(await store.Active(), jsonOpts));

// Serve the built Vue SPA (wwwroot) with SPA fallback to index.html.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapFallbackToFile("index.html");

app.Run();
return 0;
