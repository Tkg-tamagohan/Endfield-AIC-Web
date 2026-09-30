using EndfieldAicWeb.Domain.Calculation;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;
using F = EndfieldAicWeb.Domain.Tests.CalculationFixtures;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>GAT: 採取上限の計算（仕様決定 AC・AD・AE、docs/phases/test-specification-phase11.md §3）。</summary>
public class GatherCapTests
{
    [Fact(DisplayName = "GAT-01: マップ未選択は採取無制限")]
    public void UnselectedMapMeansUnlimitedGathering()
    {
        ProductionPlan plan = F.Run(F.F15(), [("i-ore", 100.0)]);

        Assert.Equal(100.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Null(RunOf(plan, "r-ore"));
        Assert.Empty(plan.Warnings);
    }

    [Fact(DisplayName = "GAT-02: 上限内は採取")]
    public void DemandWithinCapIsGathered()
    {
        ProductionPlan plan = F.Run(
            F.F15(), [("i-ore", 50.0)], context: F.MapContext("m-cap"));

        Assert.Equal(50.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Null(RunOf(plan, "r-ore"));
        Assert.Empty(plan.Warnings);
    }

    [Fact(DisplayName = "GAT-03: 超過分はレシピへ展開")]
    public void ExcessExpandsToRecipe()
    {
        ProductionPlan plan = F.Run(
            F.F15(), [("i-ore", 100.0)], context: F.MapContext("m-cap"));

        Assert.Equal(60.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Equal(40.0, Supplied(plan, "i-ore", SupplyKind.Recipe), Precision);
        Assert.Equal(40.0, RunOf(plan, "r-ore")!.CyclesPerMinute, Precision);

        // r-ore の 40 サイクル/分は i-stone を 40 需要するが上限 30。
        Assert.Equal(30.0, Supplied(plan, "i-stone", SupplyKind.Gathered), Precision);
        Assert.Equal(10.0, Req(plan, "i-stone").UnmetPerMinute, Precision);
        Assert.True(HasWarning(plan, WarningCode.GatherCapExceeded));
    }

    [Fact(DisplayName = "GAT-04: 代替レシピなしは未充足＋警告")]
    public void NoAlternativeRecipeIsUnmetWithWarning()
    {
        ProductionPlan plan = F.Run(
            F.F15(), [("i-shard", 20.0)], context: F.MapContext("m-cap"));

        Assert.Equal(10.0, Supplied(plan, "i-shard", SupplyKind.Gathered), Precision);
        Assert.Equal(10.0, Req(plan, "i-shard").UnmetPerMinute, Precision);
        Assert.True(HasWarning(plan, WarningCode.GatherCapExceeded));
    }

    [Fact(DisplayName = "GAT-05: 行のない採取素材は上限 0")]
    public void MissingGatherRowCapsAtZero()
    {
        ProductionPlan plan = F.Run(
            F.F15(), [("i-ore", 50.0)], context: F.MapContext("m-none"));

        Assert.Equal(0.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Equal(50.0, Supplied(plan, "i-ore", SupplyKind.Recipe), Precision);
        Assert.Equal(50.0, RunOf(plan, "r-ore")!.CyclesPerMinute, Precision);

        // m-none は全行なし → i-stone も上限 0 で全量未充足。
        Assert.Equal(50.0, Req(plan, "i-stone").UnmetPerMinute, Precision);
        Assert.True(HasWarning(plan, WarningCode.GatherCapExceeded));
    }

    [Fact(DisplayName = "GAT-06: 無限行は上限なし")]
    public void UnlimitedRowHasNoCap()
    {
        ProductionPlan plan = F.Run(
            F.F15(), [("i-ore", 500.0)], context: F.MapContext("m-inf"));

        Assert.Equal(500.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Null(RunOf(plan, "r-ore"));
        Assert.Empty(plan.Warnings);
    }

    [Fact(DisplayName = "GAT-07: ユーザー上書きは有効レートの置き換え")]
    public void UserOverrideReplacesEffectiveRate()
    {
        ProductionPlan plan = F.Run(
            F.F15(), [("i-ore", 50.0)], context: F.MapContext("m-cap"),
            gatherOverrides: [new GatherRateOverride("i-ore", 30.0)]);

        Assert.Equal(30.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Equal(20.0, Supplied(plan, "i-ore", SupplyKind.Recipe), Precision);
        Assert.Equal(20.0, RunOf(plan, "r-ore")!.CyclesPerMinute, Precision);
    }

    [Fact(DisplayName = "GAT-08: 上書きはマップ値超過も許可")]
    public void OverrideMayExceedMapRate()
    {
        ProductionPlan plan = F.Run(
            F.F15(), [("i-ore", 100.0)], context: F.MapContext("m-cap"),
            gatherOverrides: [new GatherRateOverride("i-ore", 90.0)]);

        Assert.Equal(90.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Equal(10.0, Supplied(plan, "i-ore", SupplyKind.Recipe), Precision);
        Assert.Equal(10.0, RunOf(plan, "r-ore")!.CyclesPerMinute, Precision);
    }

    [Fact(DisplayName = "GAT-09: 行なしアイテムへ上書き")]
    public void OverrideAppliesToItemWithoutRow()
    {
        ProductionPlan plan = F.Run(
            F.F15(), [("i-ore", 50.0)], context: F.MapContext("m-none"),
            gatherOverrides: [new GatherRateOverride("i-ore", 20.0)]);

        Assert.Equal(20.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Equal(30.0, Supplied(plan, "i-ore", SupplyKind.Recipe), Precision);
        Assert.Equal(30.0, RunOf(plan, "r-ore")!.CyclesPerMinute, Precision);
    }

    [Fact(DisplayName = "GAT-10: 非有効イベントのマップは全採取不可＋上書き無効")]
    public void InactiveEventMapCapsEverythingAtZero()
    {
        ProductionPlan plan = F.Run(
            F.F15(), [("i-ore", 50.0)], context: F.MapContext("m-ev"),
            gatherOverrides: [new GatherRateOverride("i-ore", 10.0)]);

        Assert.True(HasWarning(plan, WarningCode.GatherMapUnavailable));
        Assert.Equal(0.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Equal(50.0, Supplied(plan, "i-ore", SupplyKind.Recipe), Precision);
        Assert.Equal(50.0, RunOf(plan, "r-ore")!.CyclesPerMinute, Precision);

        // 全採取素材が上限 0 になるため i-stone も採取できない。
        Assert.Equal(50.0, Req(plan, "i-stone").UnmetPerMinute, Precision);
        Assert.True(HasWarning(plan, WarningCode.GatherCapExceeded));
    }

    [Fact(DisplayName = "GAT-11: 非有効イベントのマップで代替なしは未充足")]
    public void InactiveEventMapWithoutAlternativeIsUnmet()
    {
        ProductionPlan plan = F.Run(
            F.F15(), [("i-shard", 20.0)], context: F.MapContext("m-ev-shard"));

        Assert.Equal(0.0, Supplied(plan, "i-shard", SupplyKind.Gathered), Precision);
        Assert.Equal(20.0, Req(plan, "i-shard").UnmetPerMinute, Precision);
        Assert.True(HasWarning(plan, WarningCode.GatherMapUnavailable));
        Assert.True(HasWarning(plan, WarningCode.GatherCapExceeded));
    }

    [Fact(DisplayName = "GAT-12: 採取上限は需要の発生源を問わない（固定消費由来）")]
    public void CapAppliesToFixedConsumptionDemand()
    {
        // r-fx は i-fx 10/分で 5 台 → i-ore 固定消費 80/分。
        ProductionPlan plan = F.Run(
            F.F15WithFixedConsumption(), [("i-fx", 10.0)], context: F.MapContext("m-cap"));

        Assert.Equal(80.0, Req(plan, "i-ore").RequiredPerMinute, Precision);
        Assert.Equal(60.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Equal(20.0, Supplied(plan, "i-ore", SupplyKind.Recipe), Precision);
        Assert.Equal(20.0, RunOf(plan, "r-ore")!.CyclesPerMinute, Precision);
    }

    [Fact(DisplayName = "GAT-13: 採取上限は需要の発生源を問わない（環境消費由来）")]
    public void CapAppliesToEnvironmentConsumptionDemand()
    {
        // r-hot は env-burn 散布機 1 台 → i-ore 環境消費 80/分。
        ProductionPlan plan = F.Run(
            F.F15WithEnvConsumption(), [("i-hot", 10.0)], context: F.MapContext("m-cap"));

        Assert.Equal(80.0, Req(plan, "i-ore").RequiredPerMinute, Precision);
        Assert.Equal(60.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Equal(20.0, Supplied(plan, "i-ore", SupplyKind.Recipe), Precision);
        Assert.Equal(20.0, RunOf(plan, "r-ore")!.CyclesPerMinute, Precision);
    }

    [Fact(DisplayName = "GAT-14: 超過レシピの入力も採取上限の対象（連鎖）")]
    public void AlternativeRecipeInputsAreCappedToo()
    {
        ProductionPlan plan = F.Run(
            F.F15(), [("i-ore", 200.0)], context: F.MapContext("m-cap"));

        Assert.Equal(60.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Equal(140.0, Supplied(plan, "i-ore", SupplyKind.Recipe), Precision);
        Assert.Equal(140.0, RunOf(plan, "r-ore")!.CyclesPerMinute, Precision);

        Assert.Equal(140.0, Req(plan, "i-stone").RequiredPerMinute, Precision);
        Assert.Equal(30.0, Supplied(plan, "i-stone", SupplyKind.Gathered), Precision);
        Assert.Equal(110.0, Req(plan, "i-stone").UnmetPerMinute, Precision);
        Assert.True(HasWarning(plan, WarningCode.GatherCapExceeded));
    }

    [Fact(DisplayName = "GAT-15: 副産物は採取より先に残差を減らす")]
    public void ByproductOffsetsGatheredAmount()
    {
        // i-part 1/分 → r-side 1 サイクル/分が i-ore を 30/分 副産。
        // i-ore 需要 100 − 副産物 30 = 残差 70 → 採取 60 + r-ore 展開 10。
        ProductionPlan plan = F.Run(
            F.F15WithByproduct(), [("i-part", 1.0), ("i-ore", 100.0)],
            context: F.MapContext("m-cap"),
            overrides:
            [
                F.Override("i-part", "r-side", "f-asm", 6.0),
                F.Override("i-ore", "r-ore", "f-mine", 4.0),
            ]);

        Assert.Equal(30.0, Supplied(plan, "i-ore", SupplyKind.Byproduct), Precision);
        Assert.Equal(60.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Equal(10.0, Supplied(plan, "i-ore", SupplyKind.Recipe), Precision);
        Assert.Equal(10.0, RunOf(plan, "r-ore")!.CyclesPerMinute, Precision);
    }

    [Fact(DisplayName = "GAT-16: 採取素材のレシピが循環する場合")]
    public void GatherableRecipeCycleWarnsAndKeepsGatheredAmount()
    {
        ProductionPlan plan = F.Run(
            F.F15WithCycle(), [("i-ore", 100.0)], context: F.MapContext("m-cap"));

        Assert.Equal(60.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.True(HasWarning(plan, WarningCode.CycleDetected));
        Assert.Equal(40.0, Req(plan, "i-ore").UnmetPerMinute, Precision);
    }

    [Fact(DisplayName = "GAT-17: 不明なマップ Id（暫定解釈）")]
    public void UnknownMapIdWarnsAndCapsAtZero()
    {
        ProductionPlan plan = F.Run(
            F.F15(), [("i-ore", 50.0)], context: F.MapContext("m-ghost"));

        Assert.True(HasWarning(plan, WarningCode.InvalidGatherMap));
        Assert.Equal(0.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Equal(50.0, Supplied(plan, "i-ore", SupplyKind.Recipe), Precision);
        Assert.Equal(50.0, RunOf(plan, "r-ore")!.CyclesPerMinute, Precision);
    }

    [Fact(DisplayName = "GAT-18: 不正な上書きは無視＋警告（暫定解釈）")]
    public void InvalidOverridesAreIgnoredWithWarning()
    {
        ProductionPlan plan = F.Run(
            F.F15(), [("i-ore", 100.0)], context: F.MapContext("m-cap"),
            gatherOverrides:
            [
                new GatherRateOverride("i-ore", -5.0),
                new GatherRateOverride("i-ghost", 10.0),
                new GatherRateOverride("i-part", 10.0),
            ]);

        Assert.True(HasWarning(plan, WarningCode.InvalidGatherRateOverride));
        Assert.Equal(60.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Equal(40.0, Supplied(plan, "i-ore", SupplyKind.Recipe), Precision);
        Assert.Equal(40.0, RunOf(plan, "r-ore")!.CyclesPerMinute, Precision);
    }

    [Fact(DisplayName = "GAT-20: 後の引き戻しで不足が解消された場合は警告を残さない")]
    public void ResolvedShortfallLeavesNoWarning()
    {
        // i-x → r-x で i-shard 20 需要（上限 10 → 不足 10）となるが、
        // i-y 向け r-y の副産物 i-x 10 で r-x が引き戻され i-shard 需要が消える。
        ProductionPlan plan = F.Run(
            F.F16(), [("i-x", 10.0), ("i-y", 1.0)],
            context: F.MapContext("m-g16"),
            overrides: [F.Override("i-x", "r-x", "f-asm", 4.0)]);

        // i-shard の需要は 0 まで引き戻され、要求行ごと消える（警告も残らない）。
        Assert.False(HasReq(plan, "i-shard"));
        Assert.False(HasWarning(plan, WarningCode.GatherCapExceeded));
        Assert.Null(RunOf(plan, "r-x"));
        Assert.Equal(10.0, Supplied(plan, "i-x", SupplyKind.Byproduct), Precision);
    }

    [Fact(DisplayName = "GAT-19: 採取素材でも所属イベントが非有効なら不可")]
    public void InactiveEventGatherableIsUnavailable()
    {
        ProductionPlan plan = F.Run(
            F.F15WithInactiveGatherable(), [("i-ore", 50.0)], context: F.MapContext("m-cap"));

        Assert.True(HasWarning(plan, WarningCode.EventItemUnavailable));
        Assert.Equal(0.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Equal(50.0, Req(plan, "i-ore").UnmetPerMinute, Precision);
    }
}
