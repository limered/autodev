namespace Api.Runs;

/// <summary>
/// Derives a live per-stage status (done/running/pending) from the seeded stage
/// list and the run's current phase. This is a read-side projection of run
/// state: the fold persists the seeded stages and currentPhase separately
/// (currentPhase rides the cheap heartbeat path), and this combines them when
/// the runs API is read, so deriving status never writes on a heartbeat.
/// </summary>
public static class RunStageStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Done = "done";

    public static IReadOnlyList<RunStageView> Derive(IReadOnlyList<RunStage>? stages, string? currentPhase, string runStatus)
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
            return stages.Select(s => new RunStageView(s.Agent, s.Model, Done)).ToArray();
        }

        var activeIndex = IndexOfPhase(stages, currentPhase);

        // No phase reported yet (launching, or running before the first phase
        // heartbeat) or a phase that matches no seeded stage: nothing has
        // verifiably started, so every stage stays pending.
        if (activeIndex < 0)
        {
            return stages.Select(s => new RunStageView(s.Agent, s.Model, Pending)).ToArray();
        }

        var views = new RunStageView[stages.Count];
        for (var i = 0; i < stages.Count; i++)
        {
            var status = i < activeIndex ? Done : i == activeIndex ? Running : Pending;
            views[i] = new RunStageView(stages[i].Agent, stages[i].Model, status);
        }

        return views;
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
