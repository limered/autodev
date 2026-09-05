namespace Api.Issues;

/// <summary>
/// The issues domain's claim-payload derivation, stated once so the SQL
/// <see cref="IssueResolver"/> and the in-memory <see cref="IIssueResolver"/> fake
/// derive it identically and cannot drift: how an issue row's fields become the
/// (repo url, spec) the queue claims with. The resolver seam owns storage and
/// execution; this class owns only the derivation.
/// </summary>
public static class IssueClaimRules
{
    /// <summary>
    /// The claim payload for an issue row: the clone URL, and the spec — the trimmed
    /// body when it carries text, else the title, else the repo.
    /// </summary>
    public static IssueClaimPayload PayloadFor(string repo, string? body, string? title)
    {
        var repoUrl = $"https://github.com/{repo}.git";
        var spec = !string.IsNullOrWhiteSpace(body) ? body.Trim() : title ?? repo;
        return new IssueClaimPayload(repoUrl, spec);
    }
}
