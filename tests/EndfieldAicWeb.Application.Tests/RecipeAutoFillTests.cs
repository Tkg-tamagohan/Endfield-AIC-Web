using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

/// <summary>
/// docs/phases/test-specification-phase29.md §1 のテスト項目（RCP）に対応する。
/// レシピの Id・名前の自動提案（仕様決定 BU）の提案値と追随・固定・適用を検査する。
/// </summary>
public class RecipeAutoFillTests
{
    private static RecipeOutput Out(string itemId, int sortOrder = 0, double quantity = 1) =>
        new() { ItemId = itemId, Quantity = quantity, SortOrder = sortOrder };

    private static Recipe Rcp(string id, string name, params RecipeOutput[] outputs) =>
        new()
        {
            Id = id,
            Name = name,
            VersionAdded = "1.0.0",
            Outputs = [.. outputs],
            Facilities = [ApplicationFixtures.Pair("f-asm", 2, recipeId: id)],
        };

    /// <summary>提案系の最小文書。item-carbon=炭塊・item-cuprium=赤銅塊・item-xiranite=息壌・item-sewage=汚水。</summary>
    private static MasterDocument Doc(params Recipe[] recipes) => new()
    {
        SchemaVersion = 1,
        DataVersion = "0.1.0",
        Items =
        [
            ApplicationFixtures.Item("item-carbon", "炭塊"),
            ApplicationFixtures.Item("item-cuprium", "赤銅塊"),
            ApplicationFixtures.Item("item-xiranite", "息壌"),
            ApplicationFixtures.Item("item-sewage", "汚水"),
        ],
        Facilities = [ApplicationFixtures.Facility("f-asm", "加工機", 10)],
        Recipes = [.. recipes],
    };

    [Fact]
    public void RCP01_提案値はSortOrder最小行のアイテムから採る_同率は先頭行()
    {
        // SortOrder 最小行が主産物
        Recipe minWins = Rcp("recipe-a", "手入力名", Out("item-xiranite", 1), Out("item-carbon", 2));
        // 同率は先頭行が主産物
        Recipe tieFirstWins = Rcp("recipe-b", "手入力名", Out("item-carbon"), Out("item-xiranite"));
        MasterDocument doc = Doc(minWins, tieFirstWins);

        (string Id, string Name)? s1 = RecipeAutoFill.Suggest(doc, minWins);
        Assert.Equal("recipe-xiranite", s1?.Id);
        Assert.Equal("息壌", s1?.Name);

        (string Id, string Name)? s2 = RecipeAutoFill.Suggest(doc, tieFirstWins);
        Assert.Equal("recipe-carbon", s2?.Id);
        Assert.Equal("炭塊", s2?.Name);
    }

    [Fact]
    public void RCP02_直近提案値と一致する間は主産物変更へ追随する()
    {
        Recipe recipe = Rcp("recipe-cuprium", "赤銅塊", Out("item-cuprium"));
        MasterDocument doc = Doc(recipe);
        var fill = new RecipeAutoFill();
        fill.Reset(doc, recipe);

        recipe.Outputs[0].ItemId = "item-carbon";
        bool idChanged = fill.Follow(doc, recipe);

        Assert.True(idChanged);
        Assert.Equal("recipe-carbon", recipe.Id);
        Assert.Equal("炭塊", recipe.Name);
    }

    [Fact]
    public void RCP03_手動編集したフィールドだけ固定される()
    {
        Recipe recipe = Rcp("recipe-cuprium", "赤銅塊", Out("item-cuprium"));
        MasterDocument doc = Doc(recipe);
        var fill = new RecipeAutoFill();
        fill.Reset(doc, recipe);

        recipe.Name = "手入力名";
        recipe.Outputs[0].ItemId = "item-carbon";
        fill.Follow(doc, recipe);

        // 名前は固定のまま、Id だけ追随する
        Assert.Equal("recipe-carbon", recipe.Id);
        Assert.Equal("手入力名", recipe.Name);
    }

    [Fact]
    public void RCP04_提案値と同じ値を入れ直すと追随へ復帰する()
    {
        Recipe recipe = Rcp("recipe-cuprium", "赤銅塊", Out("item-cuprium"));
        MasterDocument doc = Doc(recipe);
        var fill = new RecipeAutoFill();
        fill.Reset(doc, recipe);

        recipe.Name = "手入力名";
        recipe.Outputs[0].ItemId = "item-carbon";
        fill.Follow(doc, recipe);
        Assert.Equal("手入力名", recipe.Name);

        // 提案値（炭塊）と同じ値を入れ直すと、そのフィールドは追随へ復帰する
        recipe.Name = "炭塊";
        recipe.Outputs[0].ItemId = "item-xiranite";
        fill.Follow(doc, recipe);

        Assert.Equal("recipe-xiranite", recipe.Id);
        Assert.Equal("息壌", recipe.Name);
    }

    [Fact]
    public void RCP05_提案適用で手動編集後の両フィールドが提案値へ戻る()
    {
        Recipe recipe = Rcp("recipe-cuprium", "赤銅塊", Out("item-cuprium"));
        MasterDocument doc = Doc(recipe);
        var fill = new RecipeAutoFill();
        fill.Reset(doc, recipe);

        recipe.Id = "recipe-custom";
        recipe.Name = "手入力名";
        bool idChanged = fill.ForceApply(doc, recipe);

        Assert.True(idChanged);
        Assert.Equal("recipe-cuprium", recipe.Id);
        Assert.Equal("赤銅塊", recipe.Name);

        // 適用後は提案値と一致するため追随状態へ復帰する
        recipe.Outputs[0].ItemId = "item-carbon";
        fill.Follow(doc, recipe);
        Assert.Equal("recipe-carbon", recipe.Id);
        Assert.Equal("炭塊", recipe.Name);
    }

    [Fact]
    public void RCP06_提案を計算できないとき現在値を維持する()
    {
        Recipe empty = Rcp("recipe-empty", "出力なし");
        Recipe unknown = Rcp("recipe-unknown", "未知の主産物", Out("i-nothing"));
        MasterDocument doc = Doc(empty, unknown);
        var fill = new RecipeAutoFill();
        fill.Reset(doc, empty);
        fill.Reset(doc, unknown);

        Assert.Null(RecipeAutoFill.Suggest(doc, empty));
        Assert.Null(RecipeAutoFill.Suggest(doc, unknown));
        Assert.False(fill.Follow(doc, empty));
        Assert.False(fill.Follow(doc, unknown));
        Assert.False(fill.ForceApply(doc, empty));

        Assert.Equal("recipe-empty", empty.Id);
        Assert.Equal("出力なし", empty.Name);
        Assert.Equal("recipe-unknown", unknown.Id);
        Assert.Equal("未知の主産物", unknown.Name);
    }

    [Fact]
    public void RCP07_衝突判定は編集中レシピ自身のIdを除く()
    {
        Recipe recipe = Rcp("recipe-cuprium", "赤銅塊", Out("item-cuprium"));
        MasterDocument doc = Doc(recipe);

        // 自身の recipe-cuprium を衝突とみなさない（recipe-cuprium01 へずれない）
        Assert.Equal("recipe-cuprium", RecipeAutoFill.Suggest(doc, recipe)?.Id);

        var fill = new RecipeAutoFill();
        fill.Reset(doc, recipe);
        recipe.Outputs[0].ItemId = "item-carbon";
        fill.Follow(doc, recipe);
        Assert.Equal("recipe-carbon", recipe.Id);
    }

    [Fact]
    public void RCP08_主産物の入れ替わりでも追随判定の対象になる()
    {
        Recipe recipe = Rcp("recipe-cuprium", "赤銅塊", Out("item-cuprium", 0), Out("item-sewage", 1));
        MasterDocument doc = Doc(recipe);
        var fill = new RecipeAutoFill();
        fill.Reset(doc, recipe);

        // SortOrder の変更で主産物が入れ替わる
        recipe.Outputs[1].SortOrder = -1;
        fill.Follow(doc, recipe);
        Assert.Equal("recipe-sewage", recipe.Id);
        Assert.Equal("汚水", recipe.Name);

        // 出力行の削除で主産物が戻る
        recipe.Outputs.RemoveAt(1);
        fill.Follow(doc, recipe);
        Assert.Equal("recipe-cuprium", recipe.Id);
        Assert.Equal("赤銅塊", recipe.Name);

        // 出力行の追加で主産物が入れ替わる
        recipe.Outputs.Add(Out("item-xiranite", -1));
        fill.Follow(doc, recipe);
        Assert.Equal("recipe-xiranite", recipe.Id);
        Assert.Equal("息壌", recipe.Name);
    }

    [Fact]
    public void RCP09_選択切替で直近提案値が現在の提案値へ初期化される()
    {
        Recipe a = Rcp("recipe-cuprium", "赤銅塊", Out("item-cuprium"));
        Recipe b = Rcp("recipe-carbon", "炭塊", Out("item-carbon"));
        MasterDocument doc = Doc(a, b);
        var fill = new RecipeAutoFill();
        fill.Reset(doc, a);

        // b へ切り替えると直近提案値は b 自身の提案値になる（a の提案値を引きずらない）
        fill.Reset(doc, b);
        b.Outputs[0].ItemId = "item-xiranite";
        fill.Follow(doc, b);

        Assert.Equal("recipe-xiranite", b.Id);
        Assert.Equal("息壌", b.Name);
    }

    [Fact]
    public void RCP10_別レシピと同じ主産物への変更で追随したIdは連番になる()
    {
        Recipe existing = Rcp("recipe-carbon", "炭塊", Out("item-carbon"));
        Recipe editing = Rcp("recipe-cuprium", "赤銅塊", Out("item-cuprium"));
        MasterDocument doc = Doc(existing, editing);
        var fill = new RecipeAutoFill();
        fill.Reset(doc, editing);

        editing.Outputs[0].ItemId = "item-carbon";
        fill.Follow(doc, editing);

        Assert.Equal("recipe-carbon01", editing.Id);
        Assert.Equal("炭塊", editing.Name);
    }

    // PR #86 Devin Review 対応の回帰テスト: 空の文書で作ったレシピの仮採番は
    // 最初に提案を計算できた時点で提案値へ置き換わる。ただし置き換えるのは
    // 工場が生成時に採番した値そのものだけで、同形式を手入力した Id や
    // ファクトリを通らない既存レシピは固定のままにする

    [Fact]
    public void RCP13_提案未計算の仮採番は最初の提案で置き換わる()
    {
        MasterDocument doc = Doc();
        Recipe placeholder = EntityFactory.NewRecipe("recipe-001");
        Recipe edited = EntityFactory.NewRecipe("recipe-002");
        Recipe manual = Rcp("recipe-123", "手入力名");
        doc.Recipes.Add(placeholder);
        doc.Recipes.Add(edited);
        doc.Recipes.Add(manual);

        var fill = new RecipeAutoFill();

        // 選択時は outputs 未解決で提案なし（_last = null）。その後に出力を足すと追随する
        fill.Reset(doc, placeholder);
        placeholder.Outputs.Add(Out("item-carbon"));
        Assert.True(fill.Follow(doc, placeholder));
        Assert.Equal("recipe-carbon", placeholder.Id);
        Assert.Equal("炭塊", placeholder.Name);

        // 生成時の採番値から編集された Id は置き換えない（名前は未編集なので追随する）
        fill.Reset(doc, edited);
        edited.Id = "recipe-123";
        edited.Outputs.Add(Out("item-xiranite"));
        Assert.False(fill.Follow(doc, edited));
        Assert.Equal("recipe-123", edited.Id);
        Assert.Equal("息壌", edited.Name);

        // ファクトリを通らない既存レシピの同形式 Id・名前は固定のまま置き換えない
        fill.Reset(doc, manual);
        manual.Outputs.Add(Out("item-sewage"));
        Assert.False(fill.Follow(doc, manual));
        Assert.Equal("recipe-123", manual.Id);
        Assert.Equal("手入力名", manual.Name);
    }
}
