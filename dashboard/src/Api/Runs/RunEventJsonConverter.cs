using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Api.Runs;

/// <summary>
/// Binds the run-event wire format — flat camelCase JSON keyed on <c>"type"</c>, produced
/// by <c>Send-FactoryEvent</c> — onto the <see cref="RunEvent"/> hierarchy. Dispatch reads
/// the <see cref="RunEventDiscriminatorAttribute"/> mapping each derived record carries,
/// so the discriminator literals live on the records and nowhere else.
/// </summary>
/// <remarks>
/// Two wire realities shape this converter:
/// <list type="bullet">
/// <item>PowerShell hashtables serialize with unordered keys, so <c>type</c> can appear
/// anywhere in the payload — the discriminator lookup buffers and scans the whole object
/// instead of assuming it comes first (which is what STJ polymorphic binding requires).</item>
/// <item>An unknown or missing discriminator must not fail the request: it binds to a bare
/// <see cref="RunEvent"/> (with <see cref="RunFold"/> no-op'ing it), preserving the old flat
/// model's ignore-unknown behavior.</item>
/// </list>
/// </remarks>
public sealed class RunEventJsonConverter : JsonConverter<RunEvent>
{
    /// <summary>The JSON property every PowerShell producer keys the event type on.</summary>
    private const string DiscriminatorPropertyName = "type";

    // Private lookup built once from the [RunEventDiscriminator] declarations; Dictionary
    // over IReadOnlyDictionary per CA1859 (never mutated or exposed after construction).
    private static readonly Dictionary<string, Type> DerivedTypesByDiscriminator = BuildMap();

    private static Dictionary<string, Type> BuildMap()
    {
        var map = new Dictionary<string, Type>();
        foreach (var type in typeof(RunEvent).Assembly.GetTypes())
        {
            var attr = type.GetCustomAttribute<RunEventDiscriminatorAttribute>();
            if (attr is null)
            {
                continue;
            }

            if (type == typeof(RunEvent) || !typeof(RunEvent).IsAssignableFrom(type))
            {
                throw new InvalidOperationException(
                    $"{type.Name} carries [RunEventDiscriminator] but is not a derived run-event record.");
            }

            if (!map.TryAdd(attr.Type, type))
            {
                throw new InvalidOperationException($"Duplicate run-event discriminator '{attr.Type}'.");
            }
        }

        return map;
    }

    public override RunEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Clone the reader and buffer the object for the discriminator scan; the original
        // reader stays positioned at the start of the object for the real read.
        Utf8JsonReader lookahead = reader;
        string? discriminator;
        using (var doc = JsonDocument.ParseValue(ref lookahead))
        {
            discriminator = doc.RootElement.ValueKind == JsonValueKind.Object &&
                            doc.RootElement.TryGetProperty(DiscriminatorPropertyName, out var typeEl) &&
                            typeEl.ValueKind == JsonValueKind.String
                ? typeEl.GetString()
                : null;
        }

        if (discriminator is not null &&
            DerivedTypesByDiscriminator.TryGetValue(discriminator, out var derivedType))
        {
            return (RunEvent)JsonSerializer.Deserialize(ref reader, derivedType, options)!;
        }

        // Unknown or missing discriminator: swallow the value and return the no-op base
        // event — the old flat binding's `default => null` fold path.
        using (JsonDocument.ParseValue(ref reader))
        {
        }

        return new RunEvent();
    }

    public override void Write(Utf8JsonWriter writer, RunEvent value, JsonSerializerOptions options)
    {
        // The API only ever deserializes run events; Write exists so the contract stays
        // total and round-trippable, writing the discriminator first like the producers do.
        var runtimeType = value.GetType();
        var discriminator = DerivedTypesByDiscriminator.FirstOrDefault(kv => kv.Value == runtimeType).Key
            ?? throw new NotSupportedException(
                $"Serializing {runtimeType.Name} has no wire form: no [RunEventDiscriminator] mapping.");

        writer.WriteStartObject();
        writer.WriteString(DiscriminatorPropertyName, discriminator);

        var body = JsonSerializer.Serialize(value, runtimeType, options);
        using var doc = JsonDocument.Parse(body);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            property.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}
