using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;
using F = EndfieldAicWeb.Domain.Tests.CalculationFixtures;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>VAL: マスタ検証（docs/phases/test-specification-phase2.md §3）。</summary>
public class ValidationTests
{
    /// <summary>VAL-01 で使う最小構成の有効データ。</summary>
    private static (
        List<Item> Items,
        List<Facility> Facilities,
        List<Environment> Environments,
        List<GameEvent> GameEvents,
        List<Recipe> Recipes) ValidBaseline() => (
        [F.Item("i-ore", "基礎素材", TransportKind.Belt, null, true), F.Item("i-p")],
        [F.Facility("f-a"), F.Facility("f-disp")],
        [F.Env("env-g", "f-disp", "i-ore", 1.0)],
        [F.GameEvent("ev-1")],
        [
            F.Recipe("r-p", [F.Pair("r-p", "f-a", 4.0, "env-g")],
                [("i-ore", 1.0)], [("i-p", 1.0)]),
        ]);

    private static List<MasterValidationError> Errs(
        IReadOnlyList<Item> items,
        IReadOnlyList<Facility> facilities,
        IReadOnlyList<Environment> environments,
        IReadOnlyList<GameEvent> gameEvents,
        IReadOnlyList<Recipe> recipes)
    {
        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateAll(items, facilities, environments, gameEvents, recipes, errors);
        return errors;
    }

    private static List<MasterValidationError> Errs(
        (List<Item> Items, List<Facility> Facilities, List<Environment> Environments,
            List<GameEvent> GameEvents, List<Recipe> Recipes) doc) =>
        Errs(doc.Items, doc.Facilities, doc.Environments, doc.GameEvents, doc.Recipes);

    [Fact(DisplayName = "VAL-01: 正当なマスタはエラーなし")]
    public void ValidDocumentHasNoErrors()
    {
        Assert.Empty(Errs(ValidBaseline()));
    }

    [Fact(DisplayName = "VAL-02: 必須項目の欠落")]
    public void MissingRequiredFields()
    {
        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateItem(
            new Item { Id = "", Name = " ", Category = "", VersionAdded = "1.0.0" }, errors);
        MasterValidator.ValidateItem(
            new Item { Id = "i-1", Name = "n", Category = "部品", VersionAdded = "1.0.0" }, errors);

        Assert.Contains(errors, e => e.Field == "Id");
        Assert.Contains(errors, e => e.Field == "Name");
        Assert.Contains(errors, e => e.Field == "Category");
        Assert.Equal(3, errors.Count);
    }

    [Fact(DisplayName = "VAL-03: enum 定義値外")]
    public void UndefinedEnumValue()
    {
        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateItem(
            new Item
            {
                Id = "i-1", Name = "n", Category = "部品",
                TransportKind = (TransportKind)99, VersionAdded = "1.0.0",
            }, errors);

        Assert.Contains(errors, e => e.Field == "TransportKind");
    }

    [Fact(DisplayName = "VAL-04: 参照整合性")]
    public void DanglingReferences()
    {
        (List<Item> items, List<Facility> facilities, List<Environment> environments,
            List<GameEvent> gameEvents, List<Recipe> recipes) = ValidBaseline();

        items.Add(F.Item("i-ev", "部品", TransportKind.Belt, "ev-none"));
        environments.Add(F.Env("env-bad", "f-none", "i-none", 1.0, "ev-none"));
        recipes.Add(F.Recipe("r-bad", [
                F.Pair("r-bad", "f-none", 4.0, "env-none", ("i-none3", 1.0)),
            ],
            [("i-none", 1.0)], [("i-none2", 1.0)], gameEventId: "ev-none"));

        List<MasterValidationError> errors = Errs(items, facilities, environments, gameEvents, recipes);

        Assert.Contains(errors, e => e.Field == "Inputs" && e.Message.Contains("i-none"));
        Assert.Contains(errors, e => e.Field == "Outputs" && e.Message.Contains("i-none2"));
        Assert.Contains(errors, e => e.Field == "Facilities" && e.Message.Contains("f-none"));
        Assert.Contains(errors, e => e.Field == "Facilities" && e.Message.Contains("env-none"));
        Assert.Contains(errors, e => e.Field == "Facilities" && e.Message.Contains("i-none3"));
        Assert.Contains(errors, e => e.Field == "ProviderFacilityId");
        Assert.Contains(errors, e => e.Field == "ConsumeItemId");
        Assert.True(errors.Count(e => e.Field == "GameEventId") >= 3);
    }

    [Fact(DisplayName = "VAL-05: ペア一意性（P）")]
    public void DuplicatePairIsRejected()
    {
        Recipe duplicated = F.Recipe("r-dup", [
                F.Pair("r-dup", "f-a", 4.0, "env-g"),
                F.Pair("r-dup", "f-a", 4.0, "env-g"),
            ],
            [("i-ore", 1.0)], [("i-p", 1.0)]);
        Recipe distinct = F.Recipe("r-ok", [
                F.Pair("r-ok", "f-a", 4.0, "env-g"),
                F.Pair("r-ok", "f-a", 5.0, "env-g"),
            ],
            [("i-ore", 1.0)], [("i-p", 1.0)]);

        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateRecipe(duplicated, errors);
        MasterValidator.ValidateRecipe(distinct, errors);

        Assert.Single(errors);
        Assert.Equal("Facilities", errors[0].Field);
        Assert.Equal("r-dup", errors[0].EntityId);
    }

    [Fact(DisplayName = "VAL-06: ペアの RecipeId 整合")]
    public void PairRecipeIdMustMatchParent()
    {
        Recipe recipe = F.Recipe("r-1", [
                F.Pair("r-other", "f-a", 4.0),
            ],
            [("i-ore", 1.0)], [("i-p", 1.0)]);

        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateRecipe(recipe, errors);

        Assert.Contains(errors, e => e.Field == "Facilities[0].RecipeId");
    }

    [Fact(DisplayName = "VAL-07: 仮想アイテム規則")]
    public void VirtualItemCannotBeInput()
    {
        (List<Item> items, List<Facility> facilities, List<Environment> environments,
            List<GameEvent> gameEvents, List<Recipe> recipes) = ValidBaseline();

        items.Add(F.Item("i-virtual", "仮想", TransportKind.None));
        recipes.Add(F.Recipe("r-virt-in", "f-a", 4.0,
            [("i-virtual", 1.0)], [("i-p", 1.0)]));
        recipes.Add(F.Recipe("r-virt-out", "f-a", 4.0,
            [("i-ore", 1.0)], [("i-virtual", 1.0)]));

        List<MasterValidationError> errors = Errs(items, facilities, environments, gameEvents, recipes);

        Assert.Contains(errors, e => e.EntityId == "r-virt-in" && e.Field == "Inputs");
        Assert.DoesNotContain(errors, e => e.EntityId == "r-virt-out");
    }

    [Fact(DisplayName = "VAL-08: バージョン値域")]
    public void VersionRanges()
    {
        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateItem(
            new Item { Id = "i-1", Name = "n", Category = "c", VersionAdded = "latest" }, errors);
        MasterValidator.ValidateItem(
            new Item
            {
                Id = "i-2", Name = "n", Category = "c",
                VersionAdded = "2.0.0", VersionRemoved = "1.0.0",
            }, errors);

        Assert.Contains(errors, e => e.EntityId == "i-1" && e.Field == "VersionAdded");
        Assert.Contains(errors, e => e.EntityId == "i-2" && e.Field == "VersionRemoved");
    }

    [Fact(DisplayName = "VAL-09: イベント期間の値域")]
    public void EventPeriodRange()
    {
        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateGameEvent(
            new GameEvent
            {
                Id = "ev-1", Name = "n", VersionAdded = "1.0.0",
                ActiveFrom = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                ActiveTo = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            }, errors);

        Assert.Contains(errors, e => e.Field == "ActiveFrom/ActiveTo");
    }

    [Fact(DisplayName = "VAL-18: オフセット無しのイベント日時はエラー")]
    public void EventPeriodRequiresOffset()
    {
        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateGameEvent(
            new GameEvent
            {
                Id = "ev-1", Name = "n", VersionAdded = "1.0.0",
                ActiveFrom = new DateTime(2026, 6, 1), ActiveTo = new DateTime(2026, 7, 1),
            }, errors);

        Assert.Contains(errors, e => e.Field == "ActiveFrom/ActiveTo");
    }

    [Fact(DisplayName = "VAL-19: イベント期間は瞬間として比較")]
    public void EventPeriodComparesInstants()
    {
        // 同一瞬間の表記違い（Utc と Local 変換後の値）でも瞬間としては逆転扱いになる。
        var instant = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateGameEvent(
            new GameEvent
            {
                Id = "ev-1", Name = "n", VersionAdded = "1.0.0",
                ActiveFrom = instant,
                ActiveTo = DateTime.SpecifyKind(instant.ToLocalTime(), DateTimeKind.Local),
            }, errors);

        Assert.Contains(errors, e => e.Field == "ActiveFrom/ActiveTo");
    }

    [Fact(DisplayName = "VAL-10: IconKey 文字種")]
    public void IconKeyFormat()
    {
        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateItem(
            new Item
            {
                Id = "i-1", Name = "n", Category = "c",
                VersionAdded = "1.0.0", IconKey = "bad key!",
            }, errors);
        MasterValidator.ValidateItem(
            new Item
            {
                Id = "i-2", Name = "n", Category = "c",
                VersionAdded = "1.0.0", IconKey = IconKeyRules.PlaceholderKey,
            }, errors);
        MasterValidator.ValidateItem(
            new Item { Id = "i-3", Name = "n", Category = "c", VersionAdded = "1.0.0" }, errors);

        Assert.Single(errors);
        Assert.Equal("IconKey", errors[0].Field);
        Assert.Equal("i-1", errors[0].EntityId);
    }

    [Fact(DisplayName = "VAL-11: ID の一意性")]
    public void DuplicateIdsAreRejected()
    {
        (List<Item> items, List<Facility> facilities, List<Environment> environments,
            List<GameEvent> gameEvents, List<Recipe> recipes) = ValidBaseline();
        items.Add(F.Item("i-ore", "基礎素材", TransportKind.Belt, null, true));

        List<MasterValidationError> errors = Errs(items, facilities, environments, gameEvents, recipes);

        Assert.Contains(errors, e => e.EntityKind == "Items" && e.Message.Contains("i-ore"));
    }

    [Fact(DisplayName = "VAL-12: 数値域")]
    public void NumericRanges()
    {
        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateFacility(
            new Facility
            {
                Id = "f-1", Name = "n", VersionAdded = "1.0.0",
                Width = 0, Height = -1, PowerConsumption = -5,
            }, errors);
        MasterValidator.ValidateEnvironment(
            new Environment
            {
                Id = "e-1", Name = "n", VersionAdded = "1.0.0",
                ProviderFacilityId = "f-a", ConsumeItemId = "i-1", ConsumeRatePerSecond = 0,
            }, errors);
        MasterValidator.ValidateRecipe(
            F.Recipe("r-1", [F.Pair("r-1", "f-a", 0.0)],
                [("i-ore", 1.0)], [("i-p", 1.0)]), errors);
        MasterValidator.ValidateRecipe(
            F.Recipe("r-2", [F.Pair("r-2", "f-a", 4.0)],
                [("i-ore", 0.0)], [("i-p", 1.0)]), errors);
        MasterValidator.ValidateRecipe(
            F.Recipe("r-3", [F.Pair("r-3", "f-a", 4.0, null, ("i-fuel", 0.0))],
                [("i-ore", 1.0)], [("i-p", 1.0)]), errors);

        Assert.Contains(errors, e => e.Field == "Width");
        Assert.Contains(errors, e => e.Field == "Height");
        Assert.Contains(errors, e => e.Field == "PowerConsumption");
        Assert.Contains(errors, e => e.Field == "ConsumeRatePerSecond");
        Assert.Contains(errors, e => e.Field == "Facilities[0].CycleTime");
        Assert.Contains(errors, e => e.Field == "Inputs[0].Quantity");
        Assert.Contains(errors, e => e.Field == "Facilities[0].FixedConsumption.RatePerSecond");
    }

    [Fact(DisplayName = "VAL-13: ペア 0 件のレシピはエラー")]
    public void PairlessRecipeIsRejected()
    {
        Recipe pairless = F.Recipe("r-nop", [], [("i-ore", 1.0)], [("i-p", 1.0)]);

        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateRecipe(pairless, errors);

        Assert.Contains(errors, e => e.Field == "Facilities");
    }

    [Fact(DisplayName = "VAL-14: 空 ItemId の入力でも検証は例外にならない")]
    public void EmptyInputIdDoesNotAbortValidation()
    {
        (List<Item> items, List<Facility> facilities, List<Environment> environments,
            List<GameEvent> gameEvents, List<Recipe> recipes) = ValidBaseline();

        Recipe recipe = F.Recipe("r-empty", "f-a", 4.0,
            [("i-ore", 1.0)], [("i-p", 1.0)]);
        recipe.Inputs.Add(new RecipeInput { ItemId = "", Quantity = -2.0 });
        recipes.Add(recipe);

        List<MasterValidationError> errors =
            Errs(items, facilities, environments, gameEvents, recipes);

        Assert.Contains(errors, e => e.EntityId == "r-empty" && e.Field == "Inputs[1]");
        Assert.Contains(errors, e => e.EntityId == "r-empty" && e.Field == "Inputs[1].Quantity");
    }

    [Fact(DisplayName = "VAL-15: VersionAdded は semver 形式（prerelease/build 可・2 要素不可）")]
    public void SemverFormatIsEnforced()
    {
        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateItem(
            new Item { Id = "i-pre", Name = "n", Category = "c", VersionAdded = "1.2.0-beta.1+build.7" }, errors);
        MasterValidator.ValidateItem(
            new Item { Id = "i-two", Name = "n", Category = "c", VersionAdded = "1.0" }, errors);
        MasterValidator.ValidateItem(
            new Item { Id = "i-lead", Name = "n", Category = "c", VersionAdded = "01.0.0" }, errors);

        Assert.DoesNotContain(errors, e => e.EntityId == "i-pre" && e.Field == "VersionAdded");
        Assert.Contains(errors, e => e.EntityId == "i-two" && e.Field == "VersionAdded");
        Assert.Contains(errors, e => e.EntityId == "i-lead" && e.Field == "VersionAdded");
    }

    [Fact(DisplayName = "VAL-16: 参照欠落の入力があっても仮想アイテム入力は検出される")]
    public void VirtualInputIsReportedAlongsideMissingReference()
    {
        (List<Item> items, List<Facility> facilities, List<Environment> environments,
            List<GameEvent> gameEvents, List<Recipe> recipes) = ValidBaseline();

        items.Add(F.Item("i-power", "仮想", TransportKind.None));
        recipes.Add(F.Recipe("r-mixed", "f-a", 4.0,
            [("i-ghost", 1.0), ("i-power", 1.0)], [("i-p", 1.0)]));

        List<MasterValidationError> errors = Errs(items, facilities, environments, gameEvents, recipes);

        Assert.Contains(errors,
            e => e.EntityId == "r-mixed" && e.Field == "Inputs" && e.Message.Contains("i-ghost"));
        Assert.Contains(errors,
            e => e.EntityId == "r-mixed" && e.Field == "Inputs" && e.Message.Contains("i-power"));
    }

    [Fact(DisplayName = "VAL-17: 桁あふれのバージョン要素はエラー（例外にならない）")]
    public void OversizedVersionComponentIsError()
    {
        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateItem(
            new Item { Id = "i-big", Name = "n", Category = "c", VersionAdded = "2147483648.0.0" }, errors);

        Assert.Contains(errors, e => e.EntityId == "i-big" && e.Field == "VersionAdded");
    }
}
