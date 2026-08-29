using System.Text.Json;
using Api.Host;
using Api.Issues;
using Api.Queue;
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
builder.Services.AddSingleton<IIssuesStore, IssuesStore>();
builder.Services.AddSingleton<IQueueStore, QueueStore>();
builder.Services.AddSingleton<IHostStore, HostStore>();

var app = builder.Build();

// Verify the DB connection and create the schema idempotently at boot.
try
{
    await RunsSchema.EnsureAsync(dataSource);
    await IssuesSchema.EnsureAsync(dataSource);
    await QueueSchema.EnsureAsync(dataSource);
    await HostSchema.EnsureAsync(dataSource);
    app.Logger.LogInformation("Postgres connection opened and schema ensured.");
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Failed to connect to Postgres or ensure schema at boot. Exiting.");
    return 1;
}

app.MapRunsEndpoints();
app.MapIssuesEndpoints();
app.MapQueueEndpoints();
app.MapHostEndpoints();

// Serve the built Vue SPA (wwwroot) with SPA fallback to index.html.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapFallbackToFile("index.html");

app.Run();
return 0;
