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

    private static readonly IReadOnlyList<Item> RecipeOutputItems =
    [
        new() { Id = "item-a", Name = "中間材", Category = "部品", VersionAdded = "1.0.0" },
        new() { Id = "item-b", Name = "加工素材", Category = "粉末素材", VersionAdded = "1.0.0" },
        new() { Id = "item-c", Name = "採取素材", Category = "鉱物", IsGatherable = true, VersionAdded = "1.0.0" },
    ];

    private static readonly IReadOnlyList<Recipe> Recipes =
    [
        new()
        {
            Id = "recipe-a",
            Name = "中間材レシピ",
            VersionAdded = "1.0.0",
            Outputs = [new() { ItemId = "item-a", Quantity = 1 }],
            Facilities = [],
        },
    ];

    // IC-08: レシピの成果物として登場するアイテムのみを返す（仕様決定 AR）。
    [Fact]
    public void WithRecipeOutputReturnsOutputItemsOnly()
    {
        IReadOnlyList<Item> result = ItemCatalog.WithRecipeOutput(RecipeOutputItems, Recipes);

        Assert.Equal(["item-a"], result.Select(i => i.Id));
    }

    // IC-09: 採取素材でもレシピの成果物に登場しなければ候補に含めない（仕様決定 AR）。
    [Fact]
    public void WithRecipeOutputExcludesGatherableWithoutRecipe()
    {
        IReadOnlyList<Item> result = ItemCatalog.WithRecipeOutput(RecipeOutputItems, Recipes);

        Assert.DoesNotContain(result, i => i.Id == "item-c");
    }

    // IC-10: 継続消費系の母集団は「気体」「液体」カテゴリのみ（仕様決定 AR）。
    [Fact]
    public void FuelItemsReturnsGasAndLiquidOnly()
    {
        IReadOnlyList<Item> fuelCandidates =
        [
            new() { Id = "item-gas", Name = "不活性ガス", Category = "気体", VersionAdded = "1.0.0" },
            new() { Id = "item-water", Name = "水", Category = "液体", VersionAdded = "1.0.0" },
            new() { Id = "item-ore", Name = "鉄鉱石", Category = "鉱物", VersionAdded = "1.0.0" },
        ];

        Assert.Equal(["item-gas", "item-water"], ItemCatalog.FuelItems(fuelCandidates).Select(i => i.Id));
    }

    // IC-11: 母集団外の現在値も検索先を指定すれば末尾に残す（仕様決定 AR）。
    [Fact]
    public void OptionsKeepCurrentFromLookupItemsAtEnd()
    {
        IReadOnlyList<Item> fuelItems = ItemCatalog.FuelItems(Items).ToList();
        IReadOnlyList<Item> result = ItemCatalog.OptionsForSelection(fuelItems, null, "item-ore", Items);

        Assert.Equal("item-ore", result.Last().Id);
    }
}
