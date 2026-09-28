using EndfieldAicWeb.Domain.Calculation;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>FLW: 流量調整（仕様決定 O、test-specification-phase2 §3）。</summary>
public class FlowAdjustmentTests
{
    [Fact(DisplayName = "FLW-01: 推奨制限は要求流量の実数値")]
    public void RecommendedLimitIsExactFlow()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F06(), [("i-t", 310.0)]);

        Assert.Equal(31.0 / 3.0, Fac(plan, "f-t").ExactCount, Precision);
        Assert.Equal(11, Fac(plan, "f-t").CeilCount);

        FlowAdjustment adjustment = Assert.Single(plan.FlowAdjustments);
        Assert.Equal("r-t", adjustment.RecipeId);
        Assert.Equal("i-u", adjustment.InputItemId);
        Assert.Equal(62.0 / 3.0, adjustment.RequiredPerSecond, Precision);
        Assert.Equal(62.0 / 3.0, adjustment.RecommendedLimitPerSecond, Precision);
    }

    [Fact(DisplayName = "FLW-02: 推奨制限は丸めない")]
    public void RecommendedLimitIsNotRounded()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F06(), [("i-t", 320.0)]);

        Assert.Equal(64.0 / 3.0, Assert.Single(plan.FlowAdjustments).RecommendedLimitPerSecond, Precision);
    }

    [Fact(DisplayName = "FLW-03: 整数台数なら調整行なし")]
    public void IntegerFacilityCountNeedsNoAdjustment()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F06(), [("i-t", 300.0)]);

        Assert.Empty(plan.FlowAdjustments);
        Assert.Equal(10.0, Fac(plan, "f-t").ExactCount, Precision);
        Assert.Equal(10, Fac(plan, "f-t").CeilCount);
    }

    [Fact(DisplayName = "FLW-04: 実数流量をそのまま出力")]
    public void ExactFlowIsEmitted()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F06(), [("i-t", 135.0)]);

        Assert.Equal(9.0, Assert.Single(plan.FlowAdjustments).RecommendedLimitPerSecond, Precision);
        Assert.Equal(4.5, Fac(plan, "f-t").ExactCount, Precision);
        Assert.Equal(5, Fac(plan, "f-t").CeilCount);
    }
}
