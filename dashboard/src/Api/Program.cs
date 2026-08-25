using System.Text.Json;
using Api.Runs;
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
    await RunsSchema.EnsureAsync(dataSource);
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

    await store.Apply(runId, ev);

    return Results.Accepted();
});

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
