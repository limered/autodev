namespace Api.Issues;

public record Issue(
    long GitHubId,
    string Repo,
    int Number,
    string Title,
    string HtmlUrl,
    IReadOnlyList<string> Labels,
    string? Body,
    string State,
    DateTimeOffset UpdatedAt);
