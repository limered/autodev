namespace Api.Catalogs;

public interface ITargetCatalogService
{
    Task<TargetCatalog> RefreshOnSyncAsync(string repo, CancellationToken cancellationToken = default);
    Task<TargetCatalog> RevalidateAsync(string repo, CancellationToken cancellationToken = default);
    Task<TargetCatalog> GetEffectiveAsync(string repo, CancellationToken cancellationToken = default);
}
