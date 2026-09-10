namespace Api.Runs;

/// <summary>
/// A finished phase projected for the runs API: the persisted <see cref="RunStep"/>
/// fields in wire order. Cost travels per step; no totals are computed. Category
/// is the seeded slot the worker filled (absent for steps relayed before
/// categories); the dashboard groups post-run detail under it.
/// </summary>
public record RunStepView(
    string Agent,
    int Iteration,
    string Model,
    string Status,
    long InputTokens,
    long OutputTokens,
    long DurationMs,
    decimal? Cost,
    string? Category = null);
