namespace Api.Runs;

public static class RunFold
{
    public static RunState? Apply(RunState? current, RunEvent ev)
    {
        var at = ev.At ?? DateTimeOffset.UtcNow;

        return ev.Type switch
        {
            "run-started" => ApplyRunStarted(current, ev, at),
            "agent-started" => ApplyAgentStarted(current, ev, at),
            "heartbeat" => ApplyHeartbeat(current, ev, at),
            "stall-detected" => ApplyStallDetected(current, ev, at),
            "freeze-captured" => ApplyFreezeCaptured(current, ev, at),
            "pr-verified" => ApplyPrVerified(current, ev, at),
            "run-finished" => ApplyRunFinished(current, at),
            "run-failed" => ApplyRunFailed(current, ev, at),
            _ => null,
        };
    }

    private static RunState? ApplyRunStarted(RunState? current, RunEvent ev, DateTimeOffset at)
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

    private static RunState? ApplyAgentStarted(RunState? current, RunEvent ev, DateTimeOffset at)
    {
        if (current is null || IsTerminal(current.Status) || at <= current.UpdatedAt)
        {
            return null;
        }

        return current with
        {
            Status = "running",
            VmName = ev.VmName,
            UpdatedAt = at,
        };
    }

    private static RunState? ApplyHeartbeat(RunState? current, RunEvent ev, DateTimeOffset at)
    {
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

    private static RunState? ApplyStallDetected(RunState? current, RunEvent ev, DateTimeOffset at)
    {
        if (current is null || IsTerminal(current.Status) || at <= current.UpdatedAt)
        {
            return null;
        }

        return current with
        {
            Status = "stalled",
            FailureReason = ev.FailureReason,
            UpdatedAt = at,
        };
    }

    private static RunState? ApplyFreezeCaptured(RunState? current, RunEvent ev, DateTimeOffset at)
    {
        if (current is null || at <= current.UpdatedAt)
        {
            return null;
        }

        return current with
        {
            FreezeCaptured = true,
            FreezeLocalPath = ev.FreezeLocalPath,
            UpdatedAt = at,
        };
    }

    private static RunState? ApplyPrVerified(RunState? current, RunEvent ev, DateTimeOffset at)
    {
        if (current is null || at <= current.UpdatedAt)
        {
            return null;
        }

        return current with
        {
            PrUrl = ev.PrUrl,
            UpdatedAt = at,
        };
    }

    private static RunState? ApplyRunFinished(RunState? current, DateTimeOffset at)
    {
        if (current is null || IsTerminal(current.Status) || at <= current.UpdatedAt)
        {
            return null;
        }

        return current with
        {
            Status = "done",
            FinishedAt = at,
            UpdatedAt = at,
        };
    }

    private static RunState? ApplyRunFailed(RunState? current, RunEvent ev, DateTimeOffset at)
    {
        if (current is null || IsTerminal(current.Status) || at <= current.UpdatedAt)
        {
            return null;
        }

        return current with
        {
            Status = "failed",
            FinishedAt = at,
            FailureReason = ev.FailureReason,
            UpdatedAt = at,
        };
    }

    private static bool IsTerminal(string status) => status is "done" or "failed";
}
