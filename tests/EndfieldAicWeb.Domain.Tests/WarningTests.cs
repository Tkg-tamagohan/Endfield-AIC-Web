using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;
using F = EndfieldAicWeb.Domain.Tests.CalculationFixtures;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>WRN: 警告と入力検証（docs/phases/test-specification-phase2.md §3）。</summary>
public class WarningTests
{
    [Fact(DisplayName = "WRN-01: レシピなし部品は未充足＋警告（例外ではない）")]
    public void MissingRecipeIsUnmetWithWarning()
    {
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-x")], [], []);

        ProductionPlan plan = F.Run(master, [("i-x", 10.0)]);

        Assert.True(HasWarning(plan, WarningCode.NoRecipeAvailable));
        Assert.Equal(10.0, Req(plan, "i-x").UnmetPerMinute, Precision);
    }

    [Fact(DisplayName = "WRN-02: レシピなし採取素材は外部調達（警告なし）")]
    public void MissingRecipeGatherableIsProcured()
    {
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true)], [], []);

        ProductionPlan plan = F.Run(master, [("i-ore", 10.0)]);

        Assert.Empty(plan.Warnings);
        Assert.Equal(10.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Equal(0.0, Req(plan, "i-ore").UnmetPerMinute, Precision);
    }

    [Fact(DisplayName = "WRN-03: 複数警告が同時に返る")]
    public void MultipleWarningsAreReturned()
    {
        MasterDataSnapshot master = F.Snapshot(
            [.. F.F04().Items, F.Item("i-miss")],
            F.F04().Facilities,
            F.F04().Recipes);

        ProductionPlan plan = F.Run(master, [("i-a", 10.0), ("i-miss", 5.0)]);

        Assert.True(HasWarning(plan, WarningCode.CycleDetected));
        Assert.True(HasWarning(plan, WarningCode.NoRecipeAvailable));
    }

    [Fact(DisplayName = "WRN-04: 不正な目標は ArgumentException")]
    public void InvalidTargetThrows()
    {
        MasterDataSnapshot master = F.F01();

        Assert.Throws<ArgumentException>(() => ProductionCalculator.Calculate(
            master,
            [new ProductionTarget("i-part", 0.0)],
            new ContextFilter(), [], [], []));
        Assert.Throws<ArgumentException>(() => ProductionCalculator.Calculate(
            master,
            [new ProductionTarget("i-part", -5.0)],
            new ContextFilter(), [], [], []));
        Assert.Throws<ArgumentException>(() => ProductionCalculator.Calculate(
            master,
            [new ProductionTarget("i-part", double.NaN)],
            new ContextFilter(), [], [], []));
        Assert.Throws<ArgumentException>(() => ProductionCalculator.Calculate(
            master,
            [new ProductionTarget("i-part", double.PositiveInfinity)],
            new ContextFilter(), [], [], []));
        Assert.Throws<ArgumentException>(() => ProductionCalculator.Calculate(
            master,
            [new ProductionTarget("i-ghost", 10.0)],
            new ContextFilter(), [], [], []));
    }

    [Fact(DisplayName = "WRN-05: 未知環境への台数上書きは警告")]
    public void UnknownEnvironmentOverrideWarns()
    {
        ProductionPlan plan = F.Run(
            F.F01(), [("i-part", 30.0)],
            environmentOverrides: [new EnvironmentCountOverride("env-none", 2)]);

        Assert.True(HasWarning(plan, WarningCode.InvalidEnvironmentOverride));
    }

    [Fact(DisplayName = "WRN-06: Category タグだけでは外部調達扱いにならない")]
    public void CategoryTagDoesNotImplyGatherable()
    {
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-tag-only", "採取素材")], [], []);

        ProductionPlan plan = F.Run(master, [("i-tag-only", 10.0)]);

        Assert.True(HasWarning(plan, WarningCode.NoRecipeAvailable));
        Assert.Equal(10.0, Req(plan, "i-tag-only").UnmetPerMinute, Precision);
        Assert.Equal(0.0, Supplied(plan, "i-tag-only", SupplyKind.Gathered), Precision);
    }

    [Fact(DisplayName = "WRN-07: ユニット入力がベルト容量を超える構成でも警告が出ない（BV）")]
    public void OverBeltUnitInputDoesNotWarn()
    {
        // r-t は i-u×4 / 2秒 → i-t 60/分でユニット入力 120/分（旧 TRN-01 の発火構成）。
        ProductionPlan plan = F.Run(F.F06(), [("i-t", 60.0)]);

        Assert.Empty(plan.Warnings);
    }

    [Fact(DisplayName = "WRN-08: 散布機の環境消費がパイプ容量を超える構成でも警告が出ない（BV）")]
    public void OverPipeEnvironmentConsumeDoesNotWarn()
    {
        // r-std は env-gas ペアのみ → 散布機ユニットの i-gas 消費 360/分（旧 TRN-02 の発火構成）。
        ProductionPlan plan = F.Run(F.F10(), [("i-std", 10.0)]);

        Assert.Empty(plan.Warnings);
    }

    [Fact(DisplayName = "WRN-09: 警告文のアイテム参照が名前（Id）表記（CF）")]
    public void NoRecipeWarningUsesNameWithId()
    {
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-x", name: "加工部品")], [], []);

        ProductionPlan plan = F.Run(master, [("i-x", 10.0)]);

        CalculationWarning warning =
            Assert.Single(plan.Warnings, w => w.Code == WarningCode.NoRecipeAvailable);
        Assert.Contains("アイテム", warning.Message);
        Assert.Contains("加工部品（i-x）", warning.Message);
    }

    [Fact(DisplayName = "WRN-10: 存在しない参照は Id のみ表示（CF フォールバック）")]
    public void UnresolvedReferenceFallsBackToIdOnly()
    {
        ProductionPlan plan = F.Run(
            F.F01(), [("i-part", 30.0)],
            environmentOverrides: [new EnvironmentCountOverride("env-none", 2)]);

        CalculationWarning warning =
            Assert.Single(plan.Warnings, w => w.Code == WarningCode.InvalidEnvironmentOverride);
        Assert.Contains("env-none", warning.Message);
        Assert.DoesNotContain("（", warning.Message);
    }

    [Fact(DisplayName = "WRN-11: 循環パスの各要素が名前（Id）表記（CF）")]
    public void CyclePathElementsUseNameWithId()
    {
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-a", name: "アイテムA"), F.Item("i-b", name: "アイテムB")],
            [F.Facility("f-cyc")],
            [
                F.Recipe("r-cyc-a", "f-cyc", 6.0, [("i-b", 1.0)], [("i-a", 1.0)]),
                F.Recipe("r-cyc-b", "f-cyc", 6.0, [("i-a", 1.0)], [("i-b", 1.0)]),
            ]);

        ProductionPlan plan = F.Run(master, [("i-a", 10.0)]);

        CalculationWarning warning =
            Assert.Single(plan.Warnings, w => w.Code == WarningCode.CycleDetected);
        Assert.Contains("アイテムA（i-a） → アイテムB（i-b） → アイテムA（i-a）", warning.Message);
    }

    [Fact(DisplayName = "WRN-12: イベント参照が名前（Id）で解決される（CF）")]
    public void EventReferenceUsesNameWithId()
    {
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ltd", "部品", TransportKind.Belt, "ev-ltd", name: "限定部品")],
            [],
            [],
            gameEvents: [F.GameEvent("ev-ltd", name: "期間限定イベント")]);

        ProductionPlan plan = F.Run(master, [("i-ltd", 10.0)]);

        CalculationWarning warning =
            Assert.Single(plan.Warnings, w => w.Code == WarningCode.EventItemUnavailable);
        Assert.Contains("期間限定イベント（ev-ltd）", warning.Message);
    }
}
