namespace Api.Runs;

/// <summary>
/// Derives a live per-stage status (done/running/pending) from the seeded stage
/// list and the run's current category. This is a read-side projection of run
/// state: the fold persists the seeded stages and currentCategory separately
/// (currentCategory rides the cheap heartbeat path), and this combines them when
/// the runs API is read, so deriving status never writes on a heartbeat.
///
/// Matching is on the category identity: each stage lights under its effective
/// category (its seeded category, falling back to the worker name for runs
/// persisted before categories). A reported category fixes the original
/// dark-lights failure where the reporting worker name (e.g. static-analysis)
/// never equalled the seeded slot (quality-loop). With no reported category the
/// derivation falls back to the legacy worker-name match on currentPhase, so old
/// runs render exactly as today.
/// </summary>
public static class RunStageStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Done = "done";

    public static IReadOnlyList<RunStageView> Derive(
        IReadOnlyList<RunStage>? stages,
        string? currentPhase,
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

        var activeIndex = !string.IsNullOrWhiteSpace(currentCategory)
            ? IndexOfCategory(stages, currentCategory)
            : IndexOfPhase(stages, currentPhase);

        // No category reported yet (launching, or running before the first
        // category heartbeat) or a category that matches no seeded stage:
        // nothing has verifiably started, so every stage stays pending.
        if (activeIndex < 0)
        {
            return stages.Select(s => new RunStageView(s.Agent, s.Model, Pending, EffectiveCategory(s))).ToArray();
        }

        var views = new RunStageView[stages.Count];
        for (var i = 0; i < stages.Count; i++)
        {
            var status = i < activeIndex ? Done : i == activeIndex ? Running : Pending;
            views[i] = new RunStageView(stages[i].Agent, stages[i].Model, status, EffectiveCategory(stages[i]));
        }

        return views;
    }

    private static string EffectiveCategory(RunStage stage) =>
        string.IsNullOrWhiteSpace(stage.Category) ? stage.Agent : stage.Category;

    private static int IndexOfCategory(IReadOnlyList<RunStage> stages, string? currentCategory)
    {
        if (string.IsNullOrWhiteSpace(currentCategory))
        {
            return -1;
        }

        for (var i = 0; i < stages.Count; i++)
        {
            if (string.Equals(EffectiveCategory(stages[i]), currentCategory, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfPhase(IReadOnlyList<RunStage> stages, string? currentPhase)
    {
        if (string.IsNullOrWhiteSpace(currentPhase))
        {
            return -1;
        }

        for (var i = 0; i < stages.Count; i++)
        {
            if (string.Equals(stages[i].Agent, currentPhase, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
