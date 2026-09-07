namespace Api.Runs;

public static class RunFold
{
    /// <summary>
    /// Single source for the active set. Built from <see cref="RunStatus"/>
    /// constants so the fold, guards, and Active() queries share one list.
    /// </summary>
    public static readonly string[] ActiveStatuses = [RunStatus.Launching, RunStatus.Running, RunStatus.Stalled];

    public static bool IsActiveStatus(string status) => ActiveStatuses.Contains(status);

    public static bool IsTerminal(string status) => RunStatus.IsTerminal(status);

    public static RunState? Apply(RunState? current, RunEvent ev)
    {
        var at = ev.At ?? DateTimeOffset.UtcNow;

        // Full applicability rule (ordering + terminal) in one place; handlers below
        // are pure transitions and enforce nothing themselves:
        // - run-started: creation, applies only when there is no run yet.
        // - heartbeat: freshness orders on LastHeartbeatAt (it never bumps UpdatedAt,
        //   so IsStale does not apply); exempt from the terminal rule like freeze/pr.
        // - freeze-captured, pr-verified: IsStale only, exempt from the terminal rule
        //   (post-terminal bookkeeping must still land).
        // - everything else: IsBlocked (stale or terminal).
        return ev switch
        {
            RunStartedEvent e => current is not null ? null : ApplyRunStarted(e, at),
            HeartbeatEvent e => IsHeartbeatStale(current, at) ? null : ApplyHeartbeat(current!, e, at),
            FreezeCapturedEvent e => IsStale(current, at) ? null : ApplyFreezeCaptured(current!, e, at),
            PrVerifiedEvent e => IsStale(current, at) ? null : ApplyPrVerified(current!, e, at),
            AgentStartedEvent e => IsBlocked(current, at) ? null : ApplyAgentStarted(current!, e, at),
            StallDetectedEvent e => IsBlocked(current, at) ? null : ApplyStallDetected(current!, e, at),
            PhaseFinishedEvent e => IsBlocked(current, at) ? null : ApplyPhaseFinished(current!, e, at),
            RunFinishedEvent => IsBlocked(current, at) ? null : ApplyRunFinished(current!, at),
            RunFailedEvent e => IsBlocked(current, at) ? null : ApplyRunFailed(current!, e, at),
            _ => null, // unknown/unmapped event type (a bare RunEvent): no-op
        };
    }

    /// <summary>
    /// True when an event can never apply: there is no run yet, or the event is at or
    /// before the run's last update (out of order). When false, <paramref name="current"/>
    /// is not null and the event is in order — handlers may dereference it (current!).
    /// </summary>
    public static bool IsStale(RunState? current, DateTimeOffset at) =>
        current is null || at <= current.UpdatedAt;

    /// <summary>
    /// True when the event is <see cref="IsStale">stale</see> or the run already reached
    /// a terminal status and accepts no further transitions.
    /// </summary>
    public static bool IsBlocked(RunState? current, DateTimeOffset at) =>
        IsStale(current, at) || RunStatus.IsTerminal(current!.Status);

    /// <summary>
    /// Heartbeat freshness: orders on LastHeartbeatAt, not UpdatedAt (a heartbeat
    /// never bumps UpdatedAt, so <see cref="IsStale"/> does not apply here).
    /// </summary>
    public static bool IsHeartbeatStale(RunState? current, DateTimeOffset at) =>
        current is null || (current.LastHeartbeatAt.HasValue && at <= current.LastHeartbeatAt.Value);

    private static RunState ApplyRunStarted(RunStartedEvent ev, DateTimeOffset at)
    {
        return new RunState(
            RunId: ev.RunId,
            Repo: ev.Repo ?? string.Empty,
            Branch: ev.Branch ?? string.Empty,
            Spec: ev.Spec ?? string.Empty,
            Model: ev.Model ?? string.Empty,
            VmName: null,
            Status: RunStatus.Launching,
            StartedAt: at,
            FinishedAt: null,
            LastHeartbeatAt: null,
            PrUrl: null,
            FailureReason: null,
            FreezeCaptured: false,
            FreezeLocalPath: null,
            UpdatedAt: at,
            Stages: ev.Stages);
    }

    private static RunState ApplyAgentStarted(RunState current, AgentStartedEvent ev, DateTimeOffset at)
    {
        return current with
        {
            Status = RunStatus.Running,
            VmName = ev.VmName,
            UpdatedAt = at,
        };
    }

    private static RunState ApplyHeartbeat(RunState current, HeartbeatEvent ev, DateTimeOffset at)
    {
        // currentPhase rides the heartbeat: the host relay reads the in-VM phase
        // marker and attaches it. A heartbeat without it keeps the last known
        // phase, so a transient read miss never wipes the advancing phase.
        return current with
        {
            LastHeartbeatAt = at,
            CurrentPhase = ev.CurrentPhase ?? current.CurrentPhase,
        };
    }

    private static RunState ApplyStallDetected(RunState current, StallDetectedEvent ev, DateTimeOffset at)
    {
        return current with
        {
            Status = RunStatus.Stalled,
            FailureReason = ev.FailureReason,
            UpdatedAt = at,
        };
    }

    private static RunState ApplyFreezeCaptured(RunState current, FreezeCapturedEvent ev, DateTimeOffset at)
    {
        return current with
        {
            FreezeCaptured = true,
            FreezeLocalPath = ev.FreezeLocalPath,
            UpdatedAt = at,
        };
    }

    private static RunState ApplyPrVerified(RunState current, PrVerifiedEvent ev, DateTimeOffset at)
    {
        return current with
        {
            PrUrl = ev.PrUrl,
            UpdatedAt = at,
        };
    }

    private static RunState? ApplyPhaseFinished(RunState current, PhaseFinishedEvent ev, DateTimeOffset at)
    {
        // Model is required: a model-less step is a no-op drop, never stored.
        // Iteration is required: 0 for single-run phases, 1..N for loop iterations.
        if (string.IsNullOrWhiteSpace(ev.Agent) || !ev.Iteration.HasValue || string.IsNullOrWhiteSpace(ev.Model))
        {
            return null;
        }

        var step = new RunStep(
            ev.Agent!,
            ev.Iteration.Value,
            ev.DurationMs ?? 0,
            ev.InputTokens ?? 0,
            ev.OutputTokens ?? 0,
            ev.Cost,
            ev.Status ?? "done",
            ev.Model!);

        // Last-write-wins on (agent, iteration): a re-emit replaces the earlier
        // step in place, so arrival order (phase order, then loop iterations)
        // stays stable across re-emits. Steps never touch run Status: a failed
        // step is display-only on its row.
        var steps = (current.Steps ?? Array.Empty<RunStep>()).ToList();
        var index = steps.FindIndex(s =>
            string.Equals(s.Agent, step.Agent, StringComparison.Ordinal) && s.Iteration == step.Iteration);
        if (index >= 0)
        {
            steps[index] = step;
        }
        else
        {
            steps.Add(step);
        }

        return current with
        {
            Steps = steps,
            UpdatedAt = at,
        };
    }

    private static RunState ApplyRunFinished(RunState current, DateTimeOffset at)
    {
        return current with
        {
            Status = RunStatus.Done,
            FinishedAt = at,
            UpdatedAt = at,
        };
    }

    private static RunState ApplyRunFailed(RunState current, RunFailedEvent ev, DateTimeOffset at)
    {
        return current with
        {
            Status = RunStatus.Failed,
            FinishedAt = at,
            FailureReason = ev.FailureReason,
            UpdatedAt = at,
        };
    }
}
