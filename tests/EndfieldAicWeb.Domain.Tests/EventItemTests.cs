using EndfieldAicWeb.Domain.Calculation;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>EVT: イベント限定アイテム（仕様決定 T/X、test-specification-phase2 §3）。</summary>
public class EventItemTests
{
    [Fact(DisplayName = "EVT-01: イベント非有効アイテムの目標は未充足＋警告")]
    public void InactiveItemTargetIsUnmet()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F12(), [("i-ltd", 10.0)]);

        Assert.True(HasWarning(plan, WarningCode.EventItemUnavailable));
        Assert.Equal(10.0, Req(plan, "i-ltd").UnmetPerMinute, Precision);
    }

    [Fact(DisplayName = "EVT-02: 中間素材としても不可")]
    public void InactiveItemAsIntermediateIsUnmet()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F12(), [("i-fin", 10.0)]);

        Assert.True(HasWarning(plan, WarningCode.EventItemUnavailable));
        Assert.Equal(20.0, Req(plan, "i-ltd").UnmetPerMinute, Precision);
        Assert.Equal(0.0, Req(plan, "i-fin").UnmetPerMinute, Precision);
        Assert.Equal(10.0, Supplied(plan, "i-fin", SupplyKind.Recipe), Precision);
    }

    [Fact(DisplayName = "EVT-03: イベント有効なら通常どおり生産")]
    public void ActiveEventItemIsProduced()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F12(), [("i-ltd", 10.0)],
            context: CalculationFixtures.Context("ev-ltd"));

        Assert.False(HasWarning(plan, WarningCode.EventItemUnavailable));
        Assert.Equal(0.0, Req(plan, "i-ltd").UnmetPerMinute, Precision);
        Assert.Equal(10.0, Supplied(plan, "i-ltd", SupplyKind.Recipe), Precision);
    }

    [Fact(DisplayName = "EVT-04: 基礎素材でもイベント非有効なら外部調達不可")]
    public void InactiveRawMaterialCannotBeProcured()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F12(), [("i-ltd-raw", 10.0)]);

        Assert.True(HasWarning(plan, WarningCode.EventItemUnavailable));
        Assert.Equal(10.0, Req(plan, "i-ltd-raw").UnmetPerMinute, Precision);
        Assert.Equal(0.0, Supplied(plan, "i-ltd-raw", SupplyKind.RawMaterial), Precision);
    }
}
