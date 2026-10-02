using EndfieldAicWeb.Domain.Calculation;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>TRN: 輸送容量（ベルト 30 個/分・パイプ 60 個/分、docs/phases/test-specification-phase2.md §3、仕様決定 AM）。</summary>
public class TransportCapacityTests
{
    [Fact(DisplayName = "TRN-01: ベルト超過は警告（レーン数付き）")]
    public void BeltOverflowWarns()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F08(), [("i-belt-item", 45.0)]);

        Assert.Contains(plan.Warnings, w =>
            w.Code == WarningCode.TransportCapacityExceeded
            && w.Message.Contains("i-belt-item")
            && w.Message.Contains("レーン数: 2"));
    }

    [Fact(DisplayName = "TRN-02: パイプ超過は警告")]
    public void PipeOverflowWarns()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F08(), [("i-pipe-item", 90.0)]);

        Assert.Contains(plan.Warnings, w =>
            w.Code == WarningCode.TransportCapacityExceeded
            && w.Message.Contains("i-pipe-item")
            && w.Message.Contains("レーン数: 2"));
    }

    [Fact(DisplayName = "TRN-03: TransportKind=None は対象外")]
    public void VirtualItemIsUnchecked()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F08(), [("i-none-item", 500.0)]);

        Assert.False(HasWarning(plan, WarningCode.TransportCapacityExceeded));
    }

    [Fact(DisplayName = "TRN-04: 上限ちょうどは警告なし")]
    public void ExactLimitDoesNotWarn()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F08(), [("i-belt-item", 30.0)]);

        Assert.False(HasWarning(plan, WarningCode.TransportCapacityExceeded));
    }

    [Fact(DisplayName = "TRN-05: 警告文は個/分表記")]
    public void WarningMessageUsesPerMinute()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F08(), [("i-belt-item", 45.0)]);

        Assert.Contains(plan.Warnings, w =>
            w.Code == WarningCode.TransportCapacityExceeded
            && w.Message.Contains("個/分")
            && !w.Message.Contains("個/s"));
    }
}
