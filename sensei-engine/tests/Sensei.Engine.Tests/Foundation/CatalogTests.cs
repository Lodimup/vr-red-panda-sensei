using Sensei.Engine.Catalog;

namespace Sensei.Engine.Tests.Foundation;

public sealed class CatalogTests
{
    // SPEC §4, in order: id, japanese, reading, romaji, english, variants, container.
    private static readonly (string Id, string Japanese, string Reading, string Romaji, string English, string[] Variants, bool Container)[] Expected =
    {
        ("water", "水", "みず", "mizu", "water", new[] { "default" }, false),
        ("tea", "お茶", "おちゃ", "ocha", "tea", new[] { "default" }, false),
        ("cup", "コップ", "コップ", "koppu", "cup", new[] { "blue", "red" }, false),
        ("apple", "りんご", "りんご", "ringo", "apple", new[] { "default" }, false),
        ("banana", "バナナ", "バナナ", "banana", "banana", new[] { "default" }, false),
        ("rice_ball", "おにぎり", "おにぎり", "onigiri", "rice ball", new[] { "default" }, false),
        ("book", "本", "ほん", "hon", "book", new[] { "green", "yellow" }, false),
        ("pen", "ペン", "ペン", "pen", "pen", new[] { "default" }, false),
        ("bag", "かばん", "かばん", "kaban", "bag", new[] { "default" }, true),
        ("box", "箱", "はこ", "hako", "box", new[] { "default" }, true),
        ("key", "鍵", "かぎ", "kagi", "key", new[] { "default" }, false),
        ("umbrella", "傘", "かさ", "kasa", "umbrella", new[] { "default" }, false),
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
        Assert.Equal(want.Variants, got.Variants);
        Assert.Equal(want.Container, got.IsContainer);
    }

    [Theory]
    [MemberData(nameof(EntryIndexes))]
    public void S11_first_variant_is_the_default_variant(int index)
    {
        var entry = ObjectCatalog.Entries[index];
        Assert.Equal(Expected[index].Variants[0], entry.DefaultVariant);
        Assert.Equal(entry.Variants[0], entry.DefaultVariant);
    }

    [Fact]
    public void S11_cup_default_variant_is_blue_not_red()
    {
        var cup = ObjectCatalog.Get("cup");
        Assert.Equal("blue", cup.DefaultVariant);
        Assert.NotEqual("red", cup.DefaultVariant);
    }

    [Fact]
    public void S11_book_default_variant_is_green_not_yellow()
    {
        var book = ObjectCatalog.Get("book");
        Assert.Equal("green", book.DefaultVariant);
        Assert.NotEqual("yellow", book.DefaultVariant);
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

    [Theory]
    [InlineData("cup", "blue", true)]
    [InlineData("cup", "red", true)]
    [InlineData("cup", "default", false)]
    [InlineData("cup", "green", false)]
    [InlineData("cup", "Blue", false)]
    [InlineData("cup", "", false)]
    [InlineData("book", "green", true)]
    [InlineData("book", "yellow", true)]
    [InlineData("book", "blue", false)]
    [InlineData("book", "default", false)]
    [InlineData("water", "default", true)]
    [InlineData("water", "blue", false)]
    [InlineData("water", "Default", false)]
    [InlineData("box", "default", true)]
    public void S11_HasVariant_accepts_only_listed_variants(string catalogId, string variant, bool expected)
    {
        Assert.Equal(expected, ObjectCatalog.Get(catalogId).HasVariant(variant));
    }

    [Fact]
    public void S11_every_entry_has_the_default_variant_listed_as_valid()
    {
        foreach (var entry in ObjectCatalog.Entries)
            Assert.True(entry.HasVariant(entry.DefaultVariant), entry.CatalogId);
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
            Assert.NotEmpty(e.Variants);
        }
    }
}
