namespace Api.Runs;

public static class RunFold
{
    public static RunState? Apply(RunState? current, RunEvent ev)
    {
        var at = ev.At ?? DateTimeOffset.UtcNow;

        return ev switch
        {
            RunStartedEvent e => ApplyRunStarted(current, e, at),
            AgentStartedEvent e => ApplyAgentStarted(current, e, at),
            HeartbeatEvent e => ApplyHeartbeat(current, e, at),
            StallDetectedEvent e => ApplyStallDetected(current, e, at),
            FreezeCapturedEvent e => ApplyFreezeCaptured(current, e, at),
            PrVerifiedEvent e => ApplyPrVerified(current, e, at),
            RunFinishedEvent => ApplyRunFinished(current, at),
            RunFailedEvent e => ApplyRunFailed(current, e, at),
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
        IsStale(current, at) || IsTerminal(current!.Status);

    private static bool IsTerminal(string status) => status is "done" or "failed";

    private static RunState? ApplyRunStarted(RunState? current, RunStartedEvent ev, DateTimeOffset at)
    {
        if (current is not null)
        {
            return null;
        }

        return new RunState(
            RunId: ev.RunId,
            Repo: ev.Repo ?? string.Empty,
            Branch: ev.Branch ?? string.Empty,
            Spec: ev.Spec ?? string.Empty,
            Model: ev.Model ?? string.Empty,
            VmName: null,
            Status: "launching",
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

    private static RunState? ApplyAgentStarted(RunState? current, AgentStartedEvent ev, DateTimeOffset at)
    {
        if (IsBlocked(current, at))
        {
            return null;
        }

        return current! with
        {
            Status = "running",
            VmName = ev.VmName,
            UpdatedAt = at,
        };
    }

    private static RunState? ApplyHeartbeat(RunState? current, HeartbeatEvent ev, DateTimeOffset at)
    {
        // Heartbeat keeps its own guard: it orders on LastHeartbeatAt, not UpdatedAt
        // (a heartbeat never bumps UpdatedAt), so IsStale does not apply here.
        if (current is null)
        {
            return null;
        }

        if (current.LastHeartbeatAt.HasValue && at <= current.LastHeartbeatAt.Value)
        {
            return null;
        }

        // currentPhase rides the heartbeat: the host relay reads the in-VM phase
        // marker and attaches it. A heartbeat without it keeps the last known
        // phase, so a transient read miss never wipes the advancing phase.
        return current with
        {
            LastHeartbeatAt = at,
            CurrentPhase = ev.CurrentPhase ?? current.CurrentPhase,
        };
    }

    private static RunState? ApplyStallDetected(RunState? current, StallDetectedEvent ev, DateTimeOffset at)
    {
        if (IsBlocked(current, at))
        {
            return null;
        }

        return current! with
        {
            Status = "stalled",
            FailureReason = ev.FailureReason,
            UpdatedAt = at,
        };
    }

    private static RunState? ApplyFreezeCaptured(RunState? current, FreezeCapturedEvent ev, DateTimeOffset at)
    {
        if (IsStale(current, at))
        {
            return null;
        }

        return current! with
        {
            FreezeCaptured = true,
            FreezeLocalPath = ev.FreezeLocalPath,
            UpdatedAt = at,
        };
    }

    private static RunState? ApplyPrVerified(RunState? current, PrVerifiedEvent ev, DateTimeOffset at)
    {
        if (IsStale(current, at))
        {
            return null;
        }

        return current! with
        {
            PrUrl = ev.PrUrl,
            UpdatedAt = at,
        };
    }

    private static RunState? ApplyRunFinished(RunState? current, DateTimeOffset at)
    {
        if (IsBlocked(current, at))
        {
            return null;
        }

        return current! with
        {
            Status = "done",
            FinishedAt = at,
            UpdatedAt = at,
        };
    }

    private static RunState? ApplyRunFailed(RunState? current, RunFailedEvent ev, DateTimeOffset at)
    {
        if (IsBlocked(current, at))
        {
            return null;
        }

        return current! with
        {
            Status = "failed",
            FinishedAt = at,
            FailureReason = ev.FailureReason,
            UpdatedAt = at,
        };
    }
}
