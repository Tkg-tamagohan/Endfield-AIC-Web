using EndfieldAicWeb.Domain.Calculation;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>
/// TRN: 輸送容量（ベルト 30 個/分・パイプ 60 個/分、仕様決定 AM）。
/// 判定は設備 1 ユニットへの入力流量に限る（Phase 17 の仕様決定 AN）。
/// </summary>
public class TransportCapacityTests
{
    [Fact(DisplayName = "TRN-01: ユニット入力のベルト超過は警告")]
    public void BeltUnitOverflowWarns()
    {
        // F-06: i-t 60/分 → f-t 実数 2 台でユニット入力は各 120/分（ベルト超過）。
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F06(), [("i-t", 60.0)]);

        Assert.Contains(plan.Warnings, w =>
            w.Code == WarningCode.TransportCapacityExceeded
            && w.Message.Contains("i-u")
            && w.Message.Contains("設備 1 台"));
    }

    [Fact(DisplayName = "TRN-02: 散布機入力のパイプ超過は警告")]
    public void PipeDispenserOverflowWarns()
    {
        // F-10: r-std は env-gas が必須で、散布機 1 台の i-gas 消費 360/分（パイプ超過）。
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F10(), [("i-std", 10.0)]);

        Assert.Contains(plan.Warnings, w =>
            w.Code == WarningCode.TransportCapacityExceeded
            && w.Message.Contains("i-gas"));
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
        // F-06: i-t 7.5/分 → 実数 0.25 台 → ユニット入力 i-u は 30/分 ちょうど。
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F06(), [("i-t", 7.5)]);

        Assert.False(HasWarning(plan, WarningCode.TransportCapacityExceeded));
    }

    [Fact(DisplayName = "TRN-05: 警告文は個/分・ユニット基準表記")]
    public void WarningMessageUsesPerMinute()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F06(), [("i-t", 60.0)]);

        Assert.Contains(plan.Warnings, w =>
            w.Code == WarningCode.TransportCapacityExceeded
            && w.Message.Contains("個/分")
            && w.Message.Contains("設備 1 台への入力流量")
            && !w.Message.Contains("個/s")
            && !w.Message.Contains("レーン数"));
    }

    [Fact(DisplayName = "TRN-06: レーン増設で解消できる集計超過は警告なし")]
    public void LaneSolvableAggregateDoesNotWarn()
    {
        // F-08: i-belt-item 45/分の集計流量は容量超過だが、ユニット入力は 10/分に収まる。
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F08(), [("i-belt-item", 45.0)]);

        Assert.False(HasWarning(plan, WarningCode.TransportCapacityExceeded));
    }

    [Fact(DisplayName = "TRN-07: ユニット実体化上限超過の計画で集約流量を 1 機の入力と誤判定しない")]
    public void OversizedFacilityDoesNotFalselyWarn()
    {
        // 機械数 20,000（FacilityUnitLayout の防御的上限超過）・1 機あたり入力 1/分。
        // 末尾へ集約されたスロットの流量を 1 機とみなすと誤警告になる（Phase 26 レビュー指摘）。
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.Snapshot(
                [CalculationFixtures.Item("i-a"), CalculationFixtures.Item("i-x")],
                [CalculationFixtures.Facility("f-a")],
                [CalculationFixtures.Recipe("r-x", "f-a", 60.0, [("i-a", 1.0)], [("i-x", 1.0)])]),
            [("i-x", 20_000.0)]);

        Assert.False(HasWarning(plan, WarningCode.TransportCapacityExceeded));
    }

    [Fact(DisplayName = "TRN-08: ユニット実体化上限超過でも機械あたりの容量超過は警告")]
    public void OversizedFacilityStillWarnsPerMachineBreach()
    {
        // 機械数 20,000（cycles 400,000 × 3秒/60）・1 機あたり入力 40/分（ベルト 30 超過）なら
        // 集約しても警告が要る。
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.Snapshot(
                [CalculationFixtures.Item("i-a"), CalculationFixtures.Item("i-x")],
                [CalculationFixtures.Facility("f-a")],
                [CalculationFixtures.Recipe("r-x", "f-a", 3.0, [("i-a", 2.0)], [("i-x", 1.0)])]),
            [("i-x", 400_000.0)]);

        Assert.Contains(plan.Warnings, w =>
            w.Code == WarningCode.TransportCapacityExceeded
            && w.Message.Contains("i-a"));
    }
}
