using Api.Catalogs;

namespace Api.Tests;

public sealed class FakeTargetCatalogStore : ITargetCatalogStore
{
    private readonly Dictionary<string, TargetCatalog> _rows = new(StringComparer.OrdinalIgnoreCase);

    public Task<TargetCatalog?> GetAsync(string repo, CancellationToken cancellationToken = default) =>
        Task.FromResult<TargetCatalog?>(_rows.TryGetValue(repo, out var row) ? row : null);

    public Task PutAsync(TargetCatalog catalog, CancellationToken cancellationToken = default)
    {
        _rows[catalog.Repo] = catalog;
        return Task.CompletedTask;
    }
}

public sealed class FakeTargetCatalogFetcher : ITargetCatalogFetcher
{
    private readonly Func<string, string?, FetchedCatalog?> _fetch;

    public FakeTargetCatalogFetcher(Func<string, string?, FetchedCatalog?> fetch) => _fetch = fetch;

    public int Calls { get; private set; }

    public string? LastEtag { get; private set; }

    public Task<FetchedCatalog?> FetchAsync(string repo, string? etag, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastEtag = etag;
        return Task.FromResult(_fetch(repo, etag));
    }

    public static FakeTargetCatalogFetcher Target(string sha, string content, string? etag = null) =>
        new((_, _) => new FetchedCatalog(sha, content, etag));

    public static FakeTargetCatalogFetcher Missing() =>
        new((_, _) => null);

    public static FakeTargetCatalogFetcher NotModified() =>
        new((_, etag) => new FetchedCatalog(null, string.Empty, etag, NotModified: true));

    public static FakeTargetCatalogFetcher AuthFailure() =>
        new((repo, _) => throw new CatalogAuthException($"GitHub credential is invalid for '{repo}' (401)."));

    public static FakeTargetCatalogFetcher TooLarge(string repo, long size) =>
        new((_, _) => throw new CatalogTooLargeException(repo, size, CatalogRules.MaxCatalogBytes));
}
