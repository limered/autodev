namespace Api.Runs;

/// <summary>
/// A pipeline stage projected for the runs API: the seeded agent and model plus
/// the derived per-stage status (done/running/pending). Category is the seeded
/// slot the light belongs to (the agent with no category yet, for runs persisted
/// before categories). This is a read-side projection of <see cref="RunStage"/>;
/// it is never persisted.
/// </summary>
public record RunStageView(string Agent, string Model, string Status, string Category);
