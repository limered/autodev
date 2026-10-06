using Api.Catalogs;
using Xunit;

namespace Api.Tests;

/// <summary>
/// Pins the claim-time verifier: the workflow names a claimed catalog effectively
/// runs under. A target file's names parse from its content; the factory catalog's
/// names apply on fallback; content that cannot be parsed, or no factory read,
/// answers no names — the claim caller then skips picks it cannot verify.
/// </summary>
public class CatalogRulesWorkflowNamesTests
{
    private const string NamedWorkflowsJson =
        """{"stages":{"a":{},"b":{}},"workflows":{"full":["a","b"],"quick":["a"]},"defaultWorkflow":"full"}""";

    [Fact]
    public void WorkflowNames_TargetContent_ReadsParsedNames()
    {
        var (defaultName, names) = CatalogRules.WorkflowNames(
            new TargetCatalog("owner/repo", "sha-1", NamedWorkflowsJson, CatalogRules.SourceTarget, null, DateTimeOffset.UtcNow),
            null);

        Assert.Equal("full", defaultName);
        Assert.Equal(new[] { "full", "quick" }, names);
    }

    [Fact]
    public void WorkflowNames_UnreadableContent_AnswerNoNames()
    {
        var (defaultName, names) = CatalogRules.WorkflowNames(
            new TargetCatalog("owner/repo", "sha-1", "{not json", CatalogRules.SourceTarget, null, DateTimeOffset.UtcNow),
            new FixedFactoryWorkflows(new FactoryWorkflows("full", [new WorkflowEntry("full", 1)])));

        Assert.Null(defaultName);
        Assert.Empty(names);
    }

    [Fact]
    public void WorkflowNames_FallbackCatalog_ReadsFactoryNames()
    {
        var factory = new FixedFactoryWorkflows(new FactoryWorkflows(
            "full", [new WorkflowEntry("full", 2), new WorkflowEntry("quick", 1)]));

        var (defaultName, names) = CatalogRules.WorkflowNames(
            new TargetCatalog(
                "owner/repo", null, null, CatalogRules.SourceFactoryFallback, null,
                DateTimeOffset.UtcNow), factory);

        Assert.Equal("full", defaultName);
        Assert.Equal(new[] { "full", "quick" }, names);
    }

    [Fact]
    public void WorkflowNames_NoFactoryRead_AnswerNoNames()
    {
        var (defaultName, names) = CatalogRules.WorkflowNames(
            TargetCatalog.Fallback("owner/repo", DateTimeOffset.UtcNow), null);

        Assert.Null(defaultName);
        Assert.Empty(names);
    }
}
