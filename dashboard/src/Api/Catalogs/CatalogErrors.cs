namespace Api.Catalogs;

/// <summary>
/// Catalog failure modes with distinct surfaces: oversize fails fast with a clear
/// reason, a dead backend credential surfaces instead of masquerading as a missing
/// catalog, and a mid-flight catalog change fails the operation for retry instead
/// of silently substituting.
/// </summary>
public sealed class CatalogTooLargeException(string repo, long size, long limit)
    : InvalidOperationException(
        $"Target catalog for '{repo}' is {size} bytes, exceeding the {limit}-byte limit.")
{
    public string Repo { get; } = repo;
    public long Size { get; } = size;
    public long Limit { get; } = limit;
}

public sealed class CatalogAuthException(string message) : InvalidOperationException(message);

public sealed class CatalogChangedException(string repo)
    : InvalidOperationException(
        $"Target catalog for '{repo}' changed; retry the operation.")
{
    public string Repo { get; } = repo;
}
