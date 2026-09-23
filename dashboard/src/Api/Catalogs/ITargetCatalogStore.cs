namespace Api.Catalogs;

public interface ITargetCatalogStore
{
    Task<TargetCatalog?> GetAsync(string repo, CancellationToken cancellationToken = default);
    Task PutAsync(TargetCatalog catalog, CancellationToken cancellationToken = default);
}
