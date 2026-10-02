using EndfieldAicWeb.Domain.Calculation;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>NCP: 正味増循環の定常解（docs/phases/test-specification-phase18.md §2）。</summary>
public class PositiveCycleTests
{
    [Fact(DisplayName = "NCP-01: 作物側正味増の循環は定常解へ収束する（芽針型）")]
    public void CropPositiveCycleConverges()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F17(), [("i-crop", 10.0)], context: CalculationFixtures.MapContext("m-all"));

        Assert.Equal(0.0, Req(plan, "i-crop").UnmetPerMinute, Precision);
        Assert.Equal(10.0, RunOf(plan, "r-grow")!.CyclesPerMinute, Precision);
        Assert.Equal(10.0, RunOf(plan, "r-pick")!.CyclesPerMinute, Precision);
        Assert.Equal(10.0, Supplied(plan, "i-water", SupplyKind.Gathered), Precision);
        Assert.False(HasWarning(plan, WarningCode.CycleDetected));
        Assert.False(HasWarning(plan, WarningCode.ConvergenceNotReached));
    }

    [Fact(DisplayName = "NCP-02: 種側正味増の循環も定常解へ収束する（サンドリーフ型）")]
    public void SeedPositiveCycleConverges()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F17SeedPositive(), [("i-crop", 10.0)]);

        Assert.Equal(0.0, Req(plan, "i-crop").UnmetPerMinute, Precision);
        Assert.Equal(20.0, RunOf(plan, "r-grow2")!.CyclesPerMinute, Precision);
        Assert.Equal(10.0, RunOf(plan, "r-pick2")!.CyclesPerMinute, Precision);
        Assert.False(HasWarning(plan, WarningCode.CycleDetected));
    }

    [Fact(DisplayName = "NCP-03: 正味減循環は解放せず未充足＋警告（ゲイン 1 以上）")]
    public void NegativeCycleStaysUnmet()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F17Negative(), [("i-a", 10.0)]);

        Assert.Equal(30.0, Req(plan, "i-a").RequiredPerMinute, Precision);
        Assert.Equal(20.0, Req(plan, "i-a").UnmetPerMinute, Precision);
        Assert.True(HasWarning(plan, WarningCode.CycleDetected));
    }

    [Fact(DisplayName = "NCP-04: 循環の起点アイテムを目標にしても解ける")]
    public void CycleOriginItemAsTarget()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F17(), [("i-seed", 10.0)], context: CalculationFixtures.MapContext("m-all"));

        Assert.Equal(0.0, Req(plan, "i-seed").UnmetPerMinute, Precision);
        Assert.Equal(20.0, RunOf(plan, "r-pick")!.CyclesPerMinute, Precision);
        Assert.Equal(10.0, RunOf(plan, "r-grow")!.CyclesPerMinute, Precision);
        Assert.Equal(10.0, Supplied(plan, "i-water", SupplyKind.Gathered), Precision);
    }

    [Fact(DisplayName = "NCP-05: 循環を経由する下流需要が充足する（炭塊型）")]
    public void DownstreamDemandThroughCycle()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F17WithDownstream(), [("i-char", 4.0)],
            context: CalculationFixtures.MapContext("m-all"));

        Assert.Equal(0.0, Req(plan, "i-char").UnmetPerMinute, Precision);
        Assert.Equal(2.0, RunOf(plan, "r-grow")!.CyclesPerMinute, Precision);
        Assert.Equal(2.0, RunOf(plan, "r-pick")!.CyclesPerMinute, Precision);
        Assert.Equal(2.0, RunOf(plan, "r-char")!.CyclesPerMinute, Precision);
        // 循環内アイテムの要求量は総需要（下流向け 2 ＋ 循環内消費 2）。
        Assert.Equal(4.0, Req(plan, "i-crop").RequiredPerMinute, Precision);
        Assert.Equal(2.0, Supplied(plan, "i-water", SupplyKind.Gathered), Precision);
        Assert.False(HasWarning(plan, WarningCode.CycleDetected));
    }

    [Fact(DisplayName = "NCP-06: 採取素材を含む正味増循環は採取充当後に解放する")]
    public void GatherableInPositiveCycle()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F17Gatherable(), [("i-ore", 100.0)],
            context: CalculationFixtures.MapContext("m-cap"));

        Assert.Equal(60.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Equal(80.0, Supplied(plan, "i-ore", SupplyKind.Recipe), Precision);
        Assert.Equal(40.0, RunOf(plan, "r-ore")!.CyclesPerMinute, Precision);
        Assert.Equal(40.0, RunOf(plan, "r-x")!.CyclesPerMinute, Precision);
        Assert.Equal(0.0, Req(plan, "i-ore").UnmetPerMinute, Precision);
        Assert.False(HasWarning(plan, WarningCode.CycleDetected));
    }

    [Fact(DisplayName = "NCP-07: 後から届いた副産物で未充足が消えた循環は警告しない")]
    public void CycleCoveredByLateByproductDoesNotWarn()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F04WithByproduct(), [("i-a", 10.0), ("i-x", 30.0)]);

        Assert.Equal(0.0, Req(plan, "i-a").UnmetPerMinute, Precision);
        Assert.Equal(20.0, plan.Surpluses.Single(s => s.ItemId == "i-a").ExcessPerMinute, Precision);
        Assert.False(HasWarning(plan, WarningCode.CycleDetected));
    }

    [Fact(DisplayName = "NCP-08: 循環内レシピの固定消費が追加需要として計上される")]
    public void FixedConsumptionInsideCycle()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F17WithFixedConsumption(), [("i-crop", 10.0)],
            context: CalculationFixtures.MapContext("m-all"));

        Assert.Equal(0.0, Req(plan, "i-crop").UnmetPerMinute, Precision);
        // i-water 要求 = 栽培入力 10 ＋ f-grow 切上げ 1 台 × 固定消費 6。
        Assert.Equal(16.0, Req(plan, "i-water").RequiredPerMinute, Precision);
        Assert.Equal(16.0, Supplied(plan, "i-water", SupplyKind.Gathered), Precision);
        Assert.False(HasWarning(plan, WarningCode.CycleDetected));
    }
}
