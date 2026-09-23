using System.Net;
using System.Text;
using System.Text.Json;
using Api.Catalogs;
using Xunit;

namespace Api.Tests;

public class GitHubTargetCatalogFetcherTests
{
    private static GitHubTargetCatalogFetcher Fetcher(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        out List<HttpRequestMessage> seen)
    {
        var requests = new List<HttpRequestMessage>();
        seen = requests;
        var stub = new StubHandler(req =>
        {
            requests.Add(req);
            return handler(req);
        });
        return new GitHubTargetCatalogFetcher(new HttpClient(stub)
        {
            BaseAddress = new Uri("https://api.github.com/"),
        });
    }

    private static HttpResponseMessage ContentsResponse(string sha, string raw, string? etag = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            sha,
            size = Encoding.UTF8.GetByteCount(raw),
            encoding = "base64",
            content = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)),
        });
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        if (etag is not null)
        {
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        }

        return response;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handle(request));
    }

    [Fact]
    public async Task Fetch_PresentFile_DecodesContentWithSha()
    {
        var fetcher = Fetcher(_ => ContentsResponse("sha-1", """{"stages":{}}""", "\"etag-1\""), out _);

        var fetched = await fetcher.FetchAsync("owner/repo", null);

        Assert.NotNull(fetched);
        Assert.False(fetched!.NotModified);
        Assert.Equal("sha-1", fetched.Sha);
        Assert.Equal("""{"stages":{}}""", fetched.Content);
        Assert.Equal("\"etag-1\"", fetched.Etag);
    }

    [Fact]
    public async Task Fetch_NotFound_MapsToFallbackNull()
    {
        var fetcher = Fetcher(_ => new HttpResponseMessage(HttpStatusCode.NotFound), out _);

        Assert.Null(await fetcher.FetchAsync("owner/repo", null));
    }

    [Fact]
    public async Task Fetch_Forbidden_MapsToFallbackNull()
    {
        var fetcher = Fetcher(_ => new HttpResponseMessage(HttpStatusCode.Forbidden), out _);

        Assert.Null(await fetcher.FetchAsync("owner/repo", null));
    }

    [Fact]
    public async Task Fetch_Unauthorized_SurfacesDeadCredential()
    {
        var fetcher = Fetcher(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized), out _);

        await Assert.ThrowsAsync<CatalogAuthException>(() => fetcher.FetchAsync("owner/repo", null));
    }

    [Fact]
    public async Task Fetch_NotModified_ReturnsCheapMarker()
    {
        var fetcher = Fetcher(
            req => req.Headers.IfNoneMatch.Count > 0 || req.Headers.Contains("If-None-Match")
                ? new HttpResponseMessage(HttpStatusCode.NotModified)
                : ContentsResponse("sha-1", "x"),
            out var seen);

        var fetched = await fetcher.FetchAsync("owner/repo", "\"etag-1\"");

        Assert.NotNull(fetched);
        Assert.True(fetched!.NotModified);
        Assert.True(seen.Single().Headers.Contains("If-None-Match"));
    }

    [Fact]
    public async Task Fetch_OversizeSizeField_FailsFast()
    {
        var payload = JsonSerializer.Serialize(new
        {
            sha = "sha-big",
            size = CatalogRules.MaxCatalogBytes + 1,
            encoding = "base64",
            content = Convert.ToBase64String(Encoding.UTF8.GetBytes("x")),
        });
        var fetcher = Fetcher(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        }, out _);

        var ex = await Assert.ThrowsAsync<CatalogTooLargeException>(
            () => fetcher.FetchAsync("owner/repo", null));
        Assert.Contains("owner/repo", ex.Message);
    }
}
