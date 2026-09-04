using System.Text.Json.Serialization;

namespace Api.Runs;

/// <summary>
/// One pipeline stage of a run: the agent (phase) name and the model it will use.
/// Travels on the run-started event and is persisted on the run.
/// </summary>
public record RunStage(string Agent, string Model);

/// <summary>
/// Base of the run-event hierarchy. There is no DTO layer: this hierarchy IS the
/// <c>POST /runs/{runId}/events</c> wire contract, written by <c>Send-FactoryEvent</c>
/// (factory-report.ps1) as flat camelCase JSON keyed on <c>"type"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Dispatch is carried by <see cref="RunEventDiscriminatorAttribute"/> on each derived
/// record and performed by <see cref="RunEventJsonConverter"/>. STJ's own
/// <c>[JsonPolymorphic]</c> binding cannot serve this wire: it requires the discriminator
/// to be the FIRST JSON property — PowerShell hashtables serialize with unordered keys —
/// and it cannot coexist with the converter that makes unknown types degrade gracefully.
/// </para>
/// <para>
/// The base stays concrete on purpose: an unknown, missing, or non-string discriminator
/// binds to a bare <see cref="RunEvent"/>, which <see cref="RunFold"/> treats as a no-op —
/// ingest never fails on an unmapped event type.
/// </para>
/// </remarks>
[JsonConverter(typeof(RunEventJsonConverter))]
public record RunEvent
{
    /// <summary>When the event happened; the heartbeat producer overrides this from the VM clock.</summary>
    public DateTimeOffset? At { get; init; }

    /// <summary>Set from the route by RunStore.Apply; never travels on the wire.</summary>
    [JsonIgnore]
    public Guid RunId { get; init; }
}

/// <summary>The wire discriminator an event type is keyed on, e.g. "run-started".</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RunEventDiscriminatorAttribute(string type) : Attribute
{
    public string Type { get; } = type;
}

[RunEventDiscriminator("run-started")]
public sealed record RunStartedEvent(
    string? Repo = null,
    string? Branch = null,
    string? Spec = null,
    string? Model = null,
    IReadOnlyList<RunStage>? Stages = null) : RunEvent;

[RunEventDiscriminator("agent-started")]
public sealed record AgentStartedEvent(string? VmName = null) : RunEvent;

[RunEventDiscriminator("heartbeat")]
public sealed record HeartbeatEvent(string? CurrentPhase = null) : RunEvent;

[RunEventDiscriminator("stall-detected")]
public sealed record StallDetectedEvent(string? FailureReason = null) : RunEvent;

[RunEventDiscriminator("freeze-captured")]
public sealed record FreezeCapturedEvent(string? FreezeLocalPath = null) : RunEvent;

[RunEventDiscriminator("pr-verified")]
public sealed record PrVerifiedEvent(string? PrUrl = null) : RunEvent;

[RunEventDiscriminator("run-finished")]
public sealed record RunFinishedEvent : RunEvent;

[RunEventDiscriminator("run-failed")]
public sealed record RunFailedEvent(string? FailureReason = null) : RunEvent;
