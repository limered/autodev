namespace Api.Queue;

/// <summary>
/// The claim-path verdict on a queue row's workflow pick: claim with the workflow
/// that verifiably resolved (<see cref="Resolved"/>) — a verified pick or a
/// verifiable default — or skip the row so it stays unclaimed with no run. Nothing
/// the catalog cannot verify runs: an unverifiable pick never silently substitutes
/// the default, and an unverifiable default never claims with a blank workflow.
/// </summary>
public sealed record WorkflowPickDecision(bool Skip, string? Resolved);
