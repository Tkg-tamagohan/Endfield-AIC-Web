using EndfieldAicWeb.Domain.Calculation;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>CNV: 収束反復（docs/implementation-plan.md §3-8、docs/phases/test-specification-phase2.md §3）。</summary>
public class ConvergenceTests
{
    [Fact(DisplayName = "CNV-01: 環境消費は採取が優先され自産レシピは稼働しない（仕様決定 AD）")]
    public void EnvironmentDemandIsGatheredBeforeProduction()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F13(), [("i-xp", 30.0)]);

        ItemRequirement gas = Req(plan, "i-gasp");
        Assert.Equal(360.0, gas.RequiredPerMinute, Precision);
        Assert.Equal(360.0, Supplied(plan, "i-gasp", SupplyKind.Gathered), Precision);
        Assert.Null(RunOf(plan, "r-gasp"));
        Assert.Equal(1, Fac(plan, "f-disp").CeilCount);
        Assert.False(HasWarning(plan, WarningCode.ConvergenceNotReached));
    }

    [Fact(DisplayName = "CNV-02: 収束しない場合は警告して結果を返す")]
    public void DivergentDemandWarnsWithoutException()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F13(), [("i-fuelself", 1.0)]);

        Assert.True(HasWarning(plan, WarningCode.ConvergenceNotReached));

        // 未収束でも最後に適用した需要が帳簿へ反映され、要求量は供給＋未充足と一致する。
        foreach (ItemRequirement req in plan.ItemRequirements)
        {
            double supplied = req.Supplies.Sum(s => s.AmountPerMinute) + req.UnmetPerMinute;
            Assert.Equal(req.RequiredPerMinute, supplied, 6);
        }
    }
}
