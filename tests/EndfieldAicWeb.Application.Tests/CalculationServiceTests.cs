using EndfieldAicWeb.Application.Calculation;
using EndfieldAicWeb.Application.PlanView;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

/// <summary>SVC: 計算ユースケース入口。</summary>
public class CalculationServiceTests
{
    private readonly CalculationService _service = new();

    // SVC-01: ProductionCalculator の結果がそのまま返る。
    [Fact]
    public void ReturnsEnginePlan()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A01();

        CalculationOutcome outcome = _service.Calculate(
            snapshot,
            [new ProductionTarget("i-part", 60)],
            ApplicationFixtures.Context(),
            [],
            [],
            []);

        Assert.Equal(60, Assert.Single(outcome.Plan.ItemRequirements, r => r.ItemId == "i-part").RequiredPerMinute);
        Assert.Equal(120, Assert.Single(outcome.Plan.ItemRequirements, r => r.ItemId == "i-ore").RequiredPerMinute);
        Assert.Equal(3, Assert.Single(outcome.Plan.FacilityRequirements, f => f.FacilityId == "f-asm").CeilCount);
        EnvironmentRequirement env = Assert.Single(outcome.Plan.EnvironmentRequirements);
        Assert.Equal("env-gas", env.EnvironmentId);
        Assert.Equal(1, env.DispenserCount);
        Assert.Equal(170, outcome.Plan.TotalPowerConsumption);
    }

    // SVC-02: 需要アイテムのペア候補が素材ごとに返る。
    [Fact]
    public void ReturnsPairOptionsPerDemandItem()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A01();

        CalculationOutcome outcome = _service.Calculate(
            snapshot,
            [new ProductionTarget("i-part", 60)],
            ApplicationFixtures.Context(),
            [],
            [],
            []);

        IReadOnlyList<PairOption> options = Assert.Single(outcome.PairOptionsByItemId).Value;
        Assert.Equal(2, options.Count);
        PairOption single = Assert.Single(options, o => o.IsDefault);
        Assert.Equal(3, single.CycleTime);
        Assert.Equal("env-gas", single.EnvironmentId);
    }

    // SVC-03: 候補外のペアを選んだ場合は警告付きで反映される。
    [Fact]
    public void AppliesOverrideAndSurfacesWarning()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A01();
        var option = new PairOption("key", "r-part", "f-asm", 4, null, null, false);
        PairOverride pairOverride = option.ToOverride("i-part");

        CalculationOutcome outcome = _service.Calculate(
            snapshot,
            [new ProductionTarget("i-part", 60)],
            ApplicationFixtures.Context(),
            [pairOverride],
            [],
            []);

        PairSelection selection = Assert.Single(outcome.Plan.PairSelections);
        Assert.Equal("r-part", selection.RecipeId);
        Assert.Equal(4, selection.Pair.CycleTime);
        Assert.Null(selection.Pair.EnvironmentId);
        Assert.Empty(outcome.Plan.EnvironmentRequirements);
    }

    // BAS-13: 基礎素材に指定したアイテムにはペア候補を出さない（仕様決定 CZ）。
    [Fact]
    public void SpecifiedItemHasNoPairOptions()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A01();
        var context = new ContextFilter { SpecifiedBaseItemIds = ["i-part"] };

        CalculationOutcome outcome = _service.Calculate(
            snapshot,
            [new ProductionTarget("i-part", 60)],
            context,
            [],
            [],
            []);

        Assert.False(outcome.PairOptionsByItemId.ContainsKey("i-part"));
        Assert.Equal(
            60,
            Assert.Single(outcome.Plan.ItemRequirements, r => r.ItemId == "i-part")
                .Supplies.Where(s => s.Kind == SupplyKind.ExternalProcurement)
                .Sum(s => s.AmountPerMinute));
    }

    // SVC-04: MapId と gatherOverrides が計算へ渡る。
    [Fact]
    public void AppliesGatherMapAndRateOverrides()
    {
        MasterDataSnapshot base_ = ApplicationFixtures.A01();
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            base_.Items,
            base_.Facilities,
            base_.Environments,
            base_.GameEvents,
            base_.Recipes,
            maps:
            [
                new GameMap
                {
                    Id = "m-cap",
                    Name = "上限マップ",
                    VersionAdded = "1.0.0",
                    GatherRates =
                    [
                        new GatherRate { ItemId = "i-ore", RatePerMinute = 60 },
                        new GatherRate { ItemId = "i-gas", IsUnlimited = true },
                    ],
                },
            ]);
        var context = new ContextFilter { MapId = "m-cap" };

        // 上限内: i-part 30/分 → i-ore 需要 60/分（上限ちょうど）。
        CalculationOutcome withinCap = _service.Calculate(
            snapshot,
            [new ProductionTarget("i-part", 30)],
            context,
            [],
            [],
            []);
        Assert.Equal(
            60,
            Assert.Single(withinCap.Plan.ItemRequirements, r => r.ItemId == "i-ore")
                .Supplies.Where(s => s.Kind == SupplyKind.Gathered).Sum(s => s.AmountPerMinute));
        Assert.DoesNotContain(withinCap.Plan.Warnings, w => w.Code == WarningCode.GatherCapExceeded);

        // 上書き 40: 有効レートの置き換えで採取 40 + 未充足 20（i-ore に代替レシピなし）。
        CalculationOutcome overridden = _service.Calculate(
            snapshot,
            [new ProductionTarget("i-part", 30)],
            context,
            [],
            [],
            [new GatherRateOverride("i-ore", 40)]);
        ItemRequirement ore = Assert.Single(overridden.Plan.ItemRequirements, r => r.ItemId == "i-ore");
        Assert.Equal(40, ore.Supplies.Where(s => s.Kind == SupplyKind.Gathered).Sum(s => s.AmountPerMinute));
        Assert.Equal(20, ore.UnmetPerMinute);
        Assert.Contains(overridden.Plan.Warnings, w => w.Code == WarningCode.GatherCapExceeded);
    }
}
