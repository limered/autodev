namespace Api.Catalogs;

public static class TargetCatalogEndpoints
{
    public static IEndpointRouteBuilder MapTargetCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/catalogs/{**repo}", async (string repo, ITargetCatalogService catalogs) =>
        {
            var effective = await catalogs.GetEffectiveAsync(repo);
            return Results.Json(new CatalogResponse(
                effective.Repo,
                effective.Source,
                effective.Sha,
                effective.Content,
                effective.FetchedAt));
        });

        return app;
    }
}

public sealed record CatalogResponse(
    string Repo,
    string Source,
    string? Sha,
    string? Content,
    DateTimeOffset FetchedAt);
