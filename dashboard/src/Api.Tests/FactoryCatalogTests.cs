using Api.Catalogs;
using Xunit;

namespace Api.Tests;

public class FactoryCatalogTests
{
    private static string WriteTempCatalog(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"factory-agents-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Read_LegacyStagesOnlyFile_ReadsAsDefaultWorkflow()
    {
        var path = WriteTempCatalog("""{"stages":{"a":{},"b":{},"c":{}}}""");

        var catalog = FactoryCatalog.Read(path);

        Assert.Equal("default", catalog.DefaultWorkflow);
        var single = Assert.Single(catalog.Workflows);
        Assert.Equal("default", single.Name);
        Assert.Equal(3, single.StageCount);
    }

    [Fact]
    public void Read_NamedWorkflowsWithMarker_ReadsNamesAndCountsInOrder()
    {
        var path = WriteTempCatalog(
            """
            {"stages":{"a":{},"b":{},"c":{}},
             "workflows":{"full":["a","b","c"],"quick":["a","c"]},
             "defaultWorkflow":"full"}
            """);

        var catalog = FactoryCatalog.Read(path);

        Assert.Equal("full", catalog.DefaultWorkflow);
        Assert.Equal(
            new[] { ("full", 3), ("quick", 2) },
            catalog.Workflows.Select(w => (w.Name, w.StageCount)));
    }

    [Theory]
    [InlineData("""{"stages":{"a":{}},"workflows":{},"defaultWorkflow":"x"}""", "at least one workflow")]
    [InlineData("""{"stages":{"a":{}},"workflows":{"empty":[]},"defaultWorkflow":"empty"}""", "at least one stage id")]
    [InlineData("""{"stages":{"a":{}},"workflows":{"bad":["a","ghost"]},"defaultWorkflow":"bad"}""", "unknown stage id 'ghost'")]
    [InlineData("""{"stages":{"a":{}},"workflows":{"dup":["a","a"]},"defaultWorkflow":"dup"}""", "more than once")]
    [InlineData("""{"stages":{"a":{}},"workflows":{"default":["a"]},"defaultWorkflow":"default"}""", "reserved")]
    [InlineData("""{"stages":{"a":{}},"workflows":{"only":["a"]}}""", "no default workflow marker")]
    [InlineData("""{"stages":{"a":{}},"workflows":{"only":["a"]},"defaultWorkflow":"ghost"}""", "names no known workflow")]
    public void Read_InvalidCatalog_ThrowsWithClearReason(string json, string reason)
    {
        var path = WriteTempCatalog(json);

        var ex = Assert.Throws<FactoryCatalogException>(() => FactoryCatalog.Read(path));

        Assert.Contains(reason, ex.Message);
    }

    [Fact]
    public void Read_MalformedJson_ThrowsWithClearReason()
    {
        var path = WriteTempCatalog("{not json");

        var ex = Assert.Throws<FactoryCatalogException>(() => FactoryCatalog.Read(path));

        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void Read_MissingFile_ThrowsWithClearReason()
    {
        var path = Path.Combine(Path.GetTempPath(), $"factory-agents-{Guid.NewGuid():N}.json");

        var ex = Assert.Throws<FactoryCatalogException>(() => FactoryCatalog.Read(path));

        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void Locate_FindsAgentsJsonAboveContentRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"factory-root-{Guid.NewGuid():N}");
        var nested = Path.Combine(root, "dashboard", "src", "Api");
        Directory.CreateDirectory(nested);
        var catalogPath = Path.Combine(root, "agents.json");
        File.WriteAllText(catalogPath, """{"stages":{"a":{}}}""");

        Assert.Equal(catalogPath, FactoryCatalog.Locate(nested));
    }

    [SkippableFact]
    public void Locate_NoAgentsJsonAbove_ThrowsWithClearReason()
    {
        var nested = Path.Combine(Path.GetTempPath(), $"factory-empty-{Guid.NewGuid():N}", "a", "b");
        Directory.CreateDirectory(nested);

        for (var dir = new DirectoryInfo(nested); dir is not null; dir = dir.Parent)
        {
            Skip.If(File.Exists(Path.Combine(dir.FullName, "agents.json")),
                "Ambient agents.json above the temp path shadows the empty chain.");
        }

        var ex = Assert.Throws<FactoryCatalogException>(() => FactoryCatalog.Locate(nested));

        Assert.Contains("agents.json", ex.Message);
    }
}
