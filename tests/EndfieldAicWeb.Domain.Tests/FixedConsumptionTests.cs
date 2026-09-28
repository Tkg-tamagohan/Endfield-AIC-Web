using EndfieldAicWeb.Domain.Calculation;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>FIX: 固定消費（仕様決定 J/V、test-specification-phase2 §3）。</summary>
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
        Assert.Equal(30.0, Supplied(plan, "i-fuel", SupplyKind.RawMaterial), Precision);
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

    [Fact(DisplayName = "FIX-03: 固定消費素材の生産が展開され収束する")]
    public void FixedConsumptionItemCanBeProduced()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F11WithFuelRecipe(), [("i-fc", 10.0)]);

        Assert.Equal(30.0, Supplied(plan, "i-fuel", SupplyKind.Recipe), Precision);
        Assert.Equal(30.0, RunOf(plan, "r-fuel")!.CyclesPerMinute, Precision);
        Assert.False(HasWarning(plan, WarningCode.ConvergenceNotReached));
        Assert.Equal(0.0, Req(plan, "i-fuel").UnmetPerMinute, Precision);
    }
}
