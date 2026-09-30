using EndfieldAicWeb.Application;
using EndfieldAicWeb.Domain.Calculation;

namespace EndfieldAicWeb.Application.Tests;

/// <summary>VW: 結果ビュー（調整済）。</summary>
public class ResultViewTests
{
    private static ProductionPlan Plan(MasterDataSnapshot snapshot, params ProductionTarget[] targets) =>
        ProductionCalculator.Calculate(
            snapshot,
            targets,
            new ContextFilter(),
            [],
            []);

    // VW-01: 素材行の未達量はレシピ供給を差し引いた残り。基底素材は未達 0。
    [Fact]
    public void MaterialRowsCarryRequiredAndUnmet()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A01();
        ProductionPlan plan = Plan(snapshot, new ProductionTarget("i-part", 60));

        ResultView view = ResultViewBuilder.Build(plan, snapshot, new ContextFilter(), unadjusted: false);

        MaterialViewRow part = Assert.Single(view.Materials, m => m.ItemId == "i-part");
        Assert.Equal(60, part.RequiredPerMinute);
        Assert.Equal(0, part.UnmetPerMinute);
        Assert.Contains(part.Supplies, s => s.RecipeId == "r-part");
        MaterialViewRow ore = Assert.Single(view.Materials, m => m.ItemId == "i-ore");
        Assert.Equal(120, ore.RequiredPerMinute);
        SupplyPortion rawSupply = Assert.Single(ore.Supplies);
        Assert.Equal(SupplyKind.RawMaterial, rawSupply.Kind);
    }

    // VW-02: 設備行に実数・切上げ台数と推奨流量制限が入る。
    [Fact]
    public void FacilityRowsCarryCountsAndFlowLimits()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A02();
        ProductionPlan plan = Plan(snapshot, new ProductionTarget("i-t", 72));

        ResultView view = ResultViewBuilder.Build(plan, snapshot, new ContextFilter(), unadjusted: false);

        FacilityViewRow facility = Assert.Single(view.Facilities);
        Assert.Equal("f-t", facility.FacilityId);
        Assert.Equal(2.4, facility.ExactCount, 9);
        Assert.Equal(3, facility.CeilCount);
        FlowAdjustment limit = Assert.Single(facility.FlowLimits);
        Assert.Equal("r-t", limit.RecipeId);
        Assert.Equal("i-u", limit.InputItemId);
    }

    // VW-03: 環境行の台数・消費量が出る。
    [Fact]
    public void EnvironmentRowsCarryCountsAndConsumption()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A01();
        ProductionPlan plan = Plan(snapshot, new ProductionTarget("i-part", 60));

        ResultView view = ResultViewBuilder.Build(plan, snapshot, new ContextFilter(), unadjusted: false);

        EnvironmentRequirement env = Assert.Single(view.Environments);
        Assert.Equal("env-gas", env.EnvironmentId);
        Assert.Equal("f-disp", env.ProviderFacilityId);
        Assert.Equal(1, env.DispenserCount);
        Assert.Equal("i-gas", env.ConsumeItemId);
        Assert.Equal(6, env.ConsumeRatePerSecondTotal);
    }

    // VW-04: 警告と余剰行が入る。
    [Fact]
    public void WarningsAndSurplusesPassThrough()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A01();
        ProductionPlan plan = ProductionCalculator.Calculate(
            snapshot,
            [new ProductionTarget("i-part", 60)],
            new ContextFilter(),
            [],
            [new EnvironmentCountOverride("env-unknown", 1)]);

        ResultView view = ResultViewBuilder.Build(plan, snapshot, new ContextFilter(), unadjusted: false);

        Assert.NotEmpty(view.Warnings);
        Assert.Empty(view.Surpluses);
    }

    // VW-05: 素材行に選択中ペアのキーが入る。
    [Fact]
    public void MaterialRowsExposeSelectedPairKey()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A01();
        ProductionPlan plan = Plan(snapshot, new ProductionTarget("i-part", 60));

        ResultView view = ResultViewBuilder.Build(plan, snapshot, new ContextFilter(), unadjusted: false);

        MaterialViewRow part = Assert.Single(view.Materials, m => m.ItemId == "i-part");
        Assert.Equal(PairOptionKey.Create("r-part", ApplicationFixtures.A01().Recipes[0].Facilities[1]), part.SelectedPairKey);
        MaterialViewRow ore = Assert.Single(view.Materials, m => m.ItemId == "i-ore");
        Assert.Null(ore.SelectedPairKey);
    }
}
