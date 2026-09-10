namespace Api.Runs;

/// <summary>
/// The runs API contract: the persisted <see cref="RunState"/> fields plus a
/// live per-stage status projection. Separating the response from the
/// persistence model keeps derived data (stage status) out of the runs table
/// and off the heartbeat write path.
/// </summary>
public record RunResponse(
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
    DateTimeOffset UpdatedAt,
    IReadOnlyList<RunStageView> Stages,
    string? CurrentPhase,
    IReadOnlyList<RunStepView> Steps,
    string? CurrentCategory = null)
{
    public static RunResponse From(RunState r) => new(
        r.RunId,
        r.Repo,
        r.Branch,
        r.Spec,
        r.Model,
        r.VmName,
        r.Status,
        r.StartedAt,
        r.FinishedAt,
        r.LastHeartbeatAt,
        r.PrUrl,
        r.FailureReason,
        r.FreezeCaptured,
        r.FreezeLocalPath,
        r.UpdatedAt,
        RunStageStatus.Derive(r.Stages, r.CurrentPhase, r.Status, r.CurrentCategory),
        r.CurrentPhase,
        r.Steps?.Select(s => new RunStepView(
            s.Agent, s.Iteration, s.Model, s.Status,
            s.InputTokens, s.OutputTokens, s.DurationMs, s.Cost)).ToArray()
            ?? Array.Empty<RunStepView>(),
        r.CurrentCategory);
}
