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
            [ApplicationFixtures.Item("i-fuel", "燃料", gatherable: true), ApplicationFixtures.Item("i-gas", "ガス", TransportKind.Pipe, gatherable: true), ApplicationFixtures.Item("i-fcx", "化学体")],
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

    // VWU-06: 採取とレシピが併存するアイテムの未調整余剰は採取分を含めて再計算する。
    [Fact]
    public void UnadjustedSurplusIncludesGatheredSupply()
    {
        var snapshot = ApplicationFixtures.Snapshot(
            [
                ApplicationFixtures.Item("i-ore", "鉄鉱石", gatherable: true),
                ApplicationFixtures.Item("i-stone", "石材", gatherable: true),
            ],
            [ApplicationFixtures.Facility("f-mine", "採掘機", 10)],
            [],
            [],
            [
                ApplicationFixtures.Recipe("r-ore", "鉄鉱石", [("i-stone", 1)], [("i-ore", 1)],
                    [ApplicationFixtures.Pair("f-mine", 4)]),
            ],
            maps:
            [
                new GameMap
                {
                    Id = "m-cap",
                    Name = "上限マップ",
                    VersionAdded = "1.0.0",
                    GatherRates =
                    [
                        new GatherRate { ItemId = "i-ore", RatePerMinute = 60 },
                        new GatherRate { ItemId = "i-stone", IsUnlimited = true },
                    ],
                },
            ]);
        var context = new ContextFilter { MapId = "m-cap" };

        // 採取 60 + r-ore 40（f-mine 実数 2.667 → 切上げ 3 → 未調整産出 45）= 供給 105 → 余剰 5。
        ProductionPlan plan = ProductionCalculator.Calculate(
            snapshot, [new ProductionTarget("i-ore", 100)], context, [], [], []);
        ResultView view = ResultViewBuilder.Build(plan, snapshot, context, unadjusted: true);

        SurplusProduction surplus = Assert.Single(view.Surpluses, s => s.ItemId == "i-ore");
        Assert.Equal(5, surplus.ExcessPerMinute, 6);
    }

    // VWU-07: 環境ランと非環境ランが同一設備を共用するとき、環境ランの未調整供給は
    // 設備倍率ではなく散布機カバー残量で頭打ちになる（上書きは未調整でも適用）。
    [Fact]
    public void EnvRunStallsOnDispenserCoverInUnadjusted()
    {
        // r-hp 60/分 = 4.0 機、r-nc 10/分 = 2/3 機 → f-asm 合計 14/3 ≈ 4.67 → 切上げ 5・倍率 ≈1.07。
        // 散布機上書き 1 → カバー 4.0 機。設備倍率なら 64.3/分出るところ、カバー 4.0 機で 60/分に留まる。
        (_, ResultView view) = BuildWithEnvOverride(
            EnvSharedFacilitySnapshot(), 1, new ProductionTarget("i-hp", 60), new ProductionTarget("i-nc", 10));

        Assert.Equal(60.0, Supply(view, "i-hp"), 6);
        Assert.Equal(10.0 * (5.0 / (14.0 / 3.0)), Supply(view, "i-nc"), 6);
    }

    // VWU-08: カバー残量は計画のラン登録順（RunOrder）で配り、先のランから余りを受け取る。
    [Fact]
    public void CoverageRemainderIsDistributedInRunOrder()
    {
        // r-hp 30/分 = 2.0 機、r-std 22.8/分 = 1.9 機 → 環境予約 3.9 機、カバー 4.0 機で残 0.1 機。
        // 残量は先着の r-hp へ 0.1 機譲渡（2.1 機分 → 31.5/分）、r-std は実機械 1.9 のまま 22.8/分。
        (_, ResultView view) = BuildWithEnvOverride(
            EnvSharedFacilitySnapshot(), 1,
            new ProductionTarget("i-hp", 30), new ProductionTarget("i-std", 22.8), new ProductionTarget("i-nc", 7.5));

        Assert.Equal(31.5, Supply(view, "i-hp"), 6);
        Assert.Equal(22.8, Supply(view, "i-std"), 6);
    }

    // VWU-09: 調整済み計画で削減されたランの機械数も予約に含めるため、残量配分は発生しない。
    [Fact]
    public void AdjustedBlockedMachinesStayReserved()
    {
        // r-std は調整済みで 40/分需要が 2.0 機（24/分）へ削減済み。環境予約は実機械合計 4.0 機で残 0。
        // 設備倍率で育てば r-hp は 33.3/分・r-std も実績を越えるところ、予約分で上限に達し現状維持。
        // 非環境ランのみ設備倍率 ≈1.11 で 8.33/分。
        (ProductionPlan plan, ResultView view) = BuildWithEnvOverride(
            EnvSharedFacilitySnapshot(), 1,
            new ProductionTarget("i-hp", 30), new ProductionTarget("i-std", 40), new ProductionTarget("i-nc", 7.5));

        // 調整済み側の前提: i-std の 16/分は未充足。
        Assert.Equal(16.0, plan.ItemRequirements.Single(r => r.ItemId == "i-std").UnmetPerMinute, 6);

        Assert.Equal(30.0, Supply(view, "i-hp"), 6);
        Assert.Equal(24.0, Supply(view, "i-std"), 6);
        Assert.Equal(7.5 * (5.0 / 4.5), Supply(view, "i-nc"), 6);
    }

    private static double Supply(ResultView view, string itemId) =>
        Assert.Single(view.Materials, m => m.ItemId == itemId).Supplies.Sum(s => s.AmountPerMinute);

    /// <summary>散布機台数上書きを渡す Build（env-gas のみ）。</summary>
    private static (ProductionPlan Plan, ResultView View) BuildWithEnvOverride(
        MasterDataSnapshot snapshot, int dispenserCount, params ProductionTarget[] targets)
    {
        ProductionPlan plan = ProductionCalculator.Calculate(
            snapshot,
            targets,
            new ContextFilter(),
            [],
            [new EnvironmentCountOverride("env-gas", dispenserCount)],
            []);
        return (plan, ResultViewBuilder.Build(plan, snapshot, new ContextFilter(), unadjusted: true));
    }

    /// <summary>
    /// VWU-07〜09 用フィクスチャ。f-asm 上に環境ペア 2 種（4 秒・5 秒）と非環境ペアを持ち、
    /// env-gas は散布機 1 台で 4 機をカバーする。
    /// </summary>
    private static MasterDataSnapshot EnvSharedFacilitySnapshot() => ApplicationFixtures.Snapshot(
        [
            ApplicationFixtures.Item("i-ore", "採取素材", gatherable: true),
            ApplicationFixtures.Item("i-gas", "活性ガス", TransportKind.Pipe, gatherable: true),
            ApplicationFixtures.Item("i-hp", "熱処理品"),
            ApplicationFixtures.Item("i-std", "安定品"),
            ApplicationFixtures.Item("i-nc", "非環境品"),
        ],
        [
            ApplicationFixtures.Facility("f-asm", "加工機", 50),
            ApplicationFixtures.Facility("f-disp", "ガス散布機", 20),
        ],
        [ApplicationFixtures.Env("env-gas", "ガス散布", "f-disp", "i-gas", 360)],
        [],
        [
            ApplicationFixtures.Recipe("r-hp", "熱処理品", [("i-ore", 1)], [("i-hp", 1)],
                [ApplicationFixtures.Pair("f-asm", 4, "env-gas")]),
            ApplicationFixtures.Recipe("r-std", "安定品", [("i-ore", 1)], [("i-std", 1)],
                [ApplicationFixtures.Pair("f-asm", 5, "env-gas")]),
            ApplicationFixtures.Recipe("r-nc", "非環境品", [("i-ore", 1)], [("i-nc", 1)],
                [ApplicationFixtures.Pair("f-asm", 4)]),
        ]);

    // DSP-15・DSP-21: 未調整ビューの処理消費表示（docs/phases/test-specification-phase32.md §1）。

    /// <summary>r-m が副産物 i-sew を出し、r-disp（f-trt 5 秒）が処理する構成。</summary>
    private static MasterDataSnapshot DisposalFixture() => ApplicationFixtures.Snapshot(
        [
            ApplicationFixtures.Item("i-ore", "鉄鉱石", gatherable: true),
            ApplicationFixtures.Item("i-p", "製品"),
            ApplicationFixtures.Item("i-sew", "汚水"),
        ],
        [
            ApplicationFixtures.Facility("f-asm", "加工機", 10),
            ApplicationFixtures.Facility("f-trt", "水処理設備", 10),
        ],
        [], [],
        [
            ApplicationFixtures.Recipe("r-m", "製造", [("i-ore", 1)], [("i-p", 1), ("i-sew", 1)],
                [ApplicationFixtures.Pair("f-asm", 4)]),
            ApplicationFixtures.Recipe("r-disp", "汚水処理", [("i-sew", 1)], [],
                [ApplicationFixtures.Pair("f-trt", 5)]),
        ]);

    // DSP-15: 処理消費も未調整ビューでは設備倍率で拡大され、利用可能量でクランプされる。
    [Fact]
    public void DisposalClampsToUnadjustedAvailability()
    {
        // i-p 45/分: r-m 45 サイクル×4 秒 = 実数 3.0 → 倍率 1.0 で副産物 45/分。
        // r-disp 45 サイクル×5 秒 = 実数 3.75 → 切上げ 4 → 倍率 4/3.75 で生表示 48/分。
        // 利用可能量 45/分を超えないようクランプして 45/分を表示し余剰行は出さない。
        (_, ResultView view) = Build(DisposalFixture(), new ProductionTarget("i-p", 45));

        MaterialViewRow row = Assert.Single(view.Materials, m => m.ItemId == "i-sew");
        Assert.Equal(45, row.DisposalPerMinute, 6);
        Assert.DoesNotContain(view.Surpluses, s => s.ItemId == "i-sew");
    }

    // DSP-21: 生産側にも切上げがある場合は両側の拡大が整合する。
    [Fact]
    public void DisposalMatchesUnadjustedProduction()
    {
        // i-p 40/分: r-m 40 サイクル×4 秒 = 実数 8/3 → 切上げ 3 → 倍率 1.125 で副産物 45/分。
        // r-disp 40 サイクル×5 秒 = 実数 10/3 → 切上げ 4 → 倍率 1.2 で生表示 48/分。
        // 処理表示は 45/分に揃い、余剰も出ない（調整済みでは余剰 0）。
        (_, ResultView view) = Build(DisposalFixture(), new ProductionTarget("i-p", 40));

        MaterialViewRow row = Assert.Single(view.Materials, m => m.ItemId == "i-sew");
        Assert.Equal(45, row.DisposalPerMinute, 6);
        Assert.DoesNotContain(view.Surpluses, s => s.ItemId == "i-sew");
    }
}
