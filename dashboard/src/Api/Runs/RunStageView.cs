namespace Api.Runs;

/// <summary>
/// A pipeline stage projected for the runs API: the seeded agent and model plus
/// the derived per-stage status (done/running/pending). This is a read-side
/// projection of <see cref="RunStage"/>; it is never persisted.
/// </summary>
public record RunStageView(string Agent, string Model, string Status);
