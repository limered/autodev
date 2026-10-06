namespace Api.Queue;

/// <summary>
/// The claim-path verdict on a queue row's workflow pick: claim with the pick
/// resolved to its explicit workflow (<see cref="Resolved"/>), or skip the row so it
/// stays unclaimed with no run. A pick the catalog cannot verify is stale and never
/// silently substitutes the default.
/// </summary>
public sealed record WorkflowPickDecision(bool Skip, string? Resolved);
