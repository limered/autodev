namespace Api.Runs;

/// <summary>
/// One finished agent phase persisted on the run: wall-clock duration measured
/// in the VM plus token usage summed from that phase's <c>--format json</c>
/// NDJSON (<c>step_finish</c> events). Stored as a JSONB array on runs; merged
/// last-write-wins on (Agent, Iteration), so re-emitting a step is safe.
/// Iteration is 0 for single-run phases, 1..N for quality-loop iterations.
/// Category is the seeded slot the worker filled, used only to group post-run
/// detail under its header; it never feeds the liveness derivation. A worker
/// with no configured category lands in the uncategorized bucket.
/// </summary>
public record RunStep(
    string Agent,
    int Iteration,
    long DurationMs,
    long InputTokens,
    long OutputTokens,
    decimal? Cost,
    string Status,
    string Model,
    string? Category = null);
