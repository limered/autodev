namespace Api.Catalogs;

/// <summary>
/// The cached copy per repo. Sync refreshes it; claim and start revalidate through
/// it cheaply. A missing row reads as the factory fallback so callers never fail
/// for a repo that was never synced.
/// </summary>
public interface ITargetCatalogStore
{
    Task<TargetCatalog?> GetAsync(string repo, CancellationToken cancellationToken = default);
    Task PutAsync(TargetCatalog catalog, CancellationToken cancellationToken = default);
}
