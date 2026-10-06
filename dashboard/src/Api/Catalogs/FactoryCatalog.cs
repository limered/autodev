using System.Text.Json;

namespace Api.Catalogs;

public sealed record WorkflowEntry(string Name, int StageCount);

public sealed record FactoryWorkflows(string DefaultWorkflow, IReadOnlyList<WorkflowEntry> Workflows);

public sealed class FactoryCatalogException(string message) : Exception(message);

public static class FactoryCatalog
{
    public const string ReservedDefaultName = "default";

    public static string Locate(string contentRootPath)
    {
        var dir = new DirectoryInfo(contentRootPath);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "agents.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FactoryCatalogException(
            $"Factory catalog not found: no agents.json above '{contentRootPath}'.");
    }

    public static FactoryWorkflows Read(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            throw new FactoryCatalogException($"Factory catalog unreadable at '{path}': {ex.Message}");
        }

        return ParseContent(json, detail => $"Factory catalog invalid at '{path}': {detail}");
    }

    /// <summary>
    /// Parses catalog content received as text (a fetched target-repo agents.json):
    /// same rules as <see cref="Read"/>, without the file.
    /// </summary>
    public static FactoryWorkflows ParseContent(string content)
    {
        return ParseContent(content, detail => $"Factory catalog invalid: {detail}");
    }

    private static FactoryWorkflows ParseContent(string content, Func<string, string> message)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new FactoryCatalogException(message(ex.Message));
        }

        using (doc)
        {
            return Parse(doc.RootElement, message);
        }
    }

    private static FactoryWorkflows Parse(JsonElement root, Func<string, string> message)
    {
        const string path = "catalog content";
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("stages", out var stages)
            || stages.ValueKind != JsonValueKind.Object)
        {
            throw new FactoryCatalogException(message("missing 'stages' map."));
        }

        var stageIds = stages.EnumerateObject().Select(p => p.Name).ToList();

        if (!root.TryGetProperty("workflows", out var workflows))
        {
            return new FactoryWorkflows(
                ReservedDefaultName,
                [new WorkflowEntry(ReservedDefaultName, stageIds.Count)]);
        }

        if (workflows.ValueKind != JsonValueKind.Object || !workflows.EnumerateObject().Any())
        {
            throw new FactoryCatalogException(
                message("'workflows' map must declare at least one workflow."));
        }

        var entries = new List<WorkflowEntry>();
        foreach (var property in workflows.EnumerateObject())
        {
            var name = property.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new FactoryCatalogException(
                    message("workflow name must be a non-empty string."));
            }

            if (name == ReservedDefaultName)
            {
                throw new FactoryCatalogException(
                    message("workflow name 'default' is reserved for the legacy stages-only workflow."));
            }

            entries.Add(new WorkflowEntry(name, AssertStageIds(path, message, name, property.Value, stageIds).Count));
        }

        if (!root.TryGetProperty("defaultWorkflow", out var marker)
            || marker.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(marker.GetString()))
        {
            throw new FactoryCatalogException(
                message("declares workflows but no default workflow marker ('defaultWorkflow')."));
        }

        var defaultName = marker.GetString()!;
        if (!entries.Any(e => e.Name == defaultName))
        {
            throw new FactoryCatalogException(
                message($"default workflow '{defaultName}' names no known workflow (known: {string.Join(", ", entries.Select(e => e.Name))})."));
        }

        return new FactoryWorkflows(defaultName, entries);
    }

    private static List<string> AssertStageIds(
        string path, Func<string, string> message, string workflow, JsonElement value, IReadOnlyList<string> knownIds)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new FactoryCatalogException(
                message($"workflow '{workflow}' must be an ordered list of stage ids."));
        }

        var ids = value.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : string.Empty)
            .ToList();

        if (ids.Count == 0)
        {
            throw new FactoryCatalogException(
                message($"workflow '{workflow}' must list at least one stage id."));
        }

        var seen = new HashSet<string>();
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new FactoryCatalogException(
                    message($"workflow '{workflow}' lists an empty stage id."));
            }

            if (!knownIds.Contains(id))
            {
                throw new FactoryCatalogException(
                    message($"workflow '{workflow}' references unknown stage id '{id}' (known: {string.Join(", ", knownIds)})."));
            }

            if (!seen.Add(id))
            {
                throw new FactoryCatalogException(
                    message($"workflow '{workflow}' lists stage id '{id}' more than once."));
            }
        }

        return ids;
    }
}
