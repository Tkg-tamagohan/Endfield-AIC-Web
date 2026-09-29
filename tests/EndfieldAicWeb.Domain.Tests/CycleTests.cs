using EndfieldAicWeb.Domain.Calculation;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>CYC: 循環依存（docs/phases/test-specification-phase2.md §3）。</summary>
public class CycleTests
{
    [Fact(DisplayName = "CYC-01: 相互循環は警告し残差を未充足へ")]
    public void MutualCycleWarnsAndLeavesUnmet()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F04(), [("i-a", 10.0)]);

        Assert.True(HasWarning(plan, WarningCode.CycleDetected));
        Assert.Equal(20.0, Req(plan, "i-a").RequiredPerMinute, Precision);
        Assert.Equal(10.0, Req(plan, "i-a").UnmetPerMinute, Precision);
        Assert.Equal(10.0, Req(plan, "i-b").RequiredPerMinute, Precision);
        Assert.Equal(0.0, Req(plan, "i-b").UnmetPerMinute, Precision);
    }

    [Fact(DisplayName = "CYC-02: 自己ループ")]
    public void SelfLoopWarns()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F04(), [("i-s", 10.0)]);

        Assert.True(HasWarning(plan, WarningCode.CycleDetected));
        Assert.Equal(10.0, Req(plan, "i-s").UnmetPerMinute, Precision);
    }

    [Fact(DisplayName = "CYC-03: 循環以外の需要は通常計算")]
    public void NonCyclicDemandsAreComputedNormally()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F04WithLinearChain(), [("i-a", 10.0), ("i-part", 30.0)]);

        Assert.True(HasWarning(plan, WarningCode.CycleDetected));
        Assert.Equal(0.0, Req(plan, "i-part").UnmetPerMinute, Precision);
        Assert.Equal(2.0, Fac(plan, "f-asm").ExactCount, Precision);
    }
}
