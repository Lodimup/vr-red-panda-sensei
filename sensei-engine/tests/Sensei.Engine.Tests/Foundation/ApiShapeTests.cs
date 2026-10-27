using Sensei.Engine.Catalog;
using Sensei.Engine.Llm;

namespace Sensei.Engine.Tests.Foundation;

/// <summary>Public API shape rules every slice and the black-box suite rely on (SPEC §2–§3; orchestrator ruling on namespaces).</summary>
public sealed class ApiShapeTests
{
    private static readonly Type[] Exported = typeof(SenseiEngine).Assembly.GetExportedTypes();

    [Fact]
    public void S3_every_public_type_name_is_unique_across_namespaces()
    {
        var duplicates = Exported.Where(t => !t.IsNested).GroupBy(t => t.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void S3_engine_entry_types_live_in_the_root_namespace()
    {
        Assert.Equal("Sensei.Engine", typeof(SenseiEngine).Namespace);
        Assert.Equal("Sensei.Engine", typeof(SenseiEngineOptions).Namespace);
        Assert.Equal("Sensei.Engine", typeof(TurnResult).Namespace);
        Assert.Equal("Sensei.Engine", typeof(PlayerActionResult).Namespace);
        Assert.Equal("Sensei.Engine.Catalog", typeof(ObjectCatalog).Namespace);
        Assert.Equal("Sensei.Engine.Llm", typeof(LlmSettings).Namespace);
        Assert.Equal("Sensei.Engine.Llm", typeof(ChatClientFactory).Namespace);
    }

    [Fact]
    public void S3_every_public_type_is_under_the_sensei_engine_namespace()
    {
        Assert.All(Exported, t => Assert.StartsWith("Sensei.Engine", t.Namespace));
    }
}
