using EndfieldAicWeb.Application;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

/// <summary>IC: 管理ツールのアイテム選択補助（カテゴリ列挙と絞り込み）。</summary>
public class ItemCatalogTests
{
    private static readonly IReadOnlyList<Item> Items =
    [
        new() { Id = "item-ore", Name = "鉄鉱石", Category = "鉱物", VersionAdded = "1.0.0" },
        new() { Id = "item-part", Name = "汎用部品", Category = "部品", VersionAdded = "1.0.0" },
        new() { Id = "item-coal", Name = "石炭", Category = "鉱物", VersionAdded = "1.0.0" },
        new() { Id = "item-uncategorized", Name = "未分類品", Category = "", VersionAdded = "1.0.0" },
    ];

    // IC-01: カテゴリ列挙は重複を除き初出順で返す。
    [Fact]
    public void CategoriesAreDistinctInFirstAppearanceOrder()
    {
        Assert.Equal(["鉱物", "部品"], ItemCatalog.Categories(Items));
    }

    // IC-02: 空カテゴリは列挙に含めない。
    [Fact]
    public void CategoriesExcludeEmpty()
    {
        Assert.DoesNotContain("", ItemCatalog.Categories(Items));
    }

    // IC-03: 未絞り込み（null・空文字）は全件返す。
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void UnfilteredReturnsAll(string? category)
    {
        Assert.Equal(Items.Count, ItemCatalog.FilterByCategory(Items, category).Count);
    }

    // IC-04: カテゴリ指定は一致するアイテムだけを返す。
    [Fact]
    public void FilterByCategoryReturnsMatchingOnly()
    {
        IReadOnlyList<Item> result = ItemCatalog.FilterByCategory(Items, "鉱物");

        Assert.Equal(["item-ore", "item-coal"], result.Select(i => i.Id));
    }

    // IC-05: 絞り込み条件に合わない現在値は選択肢の末尾に残す。
    [Fact]
    public void OptionsKeepCurrentItemAtEndWhenFilteredOut()
    {
        IReadOnlyList<Item> result = ItemCatalog.OptionsForSelection(Items, "鉱物", "item-part");

        Assert.Equal(["item-ore", "item-coal", "item-part"], result.Select(i => i.Id));
    }

    // IC-06: 絞り込み条件に合う現在値は重複して追加しない。
    [Fact]
    public void OptionsDoNotDuplicateCurrentItemWhenMatching()
    {
        IReadOnlyList<Item> result = ItemCatalog.OptionsForSelection(Items, "鉱物", "item-ore");

        Assert.Equal(["item-ore", "item-coal"], result.Select(i => i.Id));
    }

    // IC-07: 現在値が未選択なら絞り込み結果のみを返す。
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void OptionsWithoutCurrentReturnFilteredOnly(string? currentItemId)
    {
        IReadOnlyList<Item> result = ItemCatalog.OptionsForSelection(Items, "鉱物", currentItemId);

        Assert.Equal(["item-ore", "item-coal"], result.Select(i => i.Id));
    }
}
