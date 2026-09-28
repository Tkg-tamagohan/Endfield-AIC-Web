using EndfieldAicWeb.Domain.Calculation;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>CNV: 収束反復（implementation-plan §3-8、test-specification-phase2 §3）。</summary>
public class ConvergenceTests
{
    [Fact(DisplayName = "CNV-01: 環境消費が生産レシピへ展開して収束")]
    public void EnvironmentDemandExpandsToProduction()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F13(), [("i-xp", 30.0)]);

        ItemRequirement gas = Req(plan, "i-gasp");
        Assert.Equal(360.0, gas.RequiredPerMinute, Precision);
        Assert.Equal(360.0, Supplied(plan, "i-gasp", SupplyKind.Recipe), Precision);
        Assert.Equal(36.0, RunOf(plan, "r-gasp")!.CyclesPerMinute, Precision);
        Assert.Equal(1, Fac(plan, "f-disp").CeilCount);
        Assert.False(HasWarning(plan, WarningCode.ConvergenceNotReached));
    }

    [Fact(DisplayName = "CNV-02: 収束しない場合は警告して結果を返す")]
    public void DivergentDemandWarnsWithoutException()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F13(), [("i-fuelself", 1.0)]);

        Assert.True(HasWarning(plan, WarningCode.ConvergenceNotReached));
    }
}
