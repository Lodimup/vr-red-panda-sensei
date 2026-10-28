using System;
using System.Collections.Generic;
using System.Linq;

namespace Sensei.Engine.Catalog;

/// <summary>One catalog entry (SPEC §4). One image per object, no variants (S119).</summary>
public sealed class CatalogEntry
{
    public CatalogEntry(string catalogId, string japanese, string reading, string romaji, string english, bool isContainer)
    {
        CatalogId = catalogId;
        Japanese = japanese;
        Reading = reading;
        Romaji = romaji;
        English = english;
        IsContainer = isContainer;
    }

    public string CatalogId { get; }
    public string Japanese { get; }
    public string Reading { get; }
    public string Romaji { get; }
    public string English { get; }
    public bool IsContainer { get; }
}

/// <summary>The fixed object catalog, data in code (S11). Sprites are 32×32; the engine stores only ids.</summary>
public static class ObjectCatalog
{
    private static readonly CatalogEntry[] AllEntries =
    {
        new("water", "水", "みず", "mizu", "water", false),
        new("tea", "お茶", "おちゃ", "ocha", "tea", false),
        new("cup", "コップ", "コップ", "koppu", "cup", false),
        new("apple", "りんご", "りんご", "ringo", "apple", false),
        new("banana", "バナナ", "バナナ", "banana", "banana", false),
        new("rice_ball", "おにぎり", "おにぎり", "onigiri", "rice ball", false),
        new("book", "本", "ほん", "hon", "book", false),
        new("pen", "ペン", "ペン", "pen", "pen", false),
        new("bag", "かばん", "かばん", "kaban", "bag", true),
        new("box", "箱", "はこ", "hako", "box", true),
        new("key", "鍵", "かぎ", "kagi", "key", false),
        new("umbrella", "傘", "かさ", "kasa", "umbrella", false),
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
