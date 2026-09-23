using Api.Catalogs;
using Xunit;

namespace Api.Tests;

public class TargetCatalogTests
{
    private static TargetCatalogService Service(
        ITargetCatalogFetcher fetcher,
        ITargetCatalogStore? store = null) =>
        new(fetcher, store ?? new FakeTargetCatalogStore());

    [Fact]
    public void AssertSizeOk_AtLimit_Passes()
    {
        CatalogRules.AssertSizeOk("owner/repo", CatalogRules.MaxCatalogBytes);
    }

    [Fact]
    public void AssertSizeOk_OverLimit_ThrowsWithClearReason()
    {
        var ex = Assert.Throws<CatalogTooLargeException>(
            () => CatalogRules.AssertSizeOk("owner/repo", CatalogRules.MaxCatalogBytes + 1));
        Assert.Contains("owner/repo", ex.Message);
        Assert.Contains($"{CatalogRules.MaxCatalogBytes}", ex.Message);
    }

    [Fact]
    public void IsFallbackStatus_AbsentAndNoAccess_FallsBack()
    {
        Assert.True(CatalogRules.IsFallbackStatus(404, "owner/repo"));
        Assert.True(CatalogRules.IsFallbackStatus(403, "owner/repo"));
    }

    [Fact]
    public void IsFallbackStatus_DeadCredential_Surfaces()
    {
        Assert.Throws<CatalogAuthException>(() => CatalogRules.IsFallbackStatus(401, "owner/repo"));
    }

    [Fact]
    public async Task RefreshOnSync_TargetFile_ReplacesFallback()
    {
        var store = new FakeTargetCatalogStore();
        var service = Service(FakeTargetCatalogFetcher.Target("sha-1", """{"stages":{}}"""), store);

        var next = await service.RefreshOnSyncAsync("owner/repo");

        Assert.Equal(CatalogRules.SourceTarget, next.Source);
        Assert.Equal("sha-1", next.Sha);
        Assert.Equal("""{"stages":{}}""", next.Content);
        Assert.Equal(next, await store.GetAsync("owner/repo"));
    }

    [Fact]
    public async Task RefreshOnSync_MissingFile_MapsToFallbackWithoutFailing()
    {
        var store = new FakeTargetCatalogStore();
        var service = Service(FakeTargetCatalogFetcher.Missing(), store);

        var next = await service.RefreshOnSyncAsync("owner/repo");

        Assert.True(next.IsFallback);
        Assert.Null(next.Content);
    }

    [Fact]
    public async Task RefreshOnSync_DeadCredential_SurfacesInsteadOfFallback()
    {
        var service = Service(FakeTargetCatalogFetcher.AuthFailure());

        await Assert.ThrowsAsync<CatalogAuthException>(
            () => service.RefreshOnSyncAsync("owner/repo"));
    }

    [Fact]
    public async Task RefreshOnSync_Oversize_FailsFast()
    {
        var service = Service(FakeTargetCatalogFetcher.TooLarge("owner/repo", CatalogRules.MaxCatalogBytes + 1));

        var ex = await Assert.ThrowsAsync<CatalogTooLargeException>(
            () => service.RefreshOnSyncAsync("owner/repo"));
        Assert.Contains("owner/repo", ex.Message);
    }

    [Fact]
    public async Task Revalidate_NotModified_KeepsCacheCheaply()
    {
        var store = new FakeTargetCatalogStore();
        await store.PutAsync(new TargetCatalog(
            "owner/repo", "sha-1", "content", CatalogRules.SourceTarget, "etag-1", DateTimeOffset.UtcNow));
        var fetcher = FakeTargetCatalogFetcher.NotModified();
        var service = Service(fetcher, store);

        var effective = await service.RevalidateAsync("owner/repo");

        Assert.Equal("sha-1", effective.Sha);
        Assert.Equal("etag-1", fetcher.LastEtag);
    }

    [Fact]
    public async Task Revalidate_ChangedContent_ThrowsForRetryAndSwapsCache()
    {
        var store = new FakeTargetCatalogStore();
        await store.PutAsync(new TargetCatalog(
            "owner/repo", "sha-1", "old", CatalogRules.SourceTarget, null, DateTimeOffset.UtcNow));
        var service = Service(FakeTargetCatalogFetcher.Target("sha-2", "new"), store);

        await Assert.ThrowsAsync<CatalogChangedException>(
            () => service.RevalidateAsync("owner/repo"));

        var cached = await store.GetAsync("owner/repo");
        Assert.Equal("sha-2", cached!.Sha);
        Assert.Equal("new", cached.Content);
    }

    [Fact]
    public async Task Revalidate_TargetDisappears_ThrowsForRetryAndFallsBack()
    {
        var store = new FakeTargetCatalogStore();
        await store.PutAsync(new TargetCatalog(
            "owner/repo", "sha-1", "old", CatalogRules.SourceTarget, null, DateTimeOffset.UtcNow));
        var service = Service(FakeTargetCatalogFetcher.Missing(), store);

        await Assert.ThrowsAsync<CatalogChangedException>(
            () => service.RevalidateAsync("owner/repo"));

        Assert.True((await store.GetAsync("owner/repo"))!.IsFallback);
    }

    [Fact]
    public async Task Revalidate_StillFallback_NoThrow()
    {
        var service = Service(FakeTargetCatalogFetcher.Missing());

        var effective = await service.RevalidateAsync("owner/repo");

        Assert.True(effective.IsFallback);
    }

    [Fact]
    public async Task GetEffective_NeverSynced_ReadsAsFallback()
    {
        var service = Service(FakeTargetCatalogFetcher.Missing());

        var effective = await service.GetEffectiveAsync("owner/never-synced");

        Assert.True(effective.IsFallback);
        Assert.Null(effective.Content);
    }

    [Fact]
    public async Task ClaimNext_AttachesTargetCatalog()
    {
        var issues = new FakeIssuesStore();
        await issues.SyncRepo("owner/repo", new[]
        {
            new Issues.IssueSnapshot(
                1, 7, "Issue", "https://github.com/owner/repo/issues/7",
                new[] { "ready-for-agent" }, "Do it", "open", DateTimeOffset.UtcNow),
        });
        var queue = new FakeQueueStore { Issues = issues };
        queue.Resolver = new FakeIssueResolver(queue, issues);
        var store = new FakeTargetCatalogStore();
        var sync = new TargetCatalogService(
            FakeTargetCatalogFetcher.Target("sha-9", """{"stages":{}}"""), store);
        await sync.RefreshOnSyncAsync("owner/repo");
        queue.Catalogs = new TargetCatalogService(
            FakeTargetCatalogFetcher.NotModified(), store);

        var enqueued = await queue.Enqueue(1);
        await queue.StartNext(enqueued!.Id);
        var claim = await queue.ClaimNext();

        Assert.NotNull(claim);
        Assert.Equal("sha-9", claim!.CatalogSha);
        Assert.Equal(CatalogRules.SourceTarget, claim.CatalogSource);
        Assert.Equal("""{"stages":{}}""", claim.CatalogContent);
    }

    [Fact]
    public async Task ClaimNext_CatalogChanged_FailsWithoutAttachingRun()
    {
        var issues = new FakeIssuesStore();
        await issues.SyncRepo("owner/repo", new[]
        {
            new Issues.IssueSnapshot(
                1, 7, "Issue", "https://github.com/owner/repo/issues/7",
                new[] { "ready-for-agent" }, "Do it", "open", DateTimeOffset.UtcNow),
        });
        var queue = new FakeQueueStore { Issues = issues };
        queue.Resolver = new FakeIssueResolver(queue, issues);
        var store = new FakeTargetCatalogStore();
        await store.PutAsync(new TargetCatalog(
            "owner/repo", "sha-1", "old", CatalogRules.SourceTarget, null, DateTimeOffset.UtcNow));
        queue.Catalogs = new TargetCatalogService(
            FakeTargetCatalogFetcher.Target("sha-1", "old"), store);

        var enqueued = await queue.Enqueue(1);
        await queue.StartNext(enqueued!.Id);
        queue.Catalogs = new TargetCatalogService(
            FakeTargetCatalogFetcher.Target("sha-2", "new"), store);

        await Assert.ThrowsAsync<CatalogChangedException>(() => queue.ClaimNext());
        Assert.Null((await queue.All()).Single().RunId);
    }
}
