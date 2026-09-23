namespace Api.Catalogs;

public sealed record FetchedCatalog(string? Sha, string Content, string? Etag, bool NotModified = false);
