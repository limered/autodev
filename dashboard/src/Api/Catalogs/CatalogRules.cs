namespace Api.Catalogs;

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
        return !string.Equals(cachedSha, freshSha, StringComparison.Ordinal);
    }

    /// <summary>
    /// The workflow names a claimed catalog effectively runs under: a real target
    /// file's names parsed from its content; the factory catalog's names on fallback.
    /// Content that cannot be parsed, or no factory read available, answers no names —
    /// the claim caller then skips picks it cannot verify.
    /// </summary>
    public static (string? DefaultWorkflow, IReadOnlyList<string> Names) WorkflowNames(
        TargetCatalog? effective, IFactoryWorkflows? factory)
    {
        if (!string.IsNullOrWhiteSpace(effective?.Content))
        {
            try
            {
                var parsed = FactoryCatalog.ParseContent(effective.Content);
                return (parsed.DefaultWorkflow, parsed.Workflows.Select(w => w.Name).ToList());
            }
            catch (FactoryCatalogException)
            {
                return (null, Array.Empty<string>());
            }
        }

        var flows = factory?.TryRead();
        return flows is null
            ? (null, Array.Empty<string>())
            : (flows.DefaultWorkflow, flows.Workflows.Select(w => w.Name).ToList());
    }
}
