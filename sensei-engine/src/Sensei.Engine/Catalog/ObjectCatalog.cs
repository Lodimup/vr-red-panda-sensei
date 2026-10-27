using System;
using System.Collections.Generic;
using System.Linq;

namespace Sensei.Engine.Catalog;

/// <summary>One catalog entry (SPEC §4). The first variant is the default.</summary>
public sealed class CatalogEntry
{
    public CatalogEntry(string catalogId, string japanese, string reading, string romaji, string english, IReadOnlyList<string> variants, bool isContainer)
    {
        CatalogId = catalogId;
        Japanese = japanese;
        Reading = reading;
        Romaji = romaji;
        English = english;
        Variants = variants;
        IsContainer = isContainer;
    }

    public string CatalogId { get; }
    public string Japanese { get; }
    public string Reading { get; }
    public string Romaji { get; }
    public string English { get; }
    public IReadOnlyList<string> Variants { get; }
    public string DefaultVariant => Variants[0];
    public bool IsContainer { get; }

    public bool HasVariant(string variant) => Variants.Contains(variant, StringComparer.Ordinal);
}

/// <summary>The fixed object catalog, data in code (S11). Sprites are 32×32; the engine stores only ids.</summary>
public static class ObjectCatalog
{
    private static readonly CatalogEntry[] AllEntries =
    {
        new("water", "水", "みず", "mizu", "water", new[] { "default" }, false),
        new("tea", "お茶", "おちゃ", "ocha", "tea", new[] { "default" }, false),
        new("cup", "コップ", "コップ", "koppu", "cup", new[] { "blue", "red" }, false),
        new("apple", "りんご", "りんご", "ringo", "apple", new[] { "default" }, false),
        new("banana", "バナナ", "バナナ", "banana", "banana", new[] { "default" }, false),
        new("rice_ball", "おにぎり", "おにぎり", "onigiri", "rice ball", new[] { "default" }, false),
        new("book", "本", "ほん", "hon", "book", new[] { "green", "yellow" }, false),
        new("pen", "ペン", "ペン", "pen", "pen", new[] { "default" }, false),
        new("bag", "かばん", "かばん", "kaban", "bag", new[] { "default" }, true),
        new("box", "箱", "はこ", "hako", "box", new[] { "default" }, true),
        new("key", "鍵", "かぎ", "kagi", "key", new[] { "default" }, false),
        new("umbrella", "傘", "かさ", "kasa", "umbrella", new[] { "default" }, false),
    };

    private static readonly Dictionary<string, CatalogEntry> ById = AllEntries.ToDictionary(e => e.CatalogId, StringComparer.Ordinal);

    /// <summary>All 12 entries in SPEC order.</summary>
    public static IReadOnlyList<CatalogEntry> Entries => AllEntries;

    /// <summary>All catalog ids in SPEC order.</summary>
    public static IReadOnlyList<string> Ids { get; } = AllEntries.Select(e => e.CatalogId).ToArray();

    public static bool TryGet(string? catalogId, out CatalogEntry entry)
    {
        if (catalogId is not null && ById.TryGetValue(catalogId, out var found))
        {
            entry = found;
            return true;
        }
        entry = null!;
        return false;
    }

    public static CatalogEntry Get(string catalogId) =>
        TryGet(catalogId, out var e) ? e : throw new ArgumentException($"unknown catalog id '{catalogId}'", nameof(catalogId));

    public static bool Contains(string? catalogId) => catalogId is not null && ById.ContainsKey(catalogId);

    public static bool IsContainer(string catalogId) => TryGet(catalogId, out var e) && e.IsContainer;
}
