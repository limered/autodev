namespace Api.Runs;

/// <summary>
/// Resume-point derivation for same-branch restarts: maps a failed run's
/// seeded stages and current category to the first incomplete stage name.
/// Reuses the stage-catalog order via <see cref="RunStageStatus"/> — no
/// hardcoded loop list — so stage renames flow through the pipeline shape.
/// </summary>
public static class RunResume
{
    public static string? DeriveResumeStage(
        IReadOnlyList<RunStage>? stages,
        string? currentCategory)
    {
        if (stages is null || stages.Count == 0)
        {
            return null;
        }

        var views = RunStageStatus.Derive(stages, RunStatus.Failed, currentCategory);
        for (var i = 0; i < views.Count; i++)
        {
            if (views[i].Status == RunStageStatus.Failed || views[i].Status == RunStageStatus.Pending)
            {
                // No category ever reported (implement crash before the first
                // heartbeat): every stage reads pending, so the first entry is
                // the implement stage itself. The guest always skips implement
                // on a present branch, so point at the earliest loop instead —
                // the start of the enclosing loop where work resumes. When no
                // loop exists (e.g. a quick workflow), fall back to the first
                // pending stage itself.
                if (i == 0 && string.IsNullOrWhiteSpace(currentCategory))
                {
                    var loop = views.FirstOrDefault(v =>
                        string.Equals(v.Type, "loop", StringComparison.OrdinalIgnoreCase));
                    if (loop is not null)
                    {
                        return EffectiveName(loop);
                    }
                }

                return EffectiveName(views[i]);
            }
        }

        return null;
    }

    private static string EffectiveName(RunStageView view) =>
        string.IsNullOrWhiteSpace(view.Category) || view.Category == RunStageStatus.Uncategorized
            ? view.Agent
            : view.Category;
}
