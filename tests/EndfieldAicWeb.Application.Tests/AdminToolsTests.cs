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
            ApplicationFixtures.Item("i-ore", "原鉱石"),
            ApplicationFixtures.Item("i-gas", "活性ガス", TransportKind.Pipe),
            ApplicationFixtures.Item("i-part", "汎用部品", eventId: "ev-on"),
            ApplicationFixtures.Item("i-fc", "固定素材"),
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
        MasterValidator.ValidateAll(doc.Items, doc.Facilities, doc.Environments, doc.GameEvents, doc.Recipes, errors);
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

    // VER: DataVersion 提案

    [Theory]
    [InlineData("0.1.0", "0.1.1")]
    [InlineData("1.9.9", "1.9.10")]
    [InlineData("10.20.30", "10.20.31")]
    public void VER_semver形式はpatchを1上げる(string current, string expected)
    {
        Assert.Equal(expected, DataVersionBumper.SuggestNext(current));
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("v1.0.0")]
    [InlineData("1.0.0-beta")]
    [InlineData("1.0.0.1")]
    [InlineData("")]
    public void VER_非semver形式は提案しない(string current)
    {
        Assert.Null(DataVersionBumper.SuggestNext(current));
    }

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
}
