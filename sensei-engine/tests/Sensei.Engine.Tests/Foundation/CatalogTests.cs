using Sensei.Engine.Catalog;

namespace Sensei.Engine.Tests.Foundation;

public sealed class CatalogTests
{
    // SPEC §4, in order: id, japanese, reading, romaji, english, container (S119: no variant column).
    private static readonly (string Id, string Japanese, string Reading, string Romaji, string English, bool Container)[] Expected =
    {
        ("water", "水", "みず", "mizu", "water", false),
        ("tea", "お茶", "おちゃ", "ocha", "tea", false),
        ("cup", "コップ", "コップ", "koppu", "cup", false),
        ("apple", "りんご", "りんご", "ringo", "apple", false),
        ("banana", "バナナ", "バナナ", "banana", "banana", false),
        ("rice_ball", "おにぎり", "おにぎり", "onigiri", "rice ball", false),
        ("book", "本", "ほん", "hon", "book", false),
        ("pen", "ペン", "ペン", "pen", "pen", false),
        ("bag", "かばん", "かばん", "kaban", "bag", true),
        ("box", "箱", "はこ", "hako", "box", true),
        ("key", "鍵", "かぎ", "kagi", "key", false),
        ("umbrella", "傘", "かさ", "kasa", "umbrella", false),
    };

    public static TheoryData<int> EntryIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Expected.Length; i++) data.Add(i);
        return data;
    }

    [Fact]
    public void S11_catalog_has_exactly_12_entries()
    {
        Assert.Equal(12, ObjectCatalog.Entries.Count);
        Assert.Equal(12, ObjectCatalog.Ids.Count);
        Assert.Equal(12, Expected.Length);
    }

    [Fact]
    public void S11_catalog_ids_follow_spec_order()
    {
        Assert.Equal(Expected.Select(e => e.Id), ObjectCatalog.Ids);
        Assert.Equal(Expected.Select(e => e.Id), ObjectCatalog.Entries.Select(e => e.CatalogId));
    }

    [Fact]
    public void S11_catalog_ids_are_unique()
    {
        Assert.Equal(ObjectCatalog.Ids.Count, ObjectCatalog.Ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(EntryIndexes))]
    public void S11_entry_matches_every_spec_column(int index)
    {
        var want = Expected[index];
        var got = ObjectCatalog.Entries[index];

        Assert.Equal(want.Id, got.CatalogId);
        Assert.Equal(want.Japanese, got.Japanese);
        Assert.Equal(want.Reading, got.Reading);
        Assert.Equal(want.Romaji, got.Romaji);
        Assert.Equal(want.English, got.English);
        Assert.Equal(want.Container, got.IsContainer);
    }

    [Fact]
    public void S11_only_bag_and_box_are_containers()
    {
        var containers = ObjectCatalog.Entries.Where(e => e.IsContainer).Select(e => e.CatalogId).ToArray();
        Assert.Equal(new[] { "bag", "box" }, containers);
        Assert.True(ObjectCatalog.IsContainer("bag"));
        Assert.True(ObjectCatalog.IsContainer("box"));
        Assert.False(ObjectCatalog.IsContainer("cup"));
        Assert.False(ObjectCatalog.IsContainer("apple"));
    }

    [Fact]
    public void S11_IsContainer_is_false_for_an_unknown_id()
    {
        Assert.False(ObjectCatalog.IsContainer("table"));
    }

    [Theory]
    [MemberData(nameof(EntryIndexes))]
    public void S11_TryGet_finds_every_known_id(int index)
    {
        var id = Expected[index].Id;
        Assert.True(ObjectCatalog.TryGet(id, out var entry));
        Assert.Same(ObjectCatalog.Entries[index], entry);
        Assert.Same(entry, ObjectCatalog.Get(id));
    }

    [Theory]
    [InlineData("table")]
    [InlineData("")]
    [InlineData("Water")]
    [InlineData("WATER")]
    [InlineData(" water")]
    [InlineData("water ")]
    [InlineData("rice ball")]
    public void S11_TryGet_is_false_for_unknown_or_non_exact_ids(string id)
    {
        Assert.False(ObjectCatalog.TryGet(id, out _));
        Assert.False(ObjectCatalog.Contains(id));
    }

    [Fact]
    public void S11_TryGet_and_Contains_are_false_for_null()
    {
        Assert.False(ObjectCatalog.TryGet(null, out _));
        Assert.False(ObjectCatalog.Contains(null));
    }

    [Theory]
    [MemberData(nameof(EntryIndexes))]
    public void S11_Contains_is_true_for_every_known_id(int index)
    {
        Assert.True(ObjectCatalog.Contains(Expected[index].Id));
    }

    [Fact]
    public void S11_Get_throws_ArgumentException_for_an_unknown_id()
    {
        Assert.Throws<ArgumentException>(() => ObjectCatalog.Get("table"));
    }

    [Fact]
    public void S11_every_entry_has_non_empty_text_columns()
    {
        foreach (var e in ObjectCatalog.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Japanese), e.CatalogId);
            Assert.False(string.IsNullOrWhiteSpace(e.Reading), e.CatalogId);
            Assert.False(string.IsNullOrWhiteSpace(e.Romaji), e.CatalogId);
            Assert.False(string.IsNullOrWhiteSpace(e.English), e.CatalogId);
        }
    }
}
