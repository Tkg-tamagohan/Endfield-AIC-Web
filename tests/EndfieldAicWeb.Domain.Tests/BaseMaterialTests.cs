using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using static EndfieldAicWeb.Domain.Tests.CalculationFixtures;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>BAS: 基礎素材の指定（docs/phases/test-specification-phase42.md §1）。</summary>
public class BaseMaterialTests
{
    private static ContextFilter Specified(params string[] itemIds) =>
        new() { SpecifiedBaseItemIds = itemIds };

    /// <summary>
    /// BAS-01・02・05・08・10・11 用の多段チェーン。i-top ← i-mid ← i-low ← i-ore（採取）。
    /// i-top は i-ore も直接消費し、i-low の生産は env-gas のペアを使う
    /// （散布機 f-disp は i-gas を 360/分 消費）。i-idle は需要に登場しないアイテム。
    /// </summary>
    private static MasterDataSnapshot ChainWithEnv() => Snapshot(
        [
            Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            Item("i-gas", "採取素材", TransportKind.Pipe, null, true),
            Item("i-low"), Item("i-mid"), Item("i-top"), Item("i-idle"),
        ],
        [
            Facility("f-top", 10.0), Facility("f-mid", 20.0),
            Facility("f-low", 30.0), Facility("f-disp", 5.0),
        ],
        [
            Recipe("r-top", "f-top", 6.0, [("i-mid", 1.0), ("i-ore", 1.0)], [("i-top", 1.0)]),
            Recipe("r-mid", "f-mid", 6.0, [("i-low", 1.0)], [("i-mid", 1.0)]),
            Recipe("r-low", "f-low", 4.0, [("i-ore", 1.0)], [("i-low", 1.0)], environmentId: "env-gas"),
        ],
        [Env("env-gas", "f-disp", "i-gas", 360.0)]);

    // BAS-01: 指定アイテムは外部調達となり上流を展開しない。
    // i-top 10/分 → i-mid 需要 10/分を指定 → 全量が外部調達、r-mid・r-low と上流需要は出ない。
    [Fact]
    public void SpecifiedItemIsExternallyProcuredWithoutUpstream()
    {
        ProductionPlan plan = Run(ChainWithEnv(), [("i-top", 10.0)], Specified("i-mid"));

        ItemRequirement mid = Req(plan, "i-mid");
        Assert.Equal(10.0, mid.RequiredPerMinute, Precision);
        SupplyPortion supply = Assert.Single(mid.Supplies);
        Assert.Equal(SupplyKind.ExternalProcurement, supply.Kind);
        Assert.Equal(10.0, supply.AmountPerMinute, Precision);
        Assert.Equal(0.0, mid.UnmetPerMinute, Precision);

        Assert.Null(RunOf(plan, "r-mid"));
        Assert.Null(RunOf(plan, "r-low"));
        Assert.False(HasReq(plan, "i-low"));
        Assert.False(HasReq(plan, "i-gas"));
        // i-top のもう一方の入力 i-ore は従来どおり採取で賄われる。
        Assert.Equal(10.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
    }

    // BAS-02: 上流の設備・電力・環境が計上されない（BAS-01 と同じ構成）。
    // f-top のみ 10 サイクル×6 秒 = 実数 1.0 → 切上げ 1、電力は f-top の 10 のみ。
    [Fact]
    public void SpecifiedOmitsUpstreamFacilitiesPowerAndEnvironment()
    {
        ProductionPlan plan = Run(ChainWithEnv(), [("i-top", 10.0)], Specified("i-mid"));

        FacilityRequirement top = Assert.Single(plan.FacilityRequirements);
        Assert.Equal("f-top", top.FacilityId);
        Assert.Equal(1.0, top.ExactCount, Precision);
        Assert.Equal(1, top.CeilCount);
        Assert.Equal(10.0, plan.TotalPowerConsumption, Precision);
        Assert.Empty(plan.EnvironmentRequirements);
    }

    /// <summary>
    /// BAS-03・FG-70 用。r-main が i-mid を副産し、r-use が i-mid を消費する。
    /// r-mid は i-mid の通常生産レシピ（指定時は動かない）。
    /// </summary>
    private static MasterDataSnapshot ByproductChain() => Snapshot(
        [
            Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            Item("i-mid"), Item("i-use"), Item("i-main"),
        ],
        [Facility("f-a"), Facility("f-b"), Facility("f-c")],
        [
            Recipe("r-main", "f-a", 4.0, [("i-ore", 1.0)], [("i-main", 1.0), ("i-mid", 1.0)]),
            Recipe("r-mid", "f-b", 6.0, [("i-ore", 2.0)], [("i-mid", 1.0)]),
            Recipe("r-use", "f-c", 6.0, [("i-mid", 4.0)], [("i-use", 1.0)]),
        ]);

    // BAS-03: 副産物充当の残差のみ外部調達。
    // i-mid 需要 40/分（r-use 10 サイクル×4）、r-main の副産物が 10/分賄う → 外部調達は 30/分。
    [Fact]
    public void SpecifiedItemProcuresResidualAfterByproduct()
    {
        ProductionPlan plan = Run(
            ByproductChain(), [("i-use", 10.0), ("i-main", 10.0)], Specified("i-mid"));

        ItemRequirement mid = Req(plan, "i-mid");
        Assert.Equal(40.0, mid.RequiredPerMinute, Precision);
        Assert.Equal(10.0, Supplied(plan, "i-mid", SupplyKind.Byproduct), Precision);
        Assert.Equal(30.0, Supplied(plan, "i-mid", SupplyKind.ExternalProcurement), Precision);
        Assert.Equal(0.0, mid.UnmetPerMinute, Precision);

        Assert.Null(RunOf(plan, "r-mid"));
        // i-ore の需要は r-main の分だけ残る（r-mid・r-use の経路は動かない）。
        Assert.Equal(10.0, Req(plan, "i-ore").RequiredPerMinute, Precision);
    }

    /// <summary>BAS-04 用。i-top が独立した 2 つの中間素材（i-m1・i-m2）を消費する。</summary>
    private static MasterDataSnapshot TwoIntermediates() => Snapshot(
        [
            Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            Item("i-m1"), Item("i-m2"), Item("i-top"),
        ],
        [Facility("f-t"), Facility("f-1"), Facility("f-2")],
        [
            Recipe("r-top", "f-t", 6.0, [("i-m1", 1.0), ("i-m2", 1.0)], [("i-top", 1.0)]),
            Recipe("r-m1", "f-1", 4.0, [("i-ore", 1.0)], [("i-m1", 1.0)]),
            Recipe("r-m2", "f-2", 4.0, [("i-ore", 2.0)], [("i-m2", 1.0)]),
        ]);

    // BAS-04: 複数指定は両方とも外部調達になり、互いの経路は残らない。
    [Fact]
    public void MultipleSpecifiedItemsAreBothProcured()
    {
        ProductionPlan plan = Run(
            TwoIntermediates(), [("i-top", 10.0)], Specified("i-m1", "i-m2"));

        Assert.Equal(10.0, Supplied(plan, "i-m1", SupplyKind.ExternalProcurement), Precision);
        Assert.Equal(10.0, Supplied(plan, "i-m2", SupplyKind.ExternalProcurement), Precision);
        Assert.Null(RunOf(plan, "r-m1"));
        Assert.Null(RunOf(plan, "r-m2"));
        Assert.False(HasReq(plan, "i-ore"));
        Assert.Equal("r-top", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    // BAS-05: 指定なしの回帰。同構成で従来どおりの展開・供給（採取は Gathered のまま）。
    [Fact]
    public void UnspecifiedChainExpandsAsBefore()
    {
        ProductionPlan plan = Run(ChainWithEnv(), [("i-top", 10.0)]);

        Assert.Equal(10.0, Supplied(plan, "i-mid", SupplyKind.Recipe), Precision);
        Assert.Equal(10.0, Supplied(plan, "i-low", SupplyKind.Recipe), Precision);
        Assert.Equal(20.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Equal(360.0, Supplied(plan, "i-gas", SupplyKind.Gathered), Precision);
        Assert.Equal(0.0, Supplied(plan, "i-mid", SupplyKind.ExternalProcurement), Precision);

        Assert.Equal(1.0, Fac(plan, "f-top").ExactCount, Precision);
        Assert.Equal(1.0, Fac(plan, "f-mid").ExactCount, Precision);
        Assert.Equal(10.0 * 4.0 / 60.0, Fac(plan, "f-low").ExactCount, Precision);
        Assert.Equal(1.0, Fac(plan, "f-disp").ExactCount, Precision);
        Assert.Equal(65.0, plan.TotalPowerConsumption, Precision);
        Assert.Single(plan.EnvironmentRequirements);
        Assert.Empty(plan.Warnings);
    }

    // BAS-06: イベント非有効の指定アイテムは外部調達されず未充足のまま（仕様決定 X 優先）。
    [Fact]
    public void InactiveEventItemIsNotProcured()
    {
        MasterDataSnapshot snapshot = Snapshot(
            [
                Item("i-ore", "採取素材", TransportKind.Belt, null, true),
                Item("i-ltd", "部品", TransportKind.Belt, "ev-ltd"),
                Item("i-top"),
            ],
            [Facility("f-asm")],
            [
                Recipe("r-top", "f-asm", 6.0, [("i-ltd", 1.0)], [("i-top", 1.0)]),
                Recipe("r-ltd", "f-asm", 6.0, [("i-ore", 1.0)], [("i-ltd", 1.0)]),
            ],
            gameEvents: [GameEvent("ev-ltd")]);

        ProductionPlan plan = Run(snapshot, [("i-top", 10.0)], Specified("i-ltd"));

        ItemRequirement ltd = Req(plan, "i-ltd");
        Assert.Equal(0.0, Supplied(plan, "i-ltd", SupplyKind.ExternalProcurement), Precision);
        Assert.Equal(10.0, ltd.UnmetPerMinute, Precision);
        Assert.True(HasWarning(plan, WarningCode.EventItemUnavailable));
        Assert.Null(RunOf(plan, "r-ltd"));
    }

    // BAS-07: 指定を介した循環の解消（F-04 の i-a ⇄ i-b で i-b を指定）。
    // i-a 12/分 → r-cyc-a だけが動き、i-b は 12/分を外部調達。循環警告は出ない。
    [Fact]
    public void SpecifiedItemDissolvesCycle()
    {
        ProductionPlan plan = Run(F04(), [("i-a", 12.0)], Specified("i-b"));

        Assert.Equal(12.0, Supplied(plan, "i-b", SupplyKind.ExternalProcurement), Precision);
        Assert.Equal(0.0, Req(plan, "i-a").UnmetPerMinute, Precision);
        Assert.Equal(0.0, Req(plan, "i-b").UnmetPerMinute, Precision);
        Assert.False(HasWarning(plan, WarningCode.CycleDetected));
        Assert.Equal("r-cyc-a", Assert.Single(plan.RecipeRuns).RecipeId);
    }

    // BAS-08: 指定アイテムへのペア上書きは効果なし（ランも警告も出ない）。
    [Fact]
    public void PairOverrideOnSpecifiedItemIsIgnored()
    {
        ProductionPlan plan = Run(
            ChainWithEnv(),
            [("i-top", 10.0)],
            Specified("i-mid"),
            overrides: [Override("i-mid", "r-mid", "f-mid", 6.0)]);

        Assert.Equal(10.0, Supplied(plan, "i-mid", SupplyKind.ExternalProcurement), Precision);
        Assert.Null(RunOf(plan, "r-mid"));
        PairSelection selection = Assert.Single(plan.PairSelections);
        Assert.Equal("i-top", selection.ItemId);
        Assert.Empty(plan.Warnings);
    }

    /// <summary>
    /// BAS-09 用。i-mid は r-top の固定消費（30/分×台数）と env-burn の環境消費（60/分×散布機）の対象。
    /// </summary>
    private static MasterDataSnapshot FixedAndEnvConsumption() => Snapshot(
        [
            Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            Item("i-mid"), Item("i-top"), Item("i-envp"),
        ],
        [Facility("f-t"), Facility("f-e"), Facility("f-mid"), Facility("f-disp")],
        [
            Recipe("r-top", [Pair("r-top", "f-t", 30.0, null, ("i-mid", 30.0))],
                [("i-ore", 1.0)], [("i-top", 1.0)]),
            Recipe("r-envp", "f-e", 6.0, [("i-ore", 1.0)], [("i-envp", 1.0)], environmentId: "env-burn"),
            Recipe("r-mid", "f-mid", 4.0, [("i-ore", 2.0)], [("i-mid", 1.0)]),
        ],
        [Env("env-burn", "f-disp", "i-mid", 60.0)]);

    // BAS-09: 固定消費・環境消費由来の需要も外部調達になる。
    // r-top 10 サイクル×30 秒 = 実数 5.0 → 切上げ 5 台で固定消費 150/分、
    // r-envp 10 サイクル×6 秒 = 1.0 機 → 散布機 1 台で環境消費 60/分。合計 210/分。
    [Fact]
    public void FixedAndEnvironmentDemandIsProcured()
    {
        ProductionPlan plan = Run(
            FixedAndEnvConsumption(), [("i-top", 10.0), ("i-envp", 10.0)], Specified("i-mid"));

        Assert.Equal(210.0, Supplied(plan, "i-mid", SupplyKind.ExternalProcurement), Precision);
        Assert.Equal(0.0, Req(plan, "i-mid").UnmetPerMinute, Precision);
        Assert.Null(RunOf(plan, "r-mid"));
    }

    // BAS-10: 目標の直接入力を指定しても目標側のランは残り、設備台数は目標ラン分のみ。
    [Fact]
    public void SpecifiedDirectInputKeepsTargetRun()
    {
        ProductionPlan plan = Run(ChainWithEnv(), [("i-top", 10.0)], Specified("i-mid"));

        RecipeRun top = Assert.Single(plan.RecipeRuns);
        Assert.Equal("r-top", top.RecipeId);
        Assert.Equal(10.0, top.CyclesPerMinute, Precision);
        Assert.Equal(1.0, Fac(plan, "f-top").ExactCount, Precision);
        Assert.Equal(10.0, Supplied(plan, "i-mid", SupplyKind.ExternalProcurement), Precision);
    }

    // BAS-11: 計画に登場しない指定は効果なし（計画は指定なしと同一、例外・警告なし）。
    [Fact]
    public void SpecificationOutsidePlanHasNoEffect()
    {
        ProductionPlan plan = Run(ChainWithEnv(), [("i-top", 10.0)], Specified("i-idle"));

        Assert.False(HasReq(plan, "i-idle"));
        Assert.Equal(10.0, Supplied(plan, "i-mid", SupplyKind.Recipe), Precision);
        Assert.Equal(20.0, Supplied(plan, "i-ore", SupplyKind.Gathered), Precision);
        Assert.Equal(4, plan.FacilityRequirements.Count);
        Assert.Empty(plan.Warnings);
    }

    /// <summary>BAS-12 用。r-main が i-mid を副産し、出力なしの処理レシピ r-disp が i-mid を処理する。</summary>
    private static MasterDataSnapshot DisposalChain() => Snapshot(
        [
            Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            Item("i-mid"), Item("i-main"),
        ],
        [Facility("f-a"), Facility("f-trt")],
        [
            Recipe("r-main", "f-a", 4.0, [("i-ore", 1.0)], [("i-main", 1.0), ("i-mid", 2.0)]),
            Recipe("r-disp", "f-trt", 5.0, [("i-mid", 1.0)], []),
        ]);

    // BAS-12: 指定アイテムの余剰は処理対象になる（供給側の省略と消費側の処理は独立）。
    // i-main 10/分 → r-main 10 サイクルで i-mid を 20/分副産。余剰 20/分は r-disp が 20 サイクルで消費し、
    // 処理需要は副産物で全量賄われるため外部調達は計上されない。
    [Fact]
    public void SpecifiedItemSurplusIsDisposed()
    {
        ProductionPlan plan = Run(DisposalChain(), [("i-main", 10.0)], Specified("i-mid"));

        RecipeRun disp = Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-disp");
        Assert.Equal(20.0, disp.CyclesPerMinute, Precision);
        ItemRequirement mid = Req(plan, "i-mid");
        Assert.Equal(20.0, mid.RequiredPerMinute, Precision);
        Assert.Equal(20.0, Supplied(plan, "i-mid", SupplyKind.Byproduct), Precision);
        Assert.Equal(0.0, Supplied(plan, "i-mid", SupplyKind.ExternalProcurement), Precision);
        Assert.Equal(0.0, mid.UnmetPerMinute, Precision);
        Assert.DoesNotContain(plan.Surpluses, s => s.ItemId == "i-mid");
    }
}
