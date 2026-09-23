namespace Api.Catalogs;

/// <summary>
/// The catalog cache seam: sync refreshes the per-repo copy, claim and start
/// revalidate it cheaply, and readers resolve the effective catalog (target when
/// present, factory fallback otherwise).
/// </summary>
public interface ITargetCatalogService
{
    Task<TargetCatalog> RefreshOnSyncAsync(string repo, CancellationToken cancellationToken = default);
    Task<TargetCatalog> RevalidateAsync(string repo, CancellationToken cancellationToken = default);
    Task<TargetCatalog> GetEffectiveAsync(string repo, CancellationToken cancellationToken = default);
}
