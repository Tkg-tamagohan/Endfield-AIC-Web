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

    // PVD-03: 散布機台数の上限はその環境を使う稼働中レシピ数（仕様決定 I）。
    [Fact]
    public void DispenserLimitCountsActiveRecipes()
    {
        // A-01・既定は 3 秒 env-gas ペア → env-gas を使うレシピは r-part の 1 件。
        ProductionPlan plan = Plan(ApplicationFixtures.A01(), new ProductionTarget("i-part", 60));

        Assert.Equal(1, PlanViewDefaults.DispenserLimit(plan, "env-gas"));
    }

    // PVD-04: 同一環境を使うレシピが複数あればその数が上限になる。
    [Fact]
    public void DispenserLimitCountsMultipleRecipes()
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

        ProductionPlan plan = Plan(snapshot, new ProductionTarget("i-x", 60), new ProductionTarget("i-y", 60));

        Assert.Equal(2, PlanViewDefaults.DispenserLimit(plan, "env-1"));
    }

    // PVD-05: 計画に登場しない環境の上限は 0。
    [Fact]
    public void DispenserLimitIsZeroForUnusedEnvironment()
    {
        ProductionPlan plan = Plan(ApplicationFixtures.A02(), new ProductionTarget("i-t", 72));

        Assert.Equal(0, PlanViewDefaults.DispenserLimit(plan, "env-none"));
    }

    // PVD-06: 上限は環境要件の RequiredDispenserCount（機械数からの見積もり）を返し、
    // カバー不足で稼働が止まった環境でも自動値が返る（distinct レシピ数ではない）。
    [Fact]
    public void DispenserLimitReturnsRequiredCountEvenWhenStopped()
    {
        // A-01 の i-part 60/分は 3 秒ペアで 3.0 機分。散布機 0 へ上書きすると稼働は全停するが、
        // 必要台数は削減機械分込みの自動見積もり（1 台）を返す。
        MasterDataSnapshot snapshot = ApplicationFixtures.A01();
        ProductionPlan plan = ProductionCalculator.Calculate(
            snapshot,
            [new ProductionTarget("i-part", 60)],
            new ContextFilter(),
            [],
            [new EnvironmentCountOverride("env-gas", 0)],
            []);

        Assert.Equal(1, PlanViewDefaults.DispenserLimit(plan, "env-gas"));

        // 機械数がカバー可能台数を超える計画では、レシピ数（1）でなく必要台数（2）が上限になる。
        ProductionPlan busy = ProductionCalculator.Calculate(
            snapshot,
            [new ProductionTarget("i-part", 160)],
            new ContextFilter(),
            [],
            [],
            []);

        Assert.Equal(2, PlanViewDefaults.DispenserLimit(busy, "env-gas"));
    }
}
