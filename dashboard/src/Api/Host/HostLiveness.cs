namespace Api.Host;

/// <summary>
/// Pure host-liveness rule: one Module for the 20s threshold, shared by the
/// real store and the fake. Mirrors the QueueRules pattern — the rule lives
/// once, Adapters on both sides of the Seam share it.
/// </summary>
public static class HostLiveness
{
    public static readonly TimeSpan OnlineThreshold = TimeSpan.FromSeconds(20);

    public static bool IsOnline(DateTimeOffset? lastSeen, DateTimeOffset now)
        => lastSeen.HasValue && now - lastSeen.Value <= OnlineThreshold;
}
