using Api.Catalogs;

namespace Api.Issues;

public static class IssuesEndpoints
{
    public static IEndpointRouteBuilder MapIssuesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/issues/{**repo}", async (
            string repo,
            List<IssueSnapshot> snapshot,
            IIssuesStore store,
            ITargetCatalogService? catalogs) =>
        {
            await store.SyncRepo(repo, snapshot);

            if (catalogs is not null)
            {
                try
                {
                    await catalogs.RefreshOnSyncAsync(repo);
                }
                catch (CatalogAuthException ex)
                {
                    return Results.Problem(
                        ex.Message,
                        statusCode: StatusCodes.Status502BadGateway,
                        title: "Target catalog credential failed");
                }
                catch (CatalogTooLargeException ex)
                {
                    return Results.Problem(
                        ex.Message,
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Target catalog too large");
                }
            }

            return Results.Accepted();
        }).AddEndpointFilter<RequireFactoryToken>();

        app.MapGet("/issues", async (IIssuesStore store) => Results.Json(await store.All()));

        return app;
    }
}
