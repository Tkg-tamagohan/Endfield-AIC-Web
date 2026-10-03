using EndfieldAicWeb.Application;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

/// <summary>PVD: 既定ビュー規則（仕様決定 O・I の UI 既定）。</summary>
public class PlanViewDefaultsTests
{
    private static ProductionPlan Plan(MasterDataSnapshot snapshot, params ProductionTarget[] targets) =>
        ProductionCalculator.Calculate(
            snapshot,
            targets,
            new ContextFilter(),
            [],
            [],
            []);

    // PVD-01: 全設備が整数台数なら未調整表示が既定（両ビューの見え方が同じため）。
    [Fact]
    public void IntegerCountsDefaultToUnadjusted()
    {
        // A-01・i-part 60/分 → 加工機 3.0 台 + 散布機 1 台で切上げ過剰なし。
        ProductionPlan plan = Plan(ApplicationFixtures.A01(), new ProductionTarget("i-part", 60));

        Assert.True(PlanViewDefaults.DefaultUnadjusted(plan));
    }

    // PVD-02: 切上げ過剰が出る計画では調整済が既定（仕様決定 O）。
    [Fact]
    public void FractionalCountsDefaultToAdjusted()
    {
        // A-02・i-t 72/分 → 実数 2.4 → 切上げ 3。
        ProductionPlan plan = Plan(ApplicationFixtures.A02(), new ProductionTarget("i-t", 72));

        Assert.False(PlanViewDefaults.DefaultUnadjusted(plan));
    }

    // PVD-03: 散布機台数の入力範囲は（必要台数, 利用機械数の切上げ）（仕様決定 BS）。
    [Fact]
    public void DispenserRangeIsRequiredToUsedMachines()
    {
        // A-01・既定は 3 秒 env-gas ペア → i-part 60/分で 3.0 機、必要台数 ceil(3.0/4) = 1。
        ProductionPlan plan = Plan(ApplicationFixtures.A01(), new ProductionTarget("i-part", 60));

        Assert.Equal((1, 3), PlanViewDefaults.DispenserRange(plan, "env-gas"));
    }

    // PVD-04: 同一環境を使うレシピが複数あれば機械数の合算が上限になる。
    [Fact]
    public void DispenserRangeSumsMachinesAcrossRecipes()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [
                ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
                ApplicationFixtures.Item("i-x", "中間品X"),
                ApplicationFixtures.Item("i-y", "中間品Y"),
                ApplicationFixtures.Item("i-gas", "活性ガス", TransportKind.Pipe, gatherable: true),
            ],
            [ApplicationFixtures.Facility("f-a", "機A", 10), ApplicationFixtures.Facility("f-d", "散布機", 5)],
            [ApplicationFixtures.Env("env-1", "環境1", "f-d", "i-gas", 60)],
            [],
            [
                ApplicationFixtures.Recipe("r-x", "中間品X", [("i-u", 1)], [("i-x", 1)],
                    [ApplicationFixtures.Pair("f-a", 3, "env-1")]),
                ApplicationFixtures.Recipe("r-y", "中間品Y", [("i-u", 1)], [("i-y", 1)],
                    [ApplicationFixtures.Pair("f-a", 3, "env-1")]),
            ]);

        // 3.0 機 + 3.0 機 = 6.0 機 → 必要台数 ceil(6.0/4) = 2、上限 ceil(6.0) = 6。
        ProductionPlan plan = Plan(snapshot, new ProductionTarget("i-x", 60), new ProductionTarget("i-y", 60));

        Assert.Equal((2, 6), PlanViewDefaults.DispenserRange(plan, "env-1"));
    }

    // PVD-05: 計画に登場しない環境の範囲は (0, 0)。
    [Fact]
    public void DispenserRangeIsZeroForUnusedEnvironment()
    {
        ProductionPlan plan = Plan(ApplicationFixtures.A02(), new ProductionTarget("i-t", 72));

        Assert.Equal((0, 0), PlanViewDefaults.DispenserRange(plan, "env-none"));
    }

    // PVD-06: カバー不足で稼働が止まった環境でも（必要台数, ceil(利用機械数)）が返る
    // （実績＋有効削減機械数を分母とする、仕様決定 BS）。
    [Fact]
    public void DispenserRangeUsesRequiredAndUsedEvenWhenStopped()
    {
        // A-01 の i-part 60/分は 3 秒ペアで 3.0 機分。散布機 0 へ上書きすると稼働は全停するが、
        // 必要台数は削減機械分込みの自動見積もり（1 台）を返し、上限も利用機械数 3.0 で組まれる。
        MasterDataSnapshot snapshot = ApplicationFixtures.A01();
        ProductionPlan plan = ProductionCalculator.Calculate(
            snapshot,
            [new ProductionTarget("i-part", 60)],
            new ContextFilter(),
            [],
            [new EnvironmentCountOverride("env-gas", 0)],
            []);

        Assert.Equal((1, 3), PlanViewDefaults.DispenserRange(plan, "env-gas"));

        // 機械数がカバー可能台数を超える計画では、必要台数（2）が下限になる。
        ProductionPlan busy = ProductionCalculator.Calculate(
            snapshot,
            [new ProductionTarget("i-part", 160)],
            new ContextFilter(),
            [],
            [],
            []);

        Assert.Equal((2, 8), PlanViewDefaults.DispenserRange(busy, "env-gas"));
    }

    // PVD-07: 上限は利用機械数の実数値の切上げ（利用機械数 4.33・必要台数 2 で (2, 5)）。
    [Fact]
    public void DispenserRangeCeilsFractionalUsedMachines()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [
                ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
                ApplicationFixtures.Item("i-x", "中間品X"),
                ApplicationFixtures.Item("i-gas", "活性ガス", TransportKind.Pipe, gatherable: true),
            ],
            [ApplicationFixtures.Facility("f-a", "機A", 10), ApplicationFixtures.Facility("f-d", "散布機", 5)],
            [ApplicationFixtures.Env("env-1", "環境1", "f-d", "i-gas", 60)],
            [],
            [
                ApplicationFixtures.Recipe("r-x", "中間品X", [("i-u", 1)], [("i-x", 1)],
                    [ApplicationFixtures.Pair("f-a", 4, "env-1")]),
            ]);

        // i-x 65/分 → 4 秒ペアで 65×4/60 ≈ 4.33 機 → 必要台数 ceil(4.33/4) = 2、上限 ceil(4.33) = 5。
        ProductionPlan plan = Plan(snapshot, new ProductionTarget("i-x", 65));

        Assert.Equal((2, 5), PlanViewDefaults.DispenserRange(plan, "env-1"));
    }
}
