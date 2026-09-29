using EndfieldAicWeb.Domain.Calculation;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>TRN: 輸送容量（ベルト 30 個/s・パイプ 60 個/s、docs/phases/test-specification-phase2.md §3）。</summary>
public class TransportCapacityTests
{
    [Fact(DisplayName = "TRN-01: ベルト超過は警告（レーン数付き）")]
    public void BeltOverflowWarns()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F08(), [("i-belt-item", 1900.0)]);

        Assert.Contains(plan.Warnings, w =>
            w.Code == WarningCode.TransportCapacityExceeded
            && w.Message.Contains("i-belt-item")
            && w.Message.Contains("レーン数: 2"));
    }

    [Fact(DisplayName = "TRN-02: パイプ超過は警告")]
    public void PipeOverflowWarns()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F08(), [("i-pipe-item", 3700.0)]);

        Assert.Contains(plan.Warnings, w =>
            w.Code == WarningCode.TransportCapacityExceeded
            && w.Message.Contains("i-pipe-item")
            && w.Message.Contains("レーン数: 2"));
    }

    [Fact(DisplayName = "TRN-03: TransportKind=None は対象外")]
    public void VirtualItemIsUnchecked()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F08(), [("i-none-item", 5000.0)]);

        Assert.False(HasWarning(plan, WarningCode.TransportCapacityExceeded));
    }

    [Fact(DisplayName = "TRN-04: 上限ちょうどは警告なし")]
    public void ExactLimitDoesNotWarn()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F08(), [("i-belt-item", 1800.0)]);

        Assert.False(HasWarning(plan, WarningCode.TransportCapacityExceeded));
    }
}
