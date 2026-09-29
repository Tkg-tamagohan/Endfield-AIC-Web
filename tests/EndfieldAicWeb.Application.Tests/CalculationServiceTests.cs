using EndfieldAicWeb.Application;
using EndfieldAicWeb.Domain.Calculation;

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
            []);

        PairSelection selection = Assert.Single(outcome.Plan.PairSelections);
        Assert.Equal("r-part", selection.RecipeId);
        Assert.Equal(4, selection.Pair.CycleTime);
        Assert.Null(selection.Pair.EnvironmentId);
        Assert.Empty(outcome.Plan.EnvironmentRequirements);
    }
}
