using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;
using DomainEnvironment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Application.Tests;

/// <summary>
/// docs/phases/test-specification-phase6.md §3 のテスト項目（IDF・ENT・VER・REF）に対応する。
/// </summary>
public class AdminToolsTests
{
    /// <summary>M-01: 参照を含む最小文書。</summary>
    private static MasterDocument M01() => new()
    {
        SchemaVersion = 1,
        DataVersion = "0.1.0",
        Items =
        [
            ApplicationFixtures.Item("i-ore", "原鉱石", gatherable: true),
            ApplicationFixtures.Item("i-gas", "活性ガス", TransportKind.Pipe, gatherable: true),
            ApplicationFixtures.Item("i-part", "汎用部品", eventId: "ev-on"),
            ApplicationFixtures.Item("i-fc", "固定素材", gatherable: true),
        ],
        Facilities =
        [
            ApplicationFixtures.Facility("f-asm", "加工機", 50),
            ApplicationFixtures.Facility("f-disp", "散布機", 20),
        ],
        Environments =
        [
            ApplicationFixtures.Env("env-gas", "ガス環境", "f-disp", "i-gas", 360, "ev-on"),
        ],
        GameEvents =
        [
            ApplicationFixtures.Event("ev-on", "開催イベント"),
        ],
        Recipes =
        [
            ApplicationFixtures.Recipe(
                "r-part", "汎用部品",
                [("i-ore", 2)],
                [("i-part", 1)],
                [
                    ApplicationFixtures.Pair("f-asm", 4),
                    ApplicationFixtures.Pair("f-asm", 3, "env-gas",
                        new FixedConsumption { ItemId = "i-fc", RatePerMinute = 60 }),
                ],
                "ev-on"),
        ],
    };

    private static List<MasterValidationError> Validate(MasterDocument doc)
    {
        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateAll(
            doc.Items, doc.Facilities, doc.Environments, doc.GameEvents, doc.Recipes, doc.Maps, errors);
        return errors;
    }

    // IDF: 新規 Id の採番

    [Fact]
    public void IDF01_空の一覧なら連番の先頭を返す()
    {
        Assert.Equal("item-001", EntityFactory.SuggestId([], "item"));
    }

    [Fact]
    public void IDF02_使用中の末尾の次を返す()
    {
        Assert.Equal("item-003", EntityFactory.SuggestId(["item-001", "item-002"], "item"));
    }

    [Fact]
    public void IDF03_欠番を埋める()
    {
        Assert.Equal("item-002", EntityFactory.SuggestId(["item-001", "item-003"], "item"));
    }

    [Fact]
    public void IDF04_接頭辞ごとに独立して採番する()
    {
        Assert.Equal("fac-001", EntityFactory.SuggestId(["item-001"], "fac"));
    }

    [Fact]
    public void IDF05_3桁ゼロ埋めのまま桁が繰り上がる()
    {
        string[] taken = Enumerable.Range(1, 9).Select(i => $"item-{i:000}").ToArray();
        Assert.Equal("item-010", EntityFactory.SuggestId(taken, "item"));
    }

    // Phase 29 テスト仕様（仕様決定 BU）: レシピ Id のスラッグ採番

    [Fact]
    public void IDF06_レシピIdはItemId先頭のitem接頭辞を除いたslugで採番する()
    {
        Assert.Equal("recipe-xiranite", EntityFactory.SuggestRecipeId([], "item-xiranite"));
    }

    [Fact]
    public void IDF07_レシピIdが衝突するとslugに2桁連番で最初の空きを返す()
    {
        Assert.Equal("recipe-carbon01", EntityFactory.SuggestRecipeId(["recipe-carbon"], "item-carbon"));
    }

    [Fact]
    public void IDF08_連番の途中の欠番を埋める()
    {
        Assert.Equal(
            "recipe-carbon02",
            EntityFactory.SuggestRecipeId(["recipe-carbon", "recipe-carbon01", "recipe-carbon03"], "item-carbon"));
    }

    [Fact]
    public void IDF09_item接頭辞のないItemIdは全体をslugに使う()
    {
        Assert.Equal("recipe-x-foo", EntityFactory.SuggestRecipeId([], "x-foo"));
    }

    // ENT: 新規エンティティの既定値

    [Fact]
    public void ENT01_新規アイテムは検証を通る()
    {
        MasterDocument doc = M01();
        Item item = EntityFactory.NewItem("item-001", doc.DataVersion);
        doc.Items.Add(item);

        Assert.DoesNotContain(Validate(doc), e => e.EntityKind == "Item" && e.EntityId == item.Id);
    }

    [Fact]
    public void ENT02_新規設備は検証を通る()
    {
        MasterDocument doc = M01();
        Facility facility = EntityFactory.NewFacility("fac-001", doc.DataVersion);
        doc.Facilities.Add(facility);

        Assert.DoesNotContain(Validate(doc), e => e.EntityKind == "Facility" && e.EntityId == facility.Id);
    }

    [Fact]
    public void ENT03_参照先を渡した新規環境は検証を通る()
    {
        MasterDocument doc = M01();
        DomainEnvironment env = EntityFactory.NewEnvironment("env-001", doc.DataVersion, "f-disp", "i-gas");
        doc.Environments.Add(env);

        Assert.DoesNotContain(Validate(doc), e => e.EntityKind == "Environment" && e.EntityId == env.Id);
    }

    [Fact]
    public void ENT04_参照先がない新規環境は必須違反として残る()
    {
        MasterDocument doc = M01();
        DomainEnvironment env = EntityFactory.NewEnvironment("env-001", doc.DataVersion, null, null);
        doc.Environments.Add(env);

        Assert.Contains(Validate(doc), e => e.EntityKind == "Environment" && e.EntityId == env.Id);
    }

    [Fact]
    public void ENT05_新規イベントは常設で検証を通る()
    {
        MasterDocument doc = M01();
        GameEvent gameEvent = EntityFactory.NewGameEvent("ev-001", doc.DataVersion);
        doc.GameEvents.Add(gameEvent);

        Assert.Null(gameEvent.ActiveFrom);
        Assert.Null(gameEvent.ActiveTo);
        Assert.DoesNotContain(Validate(doc), e => e.EntityKind == "GameEvent" && e.EntityId == gameEvent.Id);
    }

    [Fact]
    public void ENT06_参照先を渡した新規レシピは検証を通る()
    {
        MasterDocument doc = M01();
        Recipe recipe = EntityFactory.NewRecipe("recipe-001", doc.DataVersion, "i-part", "f-asm");
        // Inputs も 1 件以上必要（仕様決定 BZ）。工場生成は行を持たないため参照先の入力行を足す。
        recipe.Inputs.Add(new RecipeInput { ItemId = "i-ore", Quantity = 1 });
        doc.Recipes.Add(recipe);

        Assert.Single(recipe.Outputs);
        Assert.Single(recipe.Facilities);
        Assert.All(recipe.Facilities, pair => Assert.Equal(recipe.Id, pair.RecipeId));
        Assert.DoesNotContain(Validate(doc), e => e.EntityKind == "Recipe" && e.EntityId == recipe.Id);
    }

    [Fact]
    public void ENT07_参照先がない新規レシピは行数不足の違反として残る()
    {
        MasterDocument doc = M01();
        Recipe recipe = EntityFactory.NewRecipe("recipe-001", doc.DataVersion, null, null);
        doc.Recipes.Add(recipe);

        Assert.Contains(Validate(doc), e =>
            e.EntityKind == "Recipe" && e.EntityId == recipe.Id
            && (e.Field == "Outputs" || e.Field == "Facilities"));
    }

    [Fact]
    public void ENT08_VersionAdded省略時の既定は1_0_0()
    {
        // Phase 20 テスト仕様 ENT-08（仕様決定 AV）
        Assert.Equal("1.0.0", EntityFactory.NewItem("item-x").VersionAdded);
    }

    [Fact]
    public void ENT09_新規レシピの初期ペアのサイクル秒は2()
    {
        // Phase 20 テスト仕様 ENT-09（仕様決定 AW）
        Recipe recipe = EntityFactory.NewRecipe("recipe-x", outputItemId: "i-part", facilityId: "f-asm");

        RecipeFacility pair = Assert.Single(recipe.Facilities);
        Assert.Equal(2, pair.CycleTime);
    }

    [Fact]
    public void ENT10_VersionAdded既定値は種別を問わず1_0_0()
    {
        // Phase 20 テスト仕様 ENT-10（仕様決定 AV）
        Assert.Equal("1.0.0", EntityFactory.NewFacility("fac-x").VersionAdded);
        Assert.Equal("1.0.0", EntityFactory.NewEnvironment("env-x").VersionAdded);
        Assert.Equal("1.0.0", EntityFactory.NewGameEvent("ev-x").VersionAdded);
        Assert.Equal("1.0.0", EntityFactory.NewGameMap("map-x").VersionAdded);
        Assert.Equal("1.0.0", EntityFactory.NewRecipe("recipe-x").VersionAdded);
    }

    // VER: DataVersion 提案

    [Theory]
    [InlineData("0.1.0", "0.1.1")] // VER-01
    [InlineData("1.9.9", "1.9.10")] // VER-02
    [InlineData("10.20.30", "10.20.31")] // VER-03
    public void VER_semver形式はpatchを1上げる(string current, string expected)
    {
        Assert.Equal(expected, DataVersionBumper.SuggestNext(current));
    }

    [Theory]
    [InlineData("1.0")] // VER-04
    [InlineData("v1.0.0")] // VER-04
    [InlineData("1.0.0-beta")] // VER-04
    [InlineData("1.0.0.1")] // VER-04
    [InlineData("")] // VER-04
    public void VER_非semver形式は提案しない(string current)
    {
        Assert.Null(DataVersionBumper.SuggestNext(current));
    }

    // VER-04（null 入力）
    [Fact]
    public void VER_nullは提案しない()
    {
        Assert.Null(DataVersionBumper.SuggestNext(null));
    }

    // REF: 削除前の参照列挙

    [Fact]
    public void REF01_アイテムの入力参照を列挙する()
    {
        Assert.Equal(
            new MasterReference[] { new MasterReference("Recipe", "r-part", "Inputs[0].ItemId")},
            MasterReferenceFinder.FindItemReferences(M01(), "i-ore"));
    }

    [Fact]
    public void REF02_アイテムの出力参照を列挙する()
    {
        Assert.Equal(
            new MasterReference[] { new MasterReference("Recipe", "r-part", "Outputs[0].ItemId")},
            MasterReferenceFinder.FindItemReferences(M01(), "i-part"));
    }

    [Fact]
    public void REF03_環境消費と固定消費のアイテム参照を列挙する()
    {
        Assert.Equal(
            new MasterReference[] { new MasterReference("Environment", "env-gas", "ConsumeItemId")},
            MasterReferenceFinder.FindItemReferences(M01(), "i-gas"));
        Assert.Equal(
            new MasterReference[] { new MasterReference("Recipe", "r-part", "Facilities[1].FixedConsumption.ItemId")},
            MasterReferenceFinder.FindItemReferences(M01(), "i-fc"));
    }

    [Fact]
    public void REF04_設備の参照を列挙する()
    {
        Assert.Equal(
            new MasterReference[] { new MasterReference("Environment", "env-gas", "ProviderFacilityId")},
            MasterReferenceFinder.FindFacilityReferences(M01(), "f-disp"));
        Assert.Equal(
            new MasterReference[]
            {
                new MasterReference("Recipe", "r-part", "Facilities[0].FacilityId"),
                new MasterReference("Recipe", "r-part", "Facilities[1].FacilityId"),
            },
            MasterReferenceFinder.FindFacilityReferences(M01(), "f-asm"));
    }

    [Fact]
    public void REF05_環境の参照を列挙する()
    {
        Assert.Equal(
            new MasterReference[] { new MasterReference("Recipe", "r-part", "Facilities[1].EnvironmentId")},
            MasterReferenceFinder.FindEnvironmentReferences(M01(), "env-gas"));
    }

    [Fact]
    public void REF06_イベントの参照を列挙する()
    {
        Assert.Equal(
            new MasterReference[]
            {
                new MasterReference("Item", "i-part", "GameEventId"),
                new MasterReference("Environment", "env-gas", "GameEventId"),
                new MasterReference("Recipe", "r-part", "GameEventId"),
            },
            MasterReferenceFinder.FindGameEventReferences(M01(), "ev-on"));
    }

    [Fact]
    public void REF07_参照のないIdは空を返す()
    {
        MasterDocument doc = M01();
        Assert.Empty(MasterReferenceFinder.FindItemReferences(doc, "i-unknown"));
        Assert.Empty(MasterReferenceFinder.FindFacilityReferences(doc, "f-unknown"));
        Assert.Empty(MasterReferenceFinder.FindEnvironmentReferences(doc, "env-unknown"));
        Assert.Empty(MasterReferenceFinder.FindGameEventReferences(doc, "ev-unknown"));
    }

    // RCP-11/12: 出力なしレシピの自動提案除外（docs/phases/test-specification-phase32.md §5、仕様決定 CE）。

    [Fact]
    public void RCP11_出力0件のレシピは提案を計算しない()
    {
        Recipe recipe = ApplicationFixtures.Recipe(
            "r-disp", "汚水処理",
            [("i-ore", 1)],
            [],
            [ApplicationFixtures.Pair("f-asm", 4)]);

        Assert.Null(RecipeAutoFill.Suggest(M01(), recipe));
    }

    [Fact]
    public void RCP12_出力を追加すれば提案の対象に戻る()
    {
        Recipe recipe = ApplicationFixtures.Recipe(
            "r-disp", "汚水処理",
            [("i-ore", 1)],
            [("i-part", 1)],
            [ApplicationFixtures.Pair("f-asm", 4)]);

        Assert.Equal(("recipe-i-part", "汎用部品"), RecipeAutoFill.Suggest(M01(), recipe));
    }
}
