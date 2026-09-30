using EndfieldAicWeb.Domain.Calculation;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>EXP: 需要展開と設備台数（docs/phases/test-specification-phase2.md §3）。</summary>
public class ExpansionTests
{
    [Fact(DisplayName = "EXP-01: 直線チェーンの展開")]
    public void LinearChainExpands()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F01(), [("i-part", 30.0)]);

        ItemRequirement ore = Req(plan, "i-ore");
        Assert.Equal(60.0, ore.RequiredPerMinute, Precision);
        Assert.Equal(60.0, ore.Supplies.Single(s => s.Kind == SupplyKind.Gathered).AmountPerMinute, Precision);
        Assert.Equal(0.0, ore.UnmetPerMinute, Precision);

        ItemRequirement part = Req(plan, "i-part");
        Assert.Equal(30.0, part.RequiredPerMinute, Precision);
        Assert.Equal(30.0, part.Supplies.Single(s => s.Kind == SupplyKind.Recipe).AmountPerMinute, Precision);

        RecipeRun run = Assert.Single(plan.RecipeRuns);
        Assert.Equal("r-part", run.RecipeId);
        Assert.Equal("f-asm", run.FacilityId);
        Assert.Equal(30.0, run.CyclesPerMinute, Precision);

        FacilityRequirement facility = Fac(plan, "f-asm");
        Assert.Equal(2.0, facility.ExactCount, Precision);
        Assert.Equal(2, facility.CeilCount);

        Assert.Empty(plan.Warnings);
    }

    [Fact(DisplayName = "EXP-02: 多段依存の展開")]
    public void MultiStageExpands()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F02(), [("i-a", 12.0)]);

        Assert.Equal(24.0, Req(plan, "i-b").RequiredPerMinute, Precision);
        Assert.Equal(72.0, Req(plan, "i-c").RequiredPerMinute, Precision);
        Assert.Equal(144.0, Req(plan, "i-d").RequiredPerMinute, Precision);

        Assert.Equal(1.0, Fac(plan, "f-a").ExactCount, Precision);
        Assert.Equal(4.0, Fac(plan, "f-b").ExactCount, Precision);
        Assert.Equal(7.2, Fac(plan, "f-c").ExactCount, Precision);
        Assert.Equal(8, Fac(plan, "f-c").CeilCount);
    }

    [Fact(DisplayName = "EXP-03: 同一アイテムの複数目標は合算")]
    public void DuplicateTargetsAreSummed()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F01(), [("i-part", 20.0), ("i-part", 10.0)]);

        Assert.Equal(30.0, Req(plan, "i-part").RequiredPerMinute, Precision);
        Assert.Equal(30.0, Assert.Single(plan.RecipeRuns).CyclesPerMinute, Precision);
    }

    [Fact(DisplayName = "EXP-04: 設備台数の実数と切上")]
    public void FacilityCountExactAndCeil()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F02(), [("i-a", 12.0)]);

        FacilityRequirement fc = Fac(plan, "f-c");
        Assert.Equal(7.2, fc.ExactCount, Precision);
        Assert.Equal(8, fc.CeilCount);
    }

    [Fact(DisplayName = "EXP-05: 目標なしは空結果")]
    public void EmptyTargetsProduceEmptyPlan()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F01(), []);

        Assert.Empty(plan.ItemRequirements);
        Assert.Empty(plan.FacilityRequirements);
        Assert.Empty(plan.RecipeRuns);
        Assert.Empty(plan.EnvironmentRequirements);
        Assert.Empty(plan.Surpluses);
        Assert.Empty(plan.FlowAdjustments);
        Assert.Empty(plan.Warnings);
        Assert.Equal(0.0, plan.TotalPowerConsumption, Precision);
    }
}
