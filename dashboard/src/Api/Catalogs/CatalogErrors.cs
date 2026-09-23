namespace Api.Catalogs;

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
