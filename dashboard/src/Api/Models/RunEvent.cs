namespace Api.Models;

public record RunEvent(
    string Type,
    DateTimeOffset? At,
    string? Repo,
    string? Branch,
    string? Spec,
    string? Model,
    string? VmName,
    string? PrUrl,
    string? FailureReason,
    string? FreezeLocalPath)
{
    public Guid RunId { get; init; }
}
