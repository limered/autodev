namespace Api.Catalogs;

public sealed record TargetCatalog(
    string Repo,
    string? Sha,
    string? Content,
    string Source,
    string? Etag,
    DateTimeOffset FetchedAt)
{
    public bool IsFallback => Source == CatalogRules.SourceFactoryFallback;

    public static TargetCatalog Fallback(string repo, DateTimeOffset fetchedAt) =>
        new(repo, null, null, CatalogRules.SourceFactoryFallback, null, fetchedAt);
}
