namespace Api.Catalogs;

/// <summary>
/// One fetch result from the target repo. Null means absent or no-access (the
/// caller maps it to the factory fallback). <see cref="NotModified"/> carries the
/// cheap change-detection answer: the cached copy is still current, no body sent.
/// </summary>
public sealed record FetchedCatalog(string? Sha, string Content, string? Etag, bool NotModified = false);
