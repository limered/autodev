namespace Api.Catalogs;

/// <summary>
/// Server-side fetch of the target-repo catalog file with the backend PAT. Returns
/// null for absent or no-access (the caller falls back); throws
/// <see cref="CatalogAuthException"/> for a dead credential and
/// <see cref="CatalogTooLargeException"/> for an oversize catalog. The optional
/// etag drives cheap conditional revalidation (304 means not modified).
/// </summary>
public interface ITargetCatalogFetcher
{
    Task<FetchedCatalog?> FetchAsync(string repo, string? etag, CancellationToken cancellationToken = default);
}
