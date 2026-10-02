using EndfieldAicWeb.Application;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

/// <summary>FIL: 生産リストのアイテム検索。</summary>
public class ItemSearchTests
{
    private static readonly IReadOnlyList<Item> Items =
    [
        new() { Id = "item-part", Name = "汎用部品", Category = "", VersionAdded = "1.0.0" },
        new() { Id = "item-part-hp", Name = "高純度部品", Category = "", VersionAdded = "1.0.0" },
        new() { Id = "item-ore", Name = "原鉱石", Category = "", VersionAdded = "1.0.0" },
        new() { Id = "item-frame", Name = "強化フレーム", Category = "", VersionAdded = "1.0.0" },
    ];

    // FIL-01: 空クエリは全件返す。
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyQueryReturnsAll(string? query)
    {
        Assert.Equal(Items.Count, ItemSearch.Filter(Items, query).Count);
    }

    // FIL-02: 名前の部分一致（大小文字を区別しない）。
    [Fact]
    public void MatchesByNameSubstring()
    {
        IReadOnlyList<Item> result = ItemSearch.Filter(Items, "部品");

        Assert.Equal(2, result.Count);
        Assert.All(result, i => Assert.Contains("部品", i.Name));
    }

    // FIL-03: Id の部分一致も名前と同じ候補に載る。
    [Fact]
    public void MatchesByIdSubstring()
    {
        IReadOnlyList<Item> result = ItemSearch.Filter(Items, "part");

        Assert.Equal(2, result.Count);
        Assert.All(result, i => Assert.Contains("part", i.Id));
    }

    // FIL-03b: 大小文字を区別しない。
    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        IReadOnlyList<Item> result = ItemSearch.Filter(Items, "PART");

        Assert.Equal(2, result.Count);
    }

    // FIL-04: どのアイテムにも一致しないクエリは空を返す。
    [Fact]
    public void NoMatchReturnsEmpty()
    {
        Assert.Empty(ItemSearch.Filter(Items, "存在しない"));
    }

    private static readonly IReadOnlyList<Item> CategorizedItems =
    [
        new() { Id = "item-part", Name = "汎用部品", Category = "部品", VersionAdded = "1.0.0" },
        new() { Id = "item-part-hp", Name = "高純度部品", Category = "部品", VersionAdded = "1.0.0" },
        new() { Id = "item-ore", Name = "原鉱石", Category = "鉱物", VersionAdded = "1.0.0" },
        new() { Id = "item-ore-part", Name = "鉱石部品", Category = "鉱物", VersionAdded = "1.0.0" },
    ];

    // FIL-05: カテゴリとクエリは AND で合成する。
    [Fact]
    public void FilterCombinesCategoryAndQuery()
    {
        IReadOnlyList<Item> result = ItemSearch.Filter(CategorizedItems, "部品", "鉱物");

        Assert.Equal(["item-ore-part"], result.Select(i => i.Id));
    }

    // FIL-06: クエリ空・カテゴリ指定はそのカテゴリのアイテム全件を返す。
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CategoryOnlyReturnsAllInCategory(string? query)
    {
        IReadOnlyList<Item> result = ItemSearch.Filter(CategorizedItems, query, "鉱物");

        Assert.Equal(["item-ore", "item-ore-part"], result.Select(i => i.Id));
    }

    // FIL-07: カテゴリ未指定はクエリ絞り込みのみで動く（回帰）。
    [Fact]
    public void NoCategoryBehavesAsBefore()
    {
        IReadOnlyList<Item> result = ItemSearch.Filter(CategorizedItems, "部品", null);

        Assert.Equal(3, result.Count);
        Assert.All(result, i => Assert.Contains("部品", i.Name));
    }
}
