namespace Api.Issues;

public static class IssuesEndpoints
{
    public static IEndpointRouteBuilder MapIssuesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/issues/{**repo}", async (string repo, List<IssueSnapshot> snapshot, IIssuesStore store) =>
        {
            await store.SyncRepo(repo, snapshot);
            return Results.Accepted();
        }).AddEndpointFilter<RequireFactoryToken>();

        app.MapGet("/issues", async (IIssuesStore store) => Results.Json(await store.All()));

        return app;
    }
}
