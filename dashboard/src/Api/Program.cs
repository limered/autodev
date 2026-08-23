using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// PORT / ASPNETCORE_URLS: Dockerfile sets ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000}.
// Read env vars now so later tickets can use them.
var connectionString = builder.Configuration.GetConnectionString("Runs");
var factoryToken = builder.Configuration["FACTORY_TOKEN"]; // wired, not used yet

var app = builder.Build();

// Fail fast if the DB connection string is missing.
if (string.IsNullOrWhiteSpace(connectionString))
{
    app.Logger.LogCritical("ConnectionStrings__Runs is not set. Set the Postgres connection string env var. Exiting.");
    return 1;
}

// Verify the DB connection at boot. Do NOT create schema (ticket 02).
try
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    app.Logger.LogInformation("Postgres connection opened successfully.");
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Failed to open Postgres connection at boot. Exiting.");
    return 1;
}

// Serve the built Vue SPA (wwwroot) with SPA fallback to index.html.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapFallbackToFile("index.html");

app.Run();
return 0;
