namespace Api.Runs;

/// <summary>
/// One pipeline stage of a run: the agent (phase) name and the model it will use.
/// Travels on the run-started event and is persisted on the run.
/// </summary>
public record RunStage(string Agent, string Model);

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
    string? FreezeLocalPath,
    IReadOnlyList<RunStage>? Stages = null)
{
    public Guid RunId { get; init; }
}
