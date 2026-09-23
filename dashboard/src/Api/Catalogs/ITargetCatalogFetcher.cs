namespace Api.Catalogs;

public interface ITargetCatalogFetcher
{
    Task<FetchedCatalog?> FetchAsync(string repo, string? etag, CancellationToken cancellationToken = default);
}
