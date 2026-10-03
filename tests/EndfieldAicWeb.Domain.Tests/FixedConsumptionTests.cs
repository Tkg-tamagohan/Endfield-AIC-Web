using EndfieldAicWeb.Domain.Calculation;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>FIX: 固定消費（仕様決定 J/V、docs/phases/test-specification-phase2.md §3）。</summary>
public class FixedConsumptionTests
{
    [Fact(DisplayName = "FIX-01: 固定消費が切上台数比例で需要へ")]
    public void FixedConsumptionAddsDemandByCeilCount()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F11(), [("i-fc", 10.0)]);

        Assert.Equal(5.0, Fac(plan, "f-fc").ExactCount, Precision);
        Assert.Equal(5, Fac(plan, "f-fc").CeilCount);
        Assert.Equal(30.0, Req(plan, "i-fuel").RequiredPerMinute, Precision);
        Assert.Equal(30.0, Supplied(plan, "i-fuel", SupplyKind.Gathered), Precision);
    }

    [Fact(DisplayName = "FIX-02: 基準は実数でなく切上台数")]
    public void FixedConsumptionUsesCeilCount()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F11(), [("i-fc", 5.1)]);

        Assert.Equal(2.55, Fac(plan, "f-fc").ExactCount, Precision);
        Assert.Equal(3, Fac(plan, "f-fc").CeilCount);
        Assert.Equal(18.0, Req(plan, "i-fuel").RequiredPerMinute, Precision);
    }

    [Fact(DisplayName = "FIX-03: 固定消費素材は採取が優先され自産レシピは稼働しない（仕様決定 AD）")]
    public void FixedConsumptionItemIsGatheredBeforeRecipe()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F11WithFuelRecipe(), [("i-fc", 10.0)]);

        Assert.Equal(30.0, Supplied(plan, "i-fuel", SupplyKind.Gathered), Precision);
        Assert.Null(RunOf(plan, "r-fuel"));
        Assert.False(HasWarning(plan, WarningCode.ConvergenceNotReached));
        Assert.Equal(0.0, Req(plan, "i-fuel").UnmetPerMinute, Precision);
    }

    [Fact(DisplayName = "FIX-04: 提供設備とレシピ設備が兼用なら散布機込みの切上台数が乗数")]
    public void SharedProviderFacilityCountsDispensers()
    {
        // i-fcx 10/分は 30 秒ペアで 5.0 機分。CoverableMachines 4 を超えるため散布機は 2 台になり、
        // 兼用設備の切上台数は 5.0 + 2 = 7（仕様決定 BQ で 1 台 → 2 台へ変わった分）。
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F11WithSharedProvider(), [("i-fcx", 10.0)]);

        Assert.Equal(7, Fac(plan, "f-fc").CeilCount);
        Assert.Equal(210.0, Req(plan, "i-fuel").RequiredPerMinute, Precision);
    }
}
