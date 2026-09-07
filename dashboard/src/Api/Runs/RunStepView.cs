namespace Api.Runs;

/// <summary>
/// A finished phase projected for the runs API: the persisted <see cref="RunStep"/>
/// fields in wire order. Cost travels per step; no totals are computed.
/// </summary>
public record RunStepView(
    string Agent,
    int Iteration,
    string Model,
    string Status,
    long InputTokens,
    long OutputTokens,
    long DurationMs,
    decimal? Cost);
