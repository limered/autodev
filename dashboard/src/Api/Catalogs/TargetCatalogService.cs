namespace Api.Catalogs;

/// <summary>
/// Orchestrates the per-repo catalog cache. Sync fetches unconditionally and swaps
/// the cached copy: a target file replaces the factory fallback, while absent or
/// no-access maps back to the fallback without failing the sync. Auth and oversize
/// failures propagate so the sync surfaces them. Claim/start revalidate cheaply
/// with the cached etag: not-modified keeps the copy, a new body swaps it and then
/// fails the operation with <see cref="CatalogChangedException"/> for retry, so the
/// run never silently substitutes a stale catalog.
/// </summary>
public sealed class TargetCatalogService(
    ITargetCatalogFetcher fetcher,
    ITargetCatalogStore store,
    TimeProvider? clock = null) : ITargetCatalogService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<TargetCatalog> RefreshOnSyncAsync(string repo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var cached = await store.GetAsync(repo, cancellationToken);
        var fetched = await fetcher.FetchAsync(repo, cached?.Etag, cancellationToken);

        TargetCatalog next;
        if (fetched is null)
        {
            next = TargetCatalog.Fallback(repo, _clock.GetUtcNow());
        }
        else if (fetched.NotModified)
        {
            next = cached is null
                ? TargetCatalog.Fallback(repo, _clock.GetUtcNow())
                : cached with { FetchedAt = _clock.GetUtcNow() };
        }
        else
        {
            next = new TargetCatalog(
                repo,
                fetched.Sha,
                fetched.Content,
                CatalogRules.SourceTarget,
                fetched.Etag,
                _clock.GetUtcNow());
        }

        await store.PutAsync(next, cancellationToken);
        return next;
    }

    public async Task<TargetCatalog> RevalidateAsync(string repo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var cached = await store.GetAsync(repo, cancellationToken)
            ?? TargetCatalog.Fallback(repo, _clock.GetUtcNow());

        var fetched = await fetcher.FetchAsync(repo, cached.Etag, cancellationToken);

        if (fetched is null)
        {
            if (!cached.IsFallback)
            {
                var fallback = TargetCatalog.Fallback(repo, _clock.GetUtcNow());
                await store.PutAsync(fallback, cancellationToken);
                throw new CatalogChangedException(repo);
            }

            return cached;
        }

        if (fetched.NotModified)
        {
            return cached;
        }

        if (!cached.IsFallback && !CatalogRules.IsChanged(cached.Sha, fetched.Sha))
        {
            var refreshed = cached with { FetchedAt = _clock.GetUtcNow(), Etag = fetched.Etag ?? cached.Etag };
            await store.PutAsync(refreshed, cancellationToken);
            return refreshed;
        }

        var next = new TargetCatalog(
            repo,
            fetched.Sha,
            fetched.Content,
            CatalogRules.SourceTarget,
            fetched.Etag,
            _clock.GetUtcNow());
        await store.PutAsync(next, cancellationToken);

        var changed = cached.IsFallback || CatalogRules.IsChanged(cached.Sha, fetched.Sha);
        if (changed)
        {
            throw new CatalogChangedException(repo);
        }

        return next;
    }

    public async Task<TargetCatalog> GetEffectiveAsync(string repo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        return await store.GetAsync(repo, cancellationToken)
            ?? TargetCatalog.Fallback(repo, _clock.GetUtcNow());
    }
}
