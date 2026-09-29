using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using F = EndfieldAicWeb.Domain.Tests.CalculationFixtures;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>SNP: スナップショットの不変性（構築時点でコレクションと索引を確定）。</summary>
public class SnapshotImmutabilityTests
{
    // SNP-03: 構築後に元のリストを変更しても、スナップショットのコレクションと索引は構築時点の内容を保持する。
    [Fact]
    public void SnapshotIsFrozenAtConstruction()
    {
        var items = new List<Item> { F.Item("i-1") };
        var facilities = new List<Facility> { F.Facility("f-1") };
        var environments = new List<Environment> { F.Env("env-1", "f-1", "i-1", 1.0) };
        var gameEvents = new List<GameEvent> { F.GameEvent("ev-1") };
        var recipes = new List<Recipe>
        {
            F.Recipe("r-1", "f-1", 4.0, [("i-1", 1.0)], [("i-2", 1.0)]),
        };

        var snapshot = new MasterDataSnapshot
        {
            Items = items,
            Facilities = facilities,
            Environments = environments,
            GameEvents = gameEvents,
            Recipes = recipes,
        };

        // 構築後に元リストを入れ替える（削除＋新規追加）。
        items.Clear();
        items.Add(F.Item("i-new"));
        facilities.Clear();
        environments.Clear();
        gameEvents.Clear();
        recipes.Clear();
        recipes.Add(F.Recipe("r-new", "f-1", 4.0, [("i-1", 1.0)], [("i-new", 1.0)]));

        Item item = Assert.Single(snapshot.Items);
        Assert.Equal("i-1", item.Id);
        Assert.True(snapshot.ItemsById.ContainsKey("i-1"));
        Assert.False(snapshot.ItemsById.ContainsKey("i-new"));

        Assert.Single(snapshot.Facilities);
        Assert.True(snapshot.FacilitiesById.ContainsKey("f-1"));

        Assert.Single(snapshot.Environments);
        Assert.True(snapshot.EnvironmentsById.ContainsKey("env-1"));

        Assert.Single(snapshot.GameEvents);

        Recipe recipe = Assert.Single(snapshot.Recipes);
        Assert.Equal("r-1", recipe.Id);
        Assert.True(snapshot.RecipesById.ContainsKey("r-1"));
        Assert.False(snapshot.RecipesById.ContainsKey("r-new"));

        KeyValuePair<string, IReadOnlyList<Recipe>> byOutput = Assert.Single(snapshot.RecipesByOutputItemId);
        Assert.Equal("i-2", byOutput.Key);
        Assert.Equal("r-1", Assert.Single(byOutput.Value).Id);

        // 公開コレクションは実行時型へのキャストでも変更できない。
        Assert.Throws<InvalidCastException>(() => _ = (Item[])snapshot.Items);
        Assert.Throws<InvalidCastException>(() => _ = (Dictionary<string, Item>)snapshot.ItemsById);
        Assert.Throws<InvalidCastException>(() => _ = (List<Recipe>)byOutput.Value);
        Assert.Throws<InvalidCastException>(() =>
            _ = (Dictionary<string, IReadOnlyList<Recipe>>)snapshot.RecipesByOutputItemId);
    }
}
