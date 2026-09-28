using EndfieldAicWeb.Domain.Calculation;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>SEL: レシピとペアの選択（仕様決定 F/U、test-specification-phase2 §3）。</summary>
public class SelectionTests
{
    [Fact(DisplayName = "SEL-01: 既定は VersionAdded 最新のレシピ")]
    public void NewestRecipeIsDefault()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F03(), [("i-x", 60.0)]);

        RecipeRun run = Assert.Single(plan.RecipeRuns);
        Assert.Equal("r-x-new", run.RecipeId);
        Assert.Equal("f-b", run.FacilityId);
        Assert.Equal(30.0, run.CyclesPerMinute, Precision);
        Assert.Equal(1.5, Fac(plan, "f-b").ExactCount, Precision);
        Assert.Equal(2, Fac(plan, "f-b").CeilCount);
    }

    [Fact(DisplayName = "SEL-02: イベント非有効レシピは候補外")]
    public void EventGatedRecipeIsSkippedUnlessActive()
    {
        ProductionPlan inactive = CalculationFixtures.Run(
            CalculationFixtures.F03(), [("i-x", 60.0)]);
        Assert.Equal("r-x-new", Assert.Single(inactive.RecipeRuns).RecipeId);

        ProductionPlan active = CalculationFixtures.Run(
            CalculationFixtures.F03(), [("i-x", 60.0)],
            context: CalculationFixtures.Context("ev-limited"));
        Assert.Equal("r-x-ltd", Assert.Single(active.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-03: 同 VersionAdded は Id 昇順")]
    public void SameVersionFallsBackToIdAscending()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F03(), [("i-y", 60.0)]);
        Assert.Equal("r-y-a", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-04: パース不能な VersionAdded は最古扱い＋警告")]
    public void UnparseableVersionIsOldestWithWarning()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F03(), [("i-z", 60.0)]);

        Assert.True(HasWarning(plan, WarningCode.InvalidVersionString));
        Assert.Equal("r-z", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-05: ペア既定は CycleTime 最小")]
    public void DefaultPairIsMinCycleTime()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F03(), [("i-x", 60.0)]);

        RecipeRun run = Assert.Single(plan.RecipeRuns);
        Assert.Equal("r-x-new", run.RecipeId);
        Assert.Equal("f-b", run.FacilityId);
    }

    [Fact(DisplayName = "SEL-06: 同 CycleTime は EnvironmentId=null → FixedConsumption なし/小")]
    public void TieBreakPrefersNoEnvironmentNoFixed()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F03(), [("i-w", 60.0)]);

        RecipeRun run = Assert.Single(plan.RecipeRuns);
        Assert.Equal("r-w", run.RecipeId);
        Assert.Equal("f-c", run.FacilityId);
        Assert.Empty(plan.EnvironmentRequirements);
        Assert.False(HasReq(plan, "i-fuel-w"));
        Assert.False(HasReq(plan, "i-gas-w"));
    }

    [Fact(DisplayName = "SEL-07: ペア上書きが適用される")]
    public void PairOverrideApplies()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F03(), [("i-x", 60.0)],
            overrides: [CalculationFixtures.Override("i-x", "r-x-new", "f-a", 6.0)]);

        RecipeRun run = Assert.Single(plan.RecipeRuns);
        Assert.Equal("r-x-new", run.RecipeId);
        Assert.Equal("f-a", run.FacilityId);
        Assert.False(HasFac(plan, "f-b"));
        Assert.False(HasWarning(plan, WarningCode.InvalidPairOverride));
    }

    [Fact(DisplayName = "SEL-08: 不適格なペア上書きは警告＋既定")]
    public void IneligiblePairOverrideWarnsAndFallsBack()
    {
        // 存在しないペア行
        ProductionPlan missing = CalculationFixtures.Run(
            CalculationFixtures.F03(), [("i-x", 60.0)],
            overrides: [CalculationFixtures.Override("i-x", "r-x-new", "f-zz", 3.0)]);
        Assert.True(HasWarning(missing, WarningCode.InvalidPairOverride));
        Assert.Equal("f-b", Assert.Single(missing.RecipeRuns).FacilityId);

        // イベント非有効レシピのペア
        ProductionPlan eventGated = CalculationFixtures.Run(
            CalculationFixtures.F03(), [("i-x", 60.0)],
            overrides: [CalculationFixtures.Override("i-x", "r-x-ltd", "f-a", 6.0)]);
        Assert.True(HasWarning(eventGated, WarningCode.InvalidPairOverride));
        Assert.Equal("f-b", Assert.Single(eventGated.RecipeRuns).FacilityId);

        // 環境が不適格なペア（r-v-new の env-ltd ペア、ev-off 無効）
        ProductionPlan envGated = CalculationFixtures.Run(
            CalculationFixtures.F03(), [("i-v", 60.0)],
            overrides: [CalculationFixtures.Override("i-v", "r-v-new", "f-a", 3.0, "env-ltd")]);
        Assert.True(HasWarning(envGated, WarningCode.InvalidPairOverride));
        Assert.Equal("r-v-old", Assert.Single(envGated.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-09: 最新レシピの全ペアが環境不適格なら次点レシピへ")]
    public void AllPairsIneligibleFallsBackToNextRecipe()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F03(), [("i-v", 60.0)]);

        Assert.Equal("r-v-old", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-10: 同一レシピの別ペア衝突は先勝ち＋警告")]
    public void ConflictingPairsFirstWinsWithWarning()
    {
        // 同一レシピが i-m・i-n を出力し、各需要が別ペアを指す上書き。
        MasterDataSnapshot master = CalculationFixtures.Snapshot(
            [
                CalculationFixtures.Item("i-m"),
                CalculationFixtures.Item("i-n"),
                CalculationFixtures.Item("i-ore", "基礎素材"),
            ],
            [
                CalculationFixtures.Facility("f-a"),
                CalculationFixtures.Facility("f-b"),
            ],
            [
                CalculationFixtures.Recipe("r-mn", [
                        CalculationFixtures.Pair("r-mn", "f-a", 4.0),
                        CalculationFixtures.Pair("r-mn", "f-b", 6.0),
                    ],
                    [("i-ore", 1.0)],
                    [("i-m", 1.0), ("i-n", 1.0)]),
            ]);

        ProductionPlan plan = CalculationFixtures.Run(
            master,
            [("i-m", 10.0), ("i-n", 20.0)],
            overrides:
            [
                CalculationFixtures.Override("i-m", "r-mn", "f-a", 4.0),
                CalculationFixtures.Override("i-n", "r-mn", "f-b", 6.0),
            ]);

        Assert.True(HasWarning(plan, WarningCode.PairConflict));
        RecipeRun run = Assert.Single(plan.RecipeRuns);
        Assert.Equal("r-mn", run.RecipeId);
        Assert.Equal("f-a", run.FacilityId);
        Assert.Equal(20.0, run.CyclesPerMinute, Precision);
        Assert.Equal(0.0, Req(plan, "i-n").UnmetPerMinute, Precision);
    }

    [Fact(DisplayName = "SEL-11: 引き戻しで休眠したペアの再稼働も稼働中ペアへ正規化")]
    public void RevivedDormantSelectionFollowsRunningPair()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F14(),
            [("i-y", 10.0), ("i-m", 100.0), ("i-w", 10.0)],
            overrides: [CalculationFixtures.Override("i-z", "r-yz", "f-b", 8.0, "env-y")]);

        Assert.True(HasWarning(plan, WarningCode.PairConflict));
        RecipeRun run = Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-yz");
        Assert.Equal("f-b", run.FacilityId);
        Assert.False(HasWarning(plan, WarningCode.ConvergenceNotReached));
    }
}
