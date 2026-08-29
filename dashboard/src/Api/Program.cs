using System.Net.Http.Headers;
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
builder.Services.AddSingleton<IRunCompletion, RunCompletion>();
builder.Services.AddSingleton<IIssuesStore, IssuesStore>();
builder.Services.AddSingleton<IQueueStore, QueueStore>();
builder.Services.AddSingleton<IHostStore, HostStore>();

var githubPat = ReadGitHubPat(builder.Environment.ContentRootPath);
builder.Services.AddHttpClient<IGitHubIssuesClient, GitHubIssuesClient>(client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
    client.DefaultRequestHeaders.Add("User-Agent", "autodev-api");
    if (!string.IsNullOrWhiteSpace(githubPat))
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", githubPat);
    }
});

var app = builder.Build();

if (string.IsNullOrWhiteSpace(githubPat))
{
    app.Logger.LogWarning(
        "GitHub PAT not found (GITHUB_PAT env var, /etc/secrets/github-pat.txt, or .secrets/github-pat.txt); run-completion will not close issues on GitHub.");
}

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

static string? ReadGitHubPat(string contentRootPath)
{
    var fromEnv = Environment.GetEnvironmentVariable("GITHUB_PAT");
    if (!string.IsNullOrWhiteSpace(fromEnv))
    {
        return fromEnv.Trim();
    }

    try
    {
        string[] paths =
        [
            "/etc/secrets/github-pat.txt",
            Path.GetFullPath(Path.Combine(contentRootPath, "..", "..", ".secrets", "github-pat.txt")),
        ];
        var path = paths.FirstOrDefault(File.Exists);
        return path is null ? null : File.ReadAllText(path).Trim();
    }
    catch
    {
        return null;
    }
}
