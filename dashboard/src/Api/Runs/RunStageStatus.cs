namespace Api.Runs;

/// <summary>
/// Derives a live per-stage status (done/running/failed/pending) from the
/// seeded stage list and the run's current category. This is a read-side
/// projection of run state: the fold persists the seeded stages and
/// currentCategory separately (currentCategory rides the cheap heartbeat
/// path), and this combines them when the runs API is read, so deriving
/// status never writes on a heartbeat.
///
/// Matching is on the category identity only: each stage lights under its
/// seeded category. A stage with no category lands in the uncategorized
/// bucket and never matches a report. A finished run collapses every stage
/// to done; a failed run stops advancing, marking the active category failed
/// with earlier categories done and later ones pending. With no reported
/// category, or a category that matches no seeded stage, every stage stays
/// pending rather than guessing.
/// </summary>
public static class RunStageStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";

    /// <summary>
    /// The bucket for stages and steps with no configured category. Detail
    /// rows for unmapped workers group here; the bucket never matches a
    /// heartbeat report, so it cannot darken the seeded lights.
    /// </summary>
    public const string Uncategorized = "uncategorized";

    public static IReadOnlyList<RunStageView> Derive(
        IReadOnlyList<RunStage>? stages,
        string runStatus,
        string? currentCategory = null)
    {
        if (stages is null || stages.Count == 0)
        {
            return Array.Empty<RunStageView>();
        }

        // A finished run completed every stage. The in-VM phase marker still
        // names the last agent, so positional derivation alone would leave that
        // stage "running" — collapse the whole pipeline to done instead.
        if (runStatus == RunStatus.Done)
        {
            return stages.Select(s => new RunStageView(s.Agent, s.Model, Done, EffectiveCategory(s))).ToArray();
        }

        var activeIndex = IndexOfCategory(stages, currentCategory);

        // No category reported yet (launching, or running before the first
        // category heartbeat) or a category that matches no seeded stage:
        // nothing has verifiably started, so every stage stays pending.
        if (activeIndex < 0)
        {
            return stages.Select(s => new RunStageView(s.Agent, s.Model, Pending, EffectiveCategory(s))).ToArray();
        }

        var activeStatus = runStatus == RunStatus.Failed ? Failed : Running;
        var views = new RunStageView[stages.Count];
        for (var i = 0; i < stages.Count; i++)
        {
            var status = i < activeIndex ? Done : i == activeIndex ? activeStatus : Pending;
            views[i] = new RunStageView(stages[i].Agent, stages[i].Model, status, EffectiveCategory(stages[i]));
        }

        return views;
    }

    private static string EffectiveCategory(RunStage stage) =>
        string.IsNullOrWhiteSpace(stage.Category) ? Uncategorized : stage.Category;

    private static int IndexOfCategory(IReadOnlyList<RunStage> stages, string? currentCategory)
    {
        if (string.IsNullOrWhiteSpace(currentCategory))
        {
            return -1;
        }

        for (var i = 0; i < stages.Count; i++)
        {
            // Only a seeded category can go active: stages without one sit in
            // the uncategorized bucket and never match, even if a report ever
            // carried the bucket name.
            if (!string.IsNullOrWhiteSpace(stages[i].Category)
                && string.Equals(stages[i].Category, currentCategory, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
