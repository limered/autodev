using System.Net;
using Api.Issues;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Api.Tests;

public class GitHubIssuesClientTests
{
    private static HttpClient CreateClient(TestHandler handler)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.com/")
        };
    }

    [Fact]
    public async Task CloseIssueAsync_RemovesReadyForAgentLabelAndClosesIssue()
    {
        var requests = new List<HttpRequestMessage>();
        string? closeBody = null;
        var handler = new TestHandler(async (req, _) =>
        {
            requests.Add(req);
            if (req.Method == HttpMethod.Patch)
            {
                closeBody = await req.Content!.ReadAsStringAsync();
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new GitHubIssuesClient(CreateClient(handler), new NullLogger<GitHubIssuesClient>());

        await client.CloseIssueAsync("owner/repo", 42);

        Assert.Equal(2, requests.Count);
        Assert.Equal(HttpMethod.Delete, requests[0].Method);
        Assert.Equal("/repos/owner/repo/issues/42/labels/ready-for-agent", requests[0].RequestUri!.PathAndQuery);
        Assert.Equal(HttpMethod.Patch, requests[1].Method);
        Assert.Equal("/repos/owner/repo/issues/42", requests[1].RequestUri!.PathAndQuery);
        Assert.Contains("\"state\":\"closed\"", closeBody);
    }

    [Fact]
    public async Task CloseIssueAsync_SendsAuthorizationHeader()
    {
        HttpRequestMessage? captured = null;
        var handler = new TestHandler((req, _) =>
        {
            captured = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var httpClient = CreateClient(handler);
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");
        var client = new GitHubIssuesClient(httpClient, new NullLogger<GitHubIssuesClient>());

        await client.CloseIssueAsync("owner/repo", 1);

        Assert.NotNull(captured);
        Assert.Equal("Bearer test-token", captured!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task CloseIssueAsync_LabelNotFound_IsToleratedAndClosesIssue()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new TestHandler((req, _) =>
        {
            requests.Add(req);
            var status = req.Method == HttpMethod.Delete ? HttpStatusCode.NotFound : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status));
        });
        var client = new GitHubIssuesClient(CreateClient(handler), new NullLogger<GitHubIssuesClient>());

        await client.CloseIssueAsync("owner/repo", 7);

        Assert.Equal(2, requests.Count);
        Assert.Equal(HttpMethod.Patch, requests[1].Method);
    }

    [Fact]
    public async Task CloseIssueAsync_CloseFails_Throws()
    {
        var handler = new TestHandler((req, _) =>
        {
            var status = req.Method == HttpMethod.Delete ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity;
            return Task.FromResult(new HttpResponseMessage(status));
        });
        var client = new GitHubIssuesClient(CreateClient(handler), new NullLogger<GitHubIssuesClient>());

        await Assert.ThrowsAsync<HttpRequestException>(() => client.CloseIssueAsync("owner/repo", 7));
    }

    private sealed class TestHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _callback;

        public TestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
        {
            _callback = callback;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _callback(request, cancellationToken);
    }

    private sealed class NullLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
