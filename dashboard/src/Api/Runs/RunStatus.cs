namespace Api.Runs;

/// <summary>
/// The central run-status vocabulary. The fold writes these statuses and
/// guards on <see cref="IsTerminal"/>; read-side projections collapse on
/// <see cref="Done"/>.
/// </summary>
public static class RunStatus
{
    public const string Launching = "launching";
    public const string Running = "running";
    public const string Stalled = "stalled";
    public const string Done = "done";
    public const string Failed = "failed";

    public static bool IsTerminal(string status) => status is Done or Failed;
}
