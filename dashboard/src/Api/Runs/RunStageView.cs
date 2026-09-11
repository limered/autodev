namespace Api.Runs;

/// <summary>
/// A pipeline stage projected for the runs API: the seeded agent and model plus
/// the derived per-stage status (done/running/failed/pending). Category is the
/// seeded slot the light belongs to (the uncategorized bucket for stages
/// persisted before categories). This is a read-side projection of <see cref="RunStage"/>;
/// it is never persisted.
/// </summary>
public record RunStageView(string Agent, string Model, string Status, string Category);
