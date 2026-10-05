using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>SEL: レシピとペアの選択（仕様決定 F/U/BA、docs/phases/test-specification-phase2.md §3・phase21 §2）。</summary>
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

    // 改訂（BT）: 旧規則「EnvironmentId=null → FixedConsumption なし/小」の順序検査から、
    // 「固定消費の有無・量が同じ同率では EnvironmentId=null が残る」維持検査へ。検査値は変わらない。
    [Fact(DisplayName = "SEL-06: 同 CycleTime で固定消費の有無・量が同じなら EnvironmentId=null を優先（BT 改定後も維持）")]
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
                CalculationFixtures.Item("i-ore", "採取素材", TransportKind.Belt, null, true),
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

    [Fact(DisplayName = "SEL-12: prerelease 版も semver で順序付ける")]
    public void PrereleaseVersionsAreOrderedBySemver()
    {
        MasterDataSnapshot betaOnly = CalculationFixtures.Snapshot(
            [
                CalculationFixtures.Item("i-x"),
                CalculationFixtures.Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            ],
            [CalculationFixtures.Facility("f-a")],
            [
                CalculationFixtures.Recipe("r-old", "f-a", 6.0,
                    [("i-ore", 1.0)], [("i-x", 1.0)], "1.1.0"),
                CalculationFixtures.Recipe("r-beta", "f-a", 6.0,
                    [("i-ore", 1.0)], [("i-x", 1.0)], "1.2.0-beta"),
            ]);

        ProductionPlan beta = CalculationFixtures.Run(betaOnly, [("i-x", 60.0)]);
        Assert.Equal("r-beta", Assert.Single(beta.RecipeRuns).RecipeId);

        MasterDataSnapshot withRelease = CalculationFixtures.Snapshot(
            [
                CalculationFixtures.Item("i-x"),
                CalculationFixtures.Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            ],
            [CalculationFixtures.Facility("f-a")],
            [
                CalculationFixtures.Recipe("r-beta", "f-a", 6.0,
                    [("i-ore", 1.0)], [("i-x", 1.0)], "1.2.0-beta"),
                CalculationFixtures.Recipe("r-rel", "f-a", 6.0,
                    [("i-ore", 1.0)], [("i-x", 1.0)], "1.2.0"),
            ]);

        ProductionPlan rel = CalculationFixtures.Run(withRelease, [("i-x", 60.0)]);
        Assert.Equal("r-rel", Assert.Single(rel.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-13: パース不能な VersionAdded は有効な全バージョンより最古")]
    public void InvalidVersionIsBelowEveryValidVersion()
    {
        MasterDataSnapshot master = CalculationFixtures.Snapshot(
            [
                CalculationFixtures.Item("i-x"),
                CalculationFixtures.Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            ],
            [CalculationFixtures.Facility("f-a")],
            [
                CalculationFixtures.Recipe("r-huge", "f-a", 6.0,
                    [("i-ore", 1.0)], [("i-x", 1.0)], "2147483648.0.0"),
                CalculationFixtures.Recipe("r-alpha", "f-a", 6.0,
                    [("i-ore", 1.0)], [("i-x", 1.0)], "0.0.0-alpha"),
            ]);

        ProductionPlan plan = CalculationFixtures.Run(master, [("i-x", 60.0)]);

        Assert.Equal("r-alpha", Assert.Single(plan.RecipeRuns).RecipeId);
        Assert.True(HasWarning(plan, WarningCode.InvalidVersionString));
    }

    [Fact(DisplayName = "SEL-14: 同 VersionAdded で出力量が異なる場合、高レート側が既定（BA）")]
    public void HigherOutputRateWinsOnVersionTie()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F18(), [("i-q1", 60.0)]);

        Assert.Equal("r-q1-rich", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-15: 同出力量で CycleTime が異なる場合、短サイクル側が既定（BA）")]
    public void ShorterCycleWinsOnVersionTie()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F18(), [("i-q2", 60.0)]);

        Assert.Equal("r-q2-b-fast", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-16: 同 VersionAdded・同実効レートは Id 昇順（BA）")]
    public void SameRateFallsBackToIdAscending()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F18(), [("i-q3", 60.0)]);

        Assert.Equal("r-q3-a", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-17: 複数ペアのレシピは最小 CycleTime ペアのレートで比較される（BA）")]
    public void MultiPairRecipeUsesBestPairRate()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F18(), [("i-q4", 60.0)]);

        RecipeRun run = Assert.Single(plan.RecipeRuns);
        Assert.Equal("r-q4-multi", run.RecipeId);
        Assert.Equal("f-b", run.FacilityId);
    }

    [Fact(DisplayName = "SEL-18: 最速ペアが環境不適格なら次点ペアのレートで比較される（BA）")]
    public void IneligibleFastestPairUsesNextPairRate()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F18(), [("i-q5", 60.0)]);

        Assert.Equal("r-q5-alt", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-19: 適格ペア 0 件のレシピは実効レート 0 で最下位（BA）")]
    public void NoEligiblePairIsLowestRate()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F18(), [("i-q6", 60.0)]);

        Assert.Equal("r-q6-slow", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-20: VersionAdded は第一キーのまま（低レートの新版が優先）（BA）")]
    public void VersionRemainsPrimaryKey()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F18(), [("i-q7", 60.0)]);

        Assert.Equal("r-q7-new", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-21: ListCandidates の候補順と IsDefault が新規則と一致（BA）")]
    public void CandidateListFollowsSameOrdering()
    {
        var candidates = PairSelector.ListCandidates(
            "i-q1", CalculationFixtures.F18(), new ContextFilter());

        Assert.Equal("r-q1-rich", candidates[0].Recipe.Id);
        Assert.True(candidates[0].IsDefault);
        Assert.Equal("r-q1-lean", candidates[1].Recipe.Id);
        Assert.False(candidates[1].IsDefault);
    }

    [Fact(DisplayName = "SEL-22: 副産物としての出力も対象アイテムの出力量でレート計算（BA）")]
    public void ByproductOutputUsesTargetItemQuantity()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F18(), [("i-q8", 60.0)]);

        Assert.Equal("r-q8-b-rich", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-23: 同 CycleTime で「環境あり・FC なし」は「環境なし・FC あり」に勝つ（BT）")]
    public void NoFixedBeatsNoEnvironment()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F03(), [("i-w2", 60.0)]);

        RecipeRun run = Assert.Single(plan.RecipeRuns);
        Assert.Equal("r-w2", run.RecipeId);
        Assert.Equal("f-a", run.FacilityId);
        Assert.Equal("env-w", Assert.Single(plan.EnvironmentRequirements).EnvironmentId);
        Assert.True(HasReq(plan, "i-gas-w"));
        Assert.False(HasReq(plan, "i-fuel-w"));
    }

    [Fact(DisplayName = "SEL-24: 両方 FC ありでは RatePerMinute 小さい方が EnvironmentId に先立つ（BT）")]
    public void SmallerFixedBeatsNoEnvironment()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F03(), [("i-w3", 60.0)]);

        RecipeRun run = Assert.Single(plan.RecipeRuns);
        Assert.Equal("r-w3", run.RecipeId);
        Assert.Equal("f-a", run.FacilityId);
        Assert.False(HasFac(plan, "f-b"));
        Assert.Equal("env-w", Assert.Single(plan.EnvironmentRequirements).EnvironmentId);
        // f-a ペアは FC i-fuel-w 10/分を持つため燃料需要も出る。
        Assert.True(HasReq(plan, "i-fuel-w"));
    }

    [Fact(DisplayName = "SEL-25: ListCandidates の候補順と IsDefault が新規則と一致（BT）")]
    public void CandidateListFollowsNewTieBreak()
    {
        var candidates = PairSelector.ListCandidates(
            "i-w2", CalculationFixtures.F03(), new ContextFilter());

        Assert.Equal(3, candidates.Count);
        Assert.Equal("f-a", candidates[0].Pair.FacilityId);
        Assert.Equal("env-w", candidates[0].Pair.EnvironmentId);
        Assert.True(candidates[0].IsDefault);
        Assert.Equal("f-b", candidates[1].Pair.FacilityId);
        Assert.False(candidates[1].IsDefault);
        Assert.Equal("f-c", candidates[2].Pair.FacilityId);
        Assert.False(candidates[2].IsDefault);
    }

    [Fact(DisplayName = "SEL-26: 同 VersionAdded・同出力レートで入力合計が小さい方が既定（CL・CM）")]
    public void SmallerInputRateWinsOnVersionAndOutputTie()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F19(), [("i-in1", 60.0)]);

        Assert.Equal("r-in1-lean", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-27: 入力が多くても出力レートの高い方が既定（CM・出力レートが入力より先）")]
    public void HigherOutputRateBeatsSmallerInputRate()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F19(), [("i-in2", 60.0)]);

        Assert.Equal("r-in2-rich", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-28: 入力レート同率では出力レートが高い方が既定（CM・出力レートは第 2 キー）")]
    public void HigherOutputRateWinsOnInputRateTie()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F19(), [("i-in3", 60.0)]);

        Assert.Equal("r-in3-high", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-29: 入力が少なくても旧版は新版に負ける（CL・VersionAdded 第一キーの維持）")]
    public void VersionAddedStillBeatsSmallerInputs()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F19(), [("i-in4", 60.0)]);

        Assert.Equal("r-in4-new", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-30: 入力レートは分換算で比較される（CL・サイクル合計の多い方が勝ちうる）")]
    public void InputRateIsComparedPerMinute()
    {
        // 出力レートは同率（30/分）。1 サイクル合計は bulk 4 個 > few 3 個だが、
        // 分換算では bulk 60/分 < few 90/分で bulk が既定。
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F19(), [("i-in5", 60.0)]);

        Assert.Equal("r-in5-bulk", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-31: FixedConsumption は入力レートに数えず全キー同率で Id 昇順（CL）")]
    public void FixedConsumptionIsNotCountedInInputRate()
    {
        // 入力同量・出力同量で FC を数える実装なら z-fc が負けずに残るが、正しくは全キー同率で
        // Id 昇順まで流れて a-plain が既定になる。
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F19(), [("i-in6", 60.0)]);

        Assert.Equal("r-in6-a-plain", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-32: 適格ペア 0 件のレシピは選ばれない（CL・回帰）")]
    public void NoEligiblePairRecipeIsNotSelected()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F19(), [("i-in7", 60.0)]);

        Assert.Equal("r-in7-live", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    [Fact(DisplayName = "SEL-33: ListCandidates の候補順と IsDefault が入力レート順に一致（CL）")]
    public void CandidateListFollowsInputRateOrdering()
    {
        var candidates = PairSelector.ListCandidates(
            "i-in1", CalculationFixtures.F19(), new ContextFilter());

        Assert.Equal(2, candidates.Count);
        Assert.Equal("r-in1-lean", candidates[0].Recipe.Id);
        Assert.True(candidates[0].IsDefault);
        Assert.Equal("r-in1-rich", candidates[1].Recipe.Id);
        Assert.False(candidates[1].IsDefault);
    }
}
