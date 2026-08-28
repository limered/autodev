namespace Api.Issues;

public static class IssuesEndpoints
{
    public static IEndpointRouteBuilder MapIssuesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/issues/{repo}", async (string repo, List<IssueSnapshot> snapshot, IIssuesStore store, IConfiguration config, HttpRequest req) =>
        {
            var factoryToken = config["FACTORY_TOKEN"];
            if (string.IsNullOrEmpty(factoryToken) || req.Headers["X-Factory-Token"].ToString() != factoryToken)
                return Results.Unauthorized();

            await store.SyncRepo(repo, snapshot);
            return Results.Accepted();
        });

        app.MapGet("/issues", async (IIssuesStore store) => Results.Json(await store.All()));

        return app;
    }
}
