namespace Api.Catalogs;

/// <summary>
/// Pure catalog decisions shared by the fetcher, the cache, and the fakes so the
/// fallback and guard rules live once. The target-repo file fully replaces the
/// factory catalog when present; absent or no-access maps to the factory fallback
/// and never fails the sync.
/// </summary>
public static class CatalogRules
{
    public const string TargetCatalogPath = "agents.json";

    public const long MaxCatalogBytes = 256 * 1024;

    public const string SourceTarget = "target";
    public const string SourceFactoryFallback = "factory-fallback";

    public static void AssertSizeOk(string repo, long size)
    {
        if (size > MaxCatalogBytes)
        {
            throw new CatalogTooLargeException(repo, size, MaxCatalogBytes);
        }
    }

    /// <summary>
    /// True when the fetch status means "no usable target file": absent (404) or
    /// no-access (403) both fall back to the factory catalog and never fail sync.
    /// A dead credential (401) throws so it surfaces instead of masquerading as
    /// a missing catalog.
    /// </summary>
    public static bool IsFallbackStatus(int statusCode, string repo)
    {
        if (statusCode == 404 || statusCode == 403)
        {
            return true;
        }

        if (statusCode == 401)
        {
            throw new CatalogAuthException(
                $"GitHub credential is invalid or expired while fetching the target catalog for '{repo}' (401).");
        }

        return false;
    }

    public static bool IsChanged(string? cachedSha, string? freshSha)
    {
        if (cachedSha is null || freshSha is null)
        {
            return !string.Equals(cachedSha, freshSha, StringComparison.Ordinal);
        }

        return !string.Equals(cachedSha, freshSha, StringComparison.Ordinal);
    }
}
