using EndfieldAicWeb.Domain.Calculation;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>BYP: 副産物の充当と余剰（docs/phases/test-specification-phase2.md §3）。</summary>
public class ByproductTests
{
    [Fact(DisplayName = "BYP-01: 需要のない副産物は余剰")]
    public void UndemandedByproductBecomesSurplus()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F05(), [("i-p", 15.0)]);

        Assert.False(HasReq(plan, "i-q"));
        Assert.Equal(30.0, plan.Surpluses.Single(s => s.ItemId == "i-q").ExcessPerMinute, Precision);
    }

    [Fact(DisplayName = "BYP-02: 副産物が需要の一部を賄い残りは自レシピ")]
    public void PartialByproductPlusOwnRecipe()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F05(), [("i-p", 15.0), ("i-q", 50.0)]);

        Assert.Equal(30.0, Supplied(plan, "i-q", SupplyKind.Byproduct), Precision);
        Assert.Equal(20.0, Supplied(plan, "i-q", SupplyKind.Recipe), Precision);
        Assert.Equal(2.0, Fac(plan, "f-q").ExactCount, Precision);
    }

    [Fact(DisplayName = "BYP-03: 副産物が需要を超えると自レシピ不稼働")]
    public void ByproductExceedsDemand()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F05(), [("i-p", 15.0), ("i-q", 20.0)]);

        Assert.Equal(30.0, Supplied(plan, "i-q", SupplyKind.Byproduct), Precision);
        Assert.Equal(0.0, Supplied(plan, "i-q", SupplyKind.Recipe), Precision);
        Assert.Null(RunOf(plan, "r-q"));
        Assert.Equal(10.0, plan.Surpluses.Single(s => s.ItemId == "i-q").ExcessPerMinute, Precision);
    }

    [Fact(DisplayName = "BYP-04: 同一レシピを複数需要が選択")]
    public void SameRecipeServesMultipleDemands()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F05(), [("i-p", 15.0), ("i-q", 45.0)],
            overrides: [CalculationFixtures.Override("i-q", "r-m", "f-m", 4.0)]);

        RecipeRun run = Assert.Single(plan.RecipeRuns);
        Assert.Equal("r-m", run.RecipeId);
        Assert.Equal(22.5, run.CyclesPerMinute, Precision);
        Assert.Equal(7.5, plan.Surpluses.Single(s => s.ItemId == "i-p").ExcessPerMinute, Precision);
        Assert.Equal(0.0, Req(plan, "i-q").UnmetPerMinute, Precision);
    }

    [Fact(DisplayName = "BYP-05: 目標順序で結果が変わらない")]
    public void TargetOrderDoesNotChangeResult()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F05(), [("i-q", 20.0), ("i-p", 15.0)]);

        Assert.Equal(30.0, Supplied(plan, "i-q", SupplyKind.Byproduct), Precision);
        Assert.Null(RunOf(plan, "r-q"));
        Assert.Equal(10.0, plan.Surpluses.Single(s => s.ItemId == "i-q").ExcessPerMinute, Precision);
    }

    [Fact(DisplayName = "BYP-06: 部分副産物で先行稼働・外部調達が縮小")]
    public void PartialByproductShrinksPriorRun()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F05WithPartialByproduct(), [("i-q", 20.0), ("i-p", 15.0)]);

        Assert.Equal(5.0, RunOf(plan, "r-q")!.CyclesPerMinute, Precision);
        Assert.Equal(15.0, Supplied(plan, "i-oreq", SupplyKind.RawMaterial), Precision);
        Assert.Equal(0.0, Req(plan, "i-q").UnmetPerMinute, Precision);
    }

    [Fact(DisplayName = "BYP-07: 循環未充足へ後から副産物が届くと未充足が縮小")]
    public void LateByproductShrinksCycleUnmet()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F04WithByproduct(), [("i-a", 10.0), ("i-x", 5.0)]);

        Assert.Equal(20.0, Req(plan, "i-a").RequiredPerMinute, Precision);
        Assert.Equal(5.0, Req(plan, "i-a").UnmetPerMinute, Precision);
    }
}
