using EndfieldAicWeb.Application;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

/// <summary>VWU: 結果ビュー（未調整・切上げ台数フル稼働想定）。</summary>
public class UnadjustedViewTests
{
    private static (ProductionPlan Plan, ResultView View) Build(MasterDataSnapshot snapshot, params ProductionTarget[] targets)
    {
        ProductionPlan plan = ProductionCalculator.Calculate(
            snapshot,
            targets,
            new ContextFilter(),
            [],
            []);
        return (plan, ResultViewBuilder.Build(plan, snapshot, new ContextFilter(), unadjusted: true));
    }

    // VWU-01: 実数台数の切上げ分だけ全レシピの供給が増える（2.4 → 3 で ×1.25）。
    [Fact]
    public void SuppliesScaleByCeilOverExact()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A02();
        (_, ResultView view) = Build(snapshot, new ProductionTarget("i-t", 72));

        MaterialViewRow row = Assert.Single(view.Materials, m => m.ItemId == "i-t");
        SupplyPortion supply = Assert.Single(row.Supplies);
        Assert.Equal(90, supply.AmountPerMinute, 6);
    }

    // VWU-02: 共用設備はレシピ台数を合算したうえで全レシピに同じ倍率がかかる。
    [Fact]
    public void SharedFacilityScalesAllRecipesEqually()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A03();
        (_, ResultView view) = Build(
            snapshot,
            new ProductionTarget("i-x", 30),
            new ProductionTarget("i-y", 20));

        // 合計実数 10/3 → 切上げ 4 → 倍率 1.2
        MaterialViewRow x = Assert.Single(view.Materials, m => m.ItemId == "i-x");
        Assert.Equal(36, Assert.Single(x.Supplies).AmountPerMinute, 6);
        MaterialViewRow y = Assert.Single(view.Materials, m => m.ItemId == "i-y");
        Assert.Equal(24, Assert.Single(y.Supplies).AmountPerMinute, 6);
    }

    // VWU-03: 倍率の分母から散布機台数を除く。散布機の消費・台数はスケールしない。
    [Fact]
    public void ScaleDenominatorExcludesDispenserCount()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A01();
        (_, ResultView view) = Build(snapshot, new ProductionTarget("i-part", 60));

        // 加工機: 実数 3 → 切上げ 3 → 倍率 1。ガスは散布機由来の消費でスケールしない。
        MaterialViewRow part = Assert.Single(view.Materials, m => m.ItemId == "i-part");
        Assert.Equal(60, Assert.Single(part.Supplies).AmountPerMinute, 6);
        EnvironmentRequirement env = Assert.Single(view.Environments);
        Assert.Equal(1, env.DispenserCount);
        Assert.Equal(360, env.ConsumeRatePerMinuteTotal);
    }

    // VWU-03b: 散布機とレシピが同じ設備を共用する場合でも、散布機台数は切上げ分から控除される。
    [Fact]
    public void ScaleExcludesDispensersOnSharedFacility()
    {
        // f-fc がレシピと散布機を兼ねる: レシピ実数 0.5 + 散布機 1 = 切上げ 2 → レシピ倍率 (2-1)/0.5 = 2
        var snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-fuel", "燃料"), ApplicationFixtures.Item("i-gas", "ガス", TransportKind.Pipe), ApplicationFixtures.Item("i-fcx", "化学体")],
            [ApplicationFixtures.Facility("f-fc", "化学機", 10)],
            [ApplicationFixtures.Env("env-fcx", "化学環境", "f-fc", "i-gas", 360)],
            [],
            [
                ApplicationFixtures.Recipe("r-fcx", "化学体", [("i-fuel", 1)], [("i-fcx", 1)],
                    [ApplicationFixtures.Pair("f-fc", 30, "env-fcx")]),
            ]);

        (_, ResultView view) = Build(snapshot, new ProductionTarget("i-fcx", 1));

        MaterialViewRow row = Assert.Single(view.Materials, m => m.ItemId == "i-fcx");
        Assert.Equal(2, Assert.Single(row.Supplies).AmountPerMinute, 6);
        Assert.Equal(1, Assert.Single(view.Environments).DispenserCount);
    }

    // VWU-04: 未使用イベントアイテムは産出全量が余剰になる。
    [Fact]
    public void InactiveEventItemIsFullySurplus()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A05();
        (_, ResultView view) = Build(snapshot, new ProductionTarget("i-side", 8));

        // 0.8 → 切上げ 1 → 倍率 1.25 → 両産物 10/分。i-ltd はイベント無効で全量余剰。
        SurplusProduction surplus = Assert.Single(view.Surpluses, s => s.ItemId == "i-ltd");
        Assert.Equal(10, surplus.ExcessPerMinute, 6);
    }

    // VWU-05: 未調整でも設備行に推奨流量制限は表示しない。
    [Fact]
    public void NoFlowLimitsInUnadjustedView()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A02();
        (_, ResultView view) = Build(snapshot, new ProductionTarget("i-t", 72));

        FacilityViewRow facility = Assert.Single(view.Facilities);
        Assert.Empty(facility.FlowLimits);
    }
}
