namespace Api.Catalogs;

/// <summary>
/// The cached copy of one repo's target catalog. A null <see cref="Sha"/> with
/// the factory-fallback source means "no usable target file": consumers use the
/// factory catalog. The row exists per repo so sync and claim stay server-side.
/// </summary>
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
