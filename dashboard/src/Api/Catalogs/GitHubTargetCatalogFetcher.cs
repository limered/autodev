using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Api.Catalogs;

/// <summary>
/// GitHub contents-API fetch of <c>agents.json</c> at the repo root with the backend
/// PAT already on the shared <see cref="HttpClient"/>. Absent (404) or no-access
/// (403) answers null for the factory fallback; a dead credential (401) throws so
/// it surfaces instead of masquerading as missing. The GitHub <c>size</c> field
/// guards oversize catalogs before the base64 body is decoded, and the decoded
/// bytes are guarded again so a lying size still fails fast.
/// </summary>
public sealed class GitHubTargetCatalogFetcher(HttpClient httpClient) : ITargetCatalogFetcher
{
    public async Task<FetchedCatalog?> FetchAsync(string repo, string? etag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"repos/{repo}/contents/{CatalogRules.TargetCatalogPath}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrEmpty(etag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new FetchedCatalog(null, string.Empty, etag, NotModified: true);
        }

        var status = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            if (CatalogRules.IsFallbackStatus(status, repo))
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Fetching the target catalog for '{repo}' failed with {(int)response.StatusCode}: {body}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        if (root.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt64(out var size))
        {
            CatalogRules.AssertSizeOk(repo, size);
        }

        var sha = root.TryGetProperty("sha", out var shaEl) ? shaEl.GetString() : null;
        var encoding = root.TryGetProperty("encoding", out var encEl) ? encEl.GetString() : null;
        var content = root.TryGetProperty("content", out var contentEl) ? contentEl.GetString() : string.Empty;
        content ??= string.Empty;

        byte[] bytes = string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase)
            ? Convert.FromBase64String(content.Replace("\n", string.Empty).Replace("\r", string.Empty))
            : Encoding.UTF8.GetBytes(content);
        CatalogRules.AssertSizeOk(repo, bytes.Length);

        var text = Encoding.UTF8.GetString(bytes);
        var responseEtag = response.Headers.ETag?.Tag;

        return new FetchedCatalog(sha, text, responseEtag);
    }
}
