using System.Text.Json;
using Api.Runs;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Runs");

// Fail fast if the DB connection string is missing.
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("ConnectionStrings__Runs is not set. Set the Postgres connection string env var. Exiting.");
    return 1;
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var dataSource = NpgsqlDataSource.Create(connectionString);
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<IRunStore, RunStore>();

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

app.MapRunsEndpoints();

// Serve the built Vue SPA (wwwroot) with SPA fallback to index.html.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapFallbackToFile("index.html");

app.Run();
return 0;
