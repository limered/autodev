namespace Api.Issues;

public record IssueSnapshot(
    long Id,
    int Number,
    string Title,
    string HtmlUrl,
    IReadOnlyList<string> Labels,
    string? Body,
    string State,
    DateTimeOffset UpdatedAt);
