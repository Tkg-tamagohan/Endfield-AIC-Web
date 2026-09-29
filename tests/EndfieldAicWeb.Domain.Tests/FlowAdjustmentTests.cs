using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>FLW: 流量調整（仕様決定 O、docs/phases/test-specification-phase2.md §3）。</summary>
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

    [Fact(DisplayName = "FLW-05: 設備共用で集計が整数でもランごとの推奨制限を出す")]
    public void SharedFacilityStillEmitsPerRunLimits()
    {
        MasterDataSnapshot master = CalculationFixtures.Snapshot(
            [
                CalculationFixtures.Item("i-a"),
                CalculationFixtures.Item("i-b"),
                CalculationFixtures.Item("i-ore", "基礎素材", TransportKind.Belt, null, true),
            ],
            [CalculationFixtures.Facility("f-sh")],
            [
                CalculationFixtures.Recipe("r-a", "f-sh", 6.0, [("i-ore", 1.0)], [("i-a", 1.0)]),
                CalculationFixtures.Recipe("r-b", "f-sh", 6.0, [("i-ore", 1.0)], [("i-b", 1.0)]),
            ]);

        ProductionPlan plan = CalculationFixtures.Run(master, [("i-a", 5.0), ("i-b", 5.0)]);

        Assert.Equal(1.0, Fac(plan, "f-sh").ExactCount, Precision);
        Assert.Equal(1, Fac(plan, "f-sh").CeilCount);
        FlowAdjustment adjA = Assert.Single(plan.FlowAdjustments, a => a.RecipeId == "r-a");
        FlowAdjustment adjB = Assert.Single(plan.FlowAdjustments, a => a.RecipeId == "r-b");
        Assert.Equal(5.0 / 60.0, adjA.RecommendedLimitPerSecond, Precision);
        Assert.Equal(5.0 / 60.0, adjB.RecommendedLimitPerSecond, Precision);
    }

    [Fact(DisplayName = "FLW-06: 設備共用でも整数台の全速稼働なら調整行なし")]
    public void SharedFacilityAtFullSpeedNeedsNoAdjustment()
    {
        MasterDataSnapshot master = CalculationFixtures.Snapshot(
            [
                CalculationFixtures.Item("i-a"),
                CalculationFixtures.Item("i-b"),
                CalculationFixtures.Item("i-ore", "基礎素材", TransportKind.Belt, null, true),
            ],
            [CalculationFixtures.Facility("f-sh")],
            [
                CalculationFixtures.Recipe("r-a", "f-sh", 6.0, [("i-ore", 1.0)], [("i-a", 1.0)]),
                CalculationFixtures.Recipe("r-b", "f-sh", 6.0, [("i-ore", 1.0)], [("i-b", 1.0)]),
            ]);

        ProductionPlan plan = CalculationFixtures.Run(master, [("i-a", 10.0), ("i-b", 10.0)]);

        Assert.Equal(2.0, Fac(plan, "f-sh").ExactCount, Precision);
        Assert.Empty(plan.FlowAdjustments);
    }
}
