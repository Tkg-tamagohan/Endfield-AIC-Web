using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;
using F = EndfieldAicWeb.Domain.Tests.CalculationFixtures;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>WRN: 警告と入力検証（test-specification-phase2 §3）。</summary>
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

    [Fact(DisplayName = "WRN-02: レシピなし基礎素材は外部調達（警告なし）")]
    public void MissingRecipeRawMaterialIsProcured()
    {
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "基礎素材", TransportKind.Belt, null, true)], [], []);

        ProductionPlan plan = F.Run(master, [("i-ore", 10.0)]);

        Assert.Empty(plan.Warnings);
        Assert.Equal(10.0, Supplied(plan, "i-ore", SupplyKind.RawMaterial), Precision);
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
        var calculator = new ProductionCalculator();
        MasterDataSnapshot master = F.F01();

        Assert.Throws<ArgumentException>(() => calculator.Calculate(
            master,
            [new ProductionTarget("i-part", 0.0)],
            new ContextFilter(), [], []));
        Assert.Throws<ArgumentException>(() => calculator.Calculate(
            master,
            [new ProductionTarget("i-part", -5.0)],
            new ContextFilter(), [], []));
        Assert.Throws<ArgumentException>(() => calculator.Calculate(
            master,
            [new ProductionTarget("i-part", double.NaN)],
            new ContextFilter(), [], []));
        Assert.Throws<ArgumentException>(() => calculator.Calculate(
            master,
            [new ProductionTarget("i-part", double.PositiveInfinity)],
            new ContextFilter(), [], []));
        Assert.Throws<ArgumentException>(() => calculator.Calculate(
            master,
            [new ProductionTarget("i-ghost", 10.0)],
            new ContextFilter(), [], []));
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
    public void CategoryTagDoesNotImplyBaseMaterial()
    {
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-tag-only", "基礎素材")], [], []);

        ProductionPlan plan = F.Run(master, [("i-tag-only", 10.0)]);

        Assert.True(HasWarning(plan, WarningCode.NoRecipeAvailable));
        Assert.Equal(10.0, Req(plan, "i-tag-only").UnmetPerMinute, Precision);
        Assert.Equal(0.0, Supplied(plan, "i-tag-only", SupplyKind.RawMaterial), Precision);
    }
}
