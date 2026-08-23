namespace Api.Models;

public record RunState(
    Guid RunId,
    string Repo,
    string Branch,
    string Spec,
    string Model,
    string? VmName,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? LastHeartbeatAt,
    string? PrUrl,
    string? FailureReason,
    bool FreezeCaptured,
    string? FreezeLocalPath,
    DateTimeOffset UpdatedAt);
