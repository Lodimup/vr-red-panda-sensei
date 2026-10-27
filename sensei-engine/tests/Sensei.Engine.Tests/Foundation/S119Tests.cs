using System.Text.Json;
using Sensei.Engine.Catalog;
using Sensei.Engine.Json;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.World;
using static Sensei.Engine.Tests.Foundation.SceneHelpers;

namespace Sensei.Engine.Tests.Foundation;

/// <summary>S119 (RULING(Handler 2026-10-28)): one image per object, no colour variants anywhere.</summary>
public sealed class S119Tests
{
    [Theory]
    [InlineData(typeof(CatalogEntry))]
    [InlineData(typeof(ObjectInstance))]
    [InlineData(typeof(SnapshotInstance))]
    [InlineData(typeof(PredicateSelector))]
    [InlineData(typeof(SpawnRequest))]
    [InlineData(typeof(Sensei.Engine.Tools.World.SpawnObjectEntry))]
    public void S119_no_type_carries_a_variant(Type type) =>
        Assert.DoesNotContain(type.GetProperties(), p => p.Name.Contains("Variant", StringComparison.Ordinal));

    [Fact]
    public async Task S119_scene_snapshot_json_has_no_variant()
    {
        await using var t = await TestEngine.CreateAsync();
        Spawn(t, ("cup", "left"), ("book", "right"));
        var json = JsonSerializer.Serialize(t.Engine.GetScene(), SenseiJsonContext.Default.SceneSnapshot);
        Assert.DoesNotContain("variant", json);
        Assert.Contains("\"catalog_id\":\"cup\"", json);
    }

    [Fact]
    public void S119_unsupported_asset_is_not_an_error_code()
    {
        Assert.DoesNotContain("unsupported_asset", ToolErrorCodes.All);
        Assert.Equal(21, ToolErrorCodes.All.Count);
        Assert.DoesNotContain(typeof(ToolErrorCodes).GetFields(), f => f.Name == "UnsupportedAsset");
    }

    [Fact]
    public void S119_no_tool_schema_mentions_variant()
    {
        foreach (var tool in ToolRegistry.CreateStandardTools())
            Assert.DoesNotContain("\"variant\"", tool.JsonSchema.GetRawText());
    }

    [Fact]
    public void S119_cup_and_book_are_single_objects_like_the_rest()
    {
        Assert.Equal(12, ObjectCatalog.Entries.Count);
        Assert.True(ObjectCatalog.Contains("cup"));
        Assert.True(ObjectCatalog.Contains("book"));
    }

    [Fact]
    public async Task S119_spawn_objects_with_a_variant_is_an_unknown_property()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("x");
        var r = await t.CallAsync("spawn_objects", """{"objects":[{"catalog_id":"cup","variant":"red"}]}""");
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal("objects[0].variant: unknown property", r.Detail);
        t.Engine.BeginTurn("y");
        var plain = await t.CallAsync("spawn_objects", """{"objects":[{"catalog_id":"cup"}]}""");
        Assert.True(plain.IsSuccess);
    }

    [Fact]
    public async Task S119_a_selector_variant_property_is_an_unknown_property()
    {
        await using var t = await TestEngine.CreateAsync();
        Spawn(t, "cup");
        t.Engine.BeginTurn("x");
        var r = await t.CallAsync("start_challenge", """{"family":"listen_arrange","instruction":{"english":"x"},"skill_targets":[{"dimension":"listening","key":"koppu"}],"predicates":[{"type":"in_region","selector":{"catalog_id":"cup","variant":"red"},"region":"left"}]}""");
        Assert.Equal("predicates[0].selector.variant: unknown property", r.Detail);
        var detail = ToolRegistry.CreateStandardTools().Single(t => t.Name == "start_challenge").JsonSchema
            .GetProperty("properties").GetProperty("predicates").GetProperty("items").GetProperty("properties")
            .GetProperty("selector").GetProperty("additionalProperties").ValueKind;
        Assert.Equal(JsonValueKind.False, detail);
        await Task.CompletedTask;
    }
}
