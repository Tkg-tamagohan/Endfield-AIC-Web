using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using F = EndfieldAicWeb.Domain.Tests.CalculationFixtures;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>DSP: 処理レシピと余剰の廃棄処理（docs/phases/test-specification-phase32.md §1）。</summary>
public class DisposalTests
{
    /// <summary>
    /// D-01: 副産物として i-sew が余剰になる最小構成。i-p 30/分 → r-m 30 サイクルで
    /// i-sew 30/分が余り、処理レシピ r-disp（f-trt 4 秒・i-sew 1/サイクル）が全量を処理する。
    /// </summary>
    private static MasterDataSnapshot D01() => F.Snapshot(
        [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true), F.Item("i-p"), F.Item("i-sew")],
        [F.Facility("f-asm", 50.0), F.Facility("f-trt", 40.0)],
        [
            F.Recipe("r-m", "f-asm", 4.0, [("i-ore", 1.0)], [("i-p", 1.0), ("i-sew", 1.0)]),
            F.Recipe("r-disp", "f-trt", 4.0, [("i-sew", 1.0)], []),
        ]);

    [Fact(DisplayName = "DSP-01: 余剰の処理ランが登録され収束する")]
    public void SurplusIsDisposedByRun()
    {
        ProductionPlan plan = F.Run(D01(), [("i-p", 30.0)]);

        // i-sew 余剰 30/分 → r-disp 30 サイクル/分（1 サイクル 1 個）。
        RecipeRun run = Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-disp");
        Assert.Equal(30.0, run.CyclesPerMinute, 6);
        Assert.Equal("f-trt", run.FacilityId);

        ItemRequirement req = Assert.Single(plan.ItemRequirements, r => r.ItemId == "i-sew");
        Assert.Equal(30.0, req.RequiredPerMinute, 6);
        Assert.Equal(0.0, req.UnmetPerMinute, 6);
        Assert.DoesNotContain(plan.Surpluses, s => s.ItemId == "i-sew");
        Assert.DoesNotContain(plan.Warnings, w => w.Code == WarningCode.ConvergenceNotReached);
    }

    [Fact(DisplayName = "DSP-02: 再利用充当後の残りだけ処理される")]
    public void ReuseTakesPrecedenceOverDisposal()
    {
        // i-sew を 1 個/サイクルで再利用する r-q に i-q 10/分の需要がある。
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true),
             F.Item("i-p"), F.Item("i-sew"), F.Item("i-q")],
            [F.Facility("f-asm"), F.Facility("f-q"), F.Facility("f-trt")],
            [
                F.Recipe("r-m", "f-asm", 4.0, [("i-ore", 1.0)], [("i-p", 1.0), ("i-sew", 1.0)]),
                F.Recipe("r-q", "f-q", 6.0, [("i-sew", 1.0)], [("i-q", 1.0)]),
                F.Recipe("r-disp", "f-trt", 4.0, [("i-sew", 1.0)], []),
            ]);

        ProductionPlan plan = F.Run(master, [("i-p", 30.0), ("i-q", 10.0)]);

        // 生産 30/分のうち再利用 10/分が先に充当され、残り 20/分だけ処理される。
        RecipeRun run = Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-disp");
        Assert.Equal(20.0, run.CyclesPerMinute, 6);
        Assert.DoesNotContain(plan.Surpluses, s => s.ItemId == "i-sew");
    }

    [Fact(DisplayName = "DSP-03: 計算目標のアイテムは処理対象外")]
    public void TargetItemIsNotDisposed()
    {
        ProductionPlan plan = F.Run(D01(), [("i-p", 30.0), ("i-sew", 10.0)]);

        Assert.DoesNotContain(plan.RecipeRuns, r => r.RecipeId == "r-disp");
        SurplusProduction surplus = Assert.Single(plan.Surpluses, s => s.ItemId == "i-sew");
        Assert.Equal(20.0, surplus.ExcessPerMinute, 6);
    }

    [Fact(DisplayName = "DSP-04: 処理対象でない余剰はそのまま残る")]
    public void NonDisposalSurplusStays()
    {
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true),
             F.Item("i-p"), F.Item("i-sew"), F.Item("i-misc")],
            [F.Facility("f-asm"), F.Facility("f-trt")],
            [
                F.Recipe("r-m", "f-asm", 4.0, [("i-ore", 1.0)],
                    [("i-p", 1.0), ("i-sew", 1.0), ("i-misc", 1.0)]),
                F.Recipe("r-disp", "f-trt", 4.0, [("i-sew", 1.0)], []),
            ]);

        ProductionPlan plan = F.Run(master, [("i-p", 30.0)]);

        Assert.DoesNotContain(plan.Surpluses, s => s.ItemId == "i-sew");
        SurplusProduction surplus = Assert.Single(plan.Surpluses, s => s.ItemId == "i-misc");
        Assert.Equal(30.0, surplus.ExcessPerMinute, 6);
    }

    [Fact(DisplayName = "DSP-05: 適格な処理レシピがない場合は余剰のまま残る")]
    public void NoEligibleDisposalRecipeLeavesSurplus()
    {
        // 処理レシピが非有効イベント所属で使えない構成。
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true), F.Item("i-p"), F.Item("i-sew")],
            [F.Facility("f-asm"), F.Facility("f-trt")],
            [
                F.Recipe("r-m", "f-asm", 4.0, [("i-ore", 1.0)], [("i-p", 1.0), ("i-sew", 1.0)]),
                F.Recipe("r-disp", "f-trt", 4.0, [("i-sew", 1.0)], [], gameEventId: "ev-off"),
            ],
            gameEvents: [F.GameEvent("ev-off")]);

        ProductionPlan plan = F.Run(master, [("i-p", 30.0)]);

        Assert.DoesNotContain(plan.RecipeRuns, r => r.RecipeId == "r-disp");
        Assert.Single(plan.Surpluses, s => s.ItemId == "i-sew");
        Assert.DoesNotContain(plan.Warnings, w => w.Code == WarningCode.ConvergenceNotReached);
    }

    [Fact(DisplayName = "DSP-06: 処理ランの設備台数・消費電力が計上される")]
    public void DisposalCountsIntoFacilitiesAndPower()
    {
        ProductionPlan plan = F.Run(D01(), [("i-p", 30.0)]);

        // r-disp 30 サイクル/分 × 4 秒 = 実数 2.0 台 → 切上げ 2 台。
        FacilityRequirement req = Assert.Single(plan.FacilityRequirements, f => f.FacilityId == "f-trt");
        Assert.Equal(2.0, req.ExactCount, 6);
        Assert.Equal(2, req.CeilCount);

        // f-asm 2 台 × 50 + f-trt 2 台 × 40 = 180。
        Assert.Equal(180.0, plan.TotalPowerConsumption, 6);
    }

    [Fact(DisplayName = "DSP-07: 処理ランの固定消費が追加需要になる")]
    public void DisposalFixedConsumptionIsAdded()
    {
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true),
             F.Item("i-fuel", "採取素材", TransportKind.Belt, null, true),
             F.Item("i-p"), F.Item("i-sew")],
            [F.Facility("f-asm"), F.Facility("f-trt")],
            [
                F.Recipe("r-m", "f-asm", 4.0, [("i-ore", 1.0)], [("i-p", 1.0), ("i-sew", 1.0)]),
                F.Recipe("r-disp", [F.Pair("r-disp", "f-trt", 4.0, null, ("i-fuel", 6.0))],
                    [("i-sew", 1.0)], []),
            ]);

        ProductionPlan plan = F.Run(master, [("i-p", 30.0)]);

        // 処理設備 切上げ 2 台 × 6/分 = 12/分が i-fuel の需要に計上される。
        ItemRequirement fuel = Assert.Single(plan.ItemRequirements, r => r.ItemId == "i-fuel");
        Assert.Equal(12.0, fuel.RequiredPerMinute, 6);
    }

    [Fact(DisplayName = "DSP-08: 環境を要するペアの処理ラン")]
    public void DisposalRunUsesEnvironment()
    {
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true),
             F.Item("i-gas", "採取素材", TransportKind.Pipe, null, true),
             F.Item("i-p"), F.Item("i-sew")],
            [F.Facility("f-asm"), F.Facility("f-trt"), F.Facility("f-disp")],
            [
                F.Recipe("r-m", "f-asm", 4.0, [("i-ore", 1.0)], [("i-p", 1.0), ("i-sew", 1.0)]),
                F.Recipe("r-disp", [F.Pair("r-disp", "f-trt", 4.0, "env-gas")],
                    [("i-sew", 1.0)], []),
            ],
            [F.Env("env-gas", "f-disp", "i-gas", 360.0)]);

        ProductionPlan plan = F.Run(master, [("i-p", 30.0)]);

        // 処理ランの機械数 2.0 が環境の利用機械数に計上され、散布機 1 台・ガス 360/分。
        EnvironmentRequirement env =
            Assert.Single(plan.EnvironmentRequirements, e => e.EnvironmentId == "env-gas");
        Assert.Equal(2.0, env.UsedMachineCount, 6);
        Assert.Equal(1, env.DispenserCount);
        Assert.Equal(360.0, env.ConsumeRatePerMinuteTotal, 6);
        Assert.Equal("f-disp", Assert.Single(plan.FacilityRequirements, f => f.FacilityId == "f-disp").FacilityId);
    }

    [Fact(DisplayName = "DSP-09: 複数入力の処理レシピ")]
    public void MultiInputDisposalConsumesAuxInput()
    {
        // i-aux は余剰を持たず補助入力。その消費は通常の需要として残る。
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true),
             F.Item("i-p"), F.Item("i-sew"), F.Item("i-aux")],
            [F.Facility("f-asm"), F.Facility("f-aux"), F.Facility("f-trt")],
            [
                F.Recipe("r-m", "f-asm", 4.0, [("i-ore", 1.0)], [("i-p", 1.0), ("i-sew", 1.0)]),
                F.Recipe("r-auxp", "f-aux", 4.0, [("i-ore", 1.0)], [("i-aux", 1.0)]),
                F.Recipe("r-disp", "f-trt", 4.0, [("i-sew", 1.0), ("i-aux", 1.0)], []),
            ]);

        ProductionPlan plan = F.Run(master, [("i-p", 30.0)]);

        RecipeRun run = Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-disp");
        Assert.Equal(30.0, run.CyclesPerMinute, 6);

        ItemRequirement aux = Assert.Single(plan.ItemRequirements, r => r.ItemId == "i-aux");
        Assert.Equal(30.0, aux.RequiredPerMinute, 6);
        Assert.Equal(0.0, aux.UnmetPerMinute, 6);
        Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-auxp");
    }

    [Fact(DisplayName = "DSP-10: 処理が誘発する余剰も収束反復で処理される")]
    public void DisposalInducedSurplusIsDisposed()
    {
        // r-disp の固定消費 i-fuel が r-fuel の生産を誘発し、その副産物 i-oily は
        // 別の処理レシピ r-disp2 で処理される。
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true),
             F.Item("i-fuel"), F.Item("i-p"), F.Item("i-sew"), F.Item("i-oily")],
            [F.Facility("f-asm"), F.Facility("f-trt"), F.Facility("f-fuel"), F.Facility("f-trt2")],
            [
                F.Recipe("r-m", "f-asm", 4.0, [("i-ore", 1.0)], [("i-p", 1.0), ("i-sew", 1.0)]),
                F.Recipe("r-disp", [F.Pair("r-disp", "f-trt", 4.0, null, ("i-fuel", 12.0))],
                    [("i-sew", 1.0)], []),
                F.Recipe("r-fuel", "f-fuel", 3.0, [("i-ore", 1.0)], [("i-fuel", 1.0), ("i-oily", 1.0)]),
                F.Recipe("r-disp2", "f-trt2", 3.0, [("i-oily", 1.0)], []),
            ]);

        ProductionPlan plan = F.Run(master, [("i-p", 30.0)]);

        // r-disp 30 サイクル → f-trt 切上げ 2 台 → 固定消費 12×2 = 24/分の i-fuel →
        // r-fuel 24 サイクル → i-oily 24/分が余剰 → r-disp2 が 24 サイクルで処理。
        Assert.Equal(30.0, Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-disp").CyclesPerMinute, 6);
        Assert.Equal(24.0, Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-disp2").CyclesPerMinute, 6);
        Assert.DoesNotContain(plan.Surpluses, s => s.ItemId == "i-sew" || s.ItemId == "i-oily");
        Assert.DoesNotContain(plan.Warnings, w => w.Code == WarningCode.ConvergenceNotReached);
    }

    [Fact(DisplayName = "DSP-11: 処理レシピの既定選択（バージョン最新→実効処理レート）")]
    public void DisposalRecipeSelectionOrder()
    {
        // r-disp-b と r-disp-c はともに 2.0.0 で最新。実効処理レートは
        // b 1/4×60=15 > c 1/8×60=7.5 で b が選ばれる（a は旧版で先に脱落）。
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true), F.Item("i-p"), F.Item("i-sew")],
            [F.Facility("f-asm"), F.Facility("f-trt")],
            [
                F.Recipe("r-m", "f-asm", 4.0, [("i-ore", 1.0)], [("i-p", 1.0), ("i-sew", 1.0)]),
                F.Recipe("r-disp-a", "f-trt", 4.0, [("i-sew", 1.0)], [], versionAdded: "1.0.0"),
                F.Recipe("r-disp-b", "f-trt", 4.0, [("i-sew", 1.0)], [], versionAdded: "2.0.0"),
                F.Recipe("r-disp-c", "f-trt", 8.0, [("i-sew", 1.0)], [], versionAdded: "2.0.0"),
            ]);

        ProductionPlan plan = F.Run(master, [("i-p", 30.0)]);

        Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-disp-b");
        Assert.DoesNotContain(plan.RecipeRuns, r => r.RecipeId == "r-disp-a" || r.RecipeId == "r-disp-c");
    }

    [Fact(DisplayName = "DSP-12: 処理ペアの既定選択（CycleTime 最小）")]
    public void DisposalPairSelectionOrder()
    {
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true), F.Item("i-p"), F.Item("i-sew")],
            [F.Facility("f-asm"), F.Facility("f-a"), F.Facility("f-b")],
            [
                F.Recipe("r-m", "f-asm", 4.0, [("i-ore", 1.0)], [("i-p", 1.0), ("i-sew", 1.0)]),
                F.Recipe("r-disp", [F.Pair("r-disp", "f-a", 8.0), F.Pair("r-disp", "f-b", 2.0)],
                    [("i-sew", 1.0)], []),
            ]);

        ProductionPlan plan = F.Run(master, [("i-p", 30.0)]);

        // CycleTime 最小の f-b 2 秒ペアが既定。30 サイクル × 2 秒 = 実数 1.0 台。
        RecipeRun run = Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-disp");
        Assert.Equal("f-b", run.FacilityId);
        FacilityRequirement req = Assert.Single(plan.FacilityRequirements, f => f.FacilityId == "f-b");
        Assert.Equal(1.0, req.ExactCount, 6);
    }

    [Fact(DisplayName = "DSP-13: 引き戻しで処理ランが除去されない")]
    public void RetractionKeepsDisposalRun()
    {
        // 目標順 i-q → i-p。i-q は先に r-q で展開され、その後 r-m の副産物充当で r-q が引き戻される
        // （i-q の実効レートは r-q 15/分 > r-m 7.5/分で r-q が選ばれる）。
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true),
             F.Item("i-p"), F.Item("i-sew"), F.Item("i-q")],
            [F.Facility("f-m"), F.Facility("f-q"), F.Facility("f-trt")],
            [
                F.Recipe("r-m", "f-m", 8.0, [("i-ore", 1.0)], [("i-p", 1.0), ("i-sew", 1.0), ("i-q", 1.0)]),
                F.Recipe("r-q", "f-q", 4.0, [("i-ore", 1.0)], [("i-q", 1.0)]),
                F.Recipe("r-disp", "f-trt", 4.0, [("i-sew", 1.0)], []),
            ]);

        ProductionPlan plan = F.Run(master, [("i-q", 10.0), ("i-p", 30.0)]);

        Assert.DoesNotContain(plan.RecipeRuns, r => r.RecipeId == "r-q");
        Assert.Equal(30.0, Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-disp").CyclesPerMinute, 6);
        ItemRequirement q = Assert.Single(plan.ItemRequirements, r => r.ItemId == "i-q");
        Assert.Equal(0.0, q.UnmetPerMinute, 6);
    }

    [Fact(DisplayName = "DSP-14: 処理のために対象アイテムを新規生産しない")]
    public void DisposalDoesNotTriggerProduction()
    {
        // i-sew を生産する r-sew が登録済みでも、処理需要は余剰分（30−20=10）だけ。
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true),
             F.Item("i-p"), F.Item("i-sew"), F.Item("i-use")],
            [F.Facility("f-m"), F.Facility("f-u"), F.Facility("f-trt"), F.Facility("f-sew")],
            [
                F.Recipe("r-m", "f-m", 4.0, [("i-ore", 1.0)], [("i-p", 1.0), ("i-sew", 1.0)]),
                F.Recipe("r-use", "f-u", 6.0, [("i-sew", 1.0)], [("i-use", 1.0)]),
                F.Recipe("r-sew", "f-sew", 4.0, [("i-ore", 1.0)], [("i-sew", 1.0)]),
                F.Recipe("r-disp", "f-trt", 4.0, [("i-sew", 1.0)], []),
            ]);

        ProductionPlan plan = F.Run(master, [("i-p", 30.0), ("i-use", 20.0)]);

        Assert.Equal(10.0, Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-disp").CyclesPerMinute, 6);
        Assert.DoesNotContain(plan.RecipeRuns, r => r.RecipeId == "r-sew");
        ItemRequirement sew = Assert.Single(plan.ItemRequirements, r => r.ItemId == "i-sew");
        Assert.Equal(30.0, sew.RequiredPerMinute, 6);
        Assert.Equal(0.0, sew.UnmetPerMinute, 6);
    }

    [Fact(DisplayName = "DSP-16: 処理ランに推奨流量制限が出ない")]
    public void DisposalRunHasNoFlowAdjustment()
    {
        // r-disp は 45 サイクル × 5 秒 = 実数 3.75 台（端数機械。生産ランなら制限が出る台数）。
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true), F.Item("i-p"), F.Item("i-sew")],
            [F.Facility("f-asm"), F.Facility("f-trt")],
            [
                F.Recipe("r-m", "f-asm", 4.0, [("i-ore", 1.0)], [("i-p", 1.0), ("i-sew", 1.0)]),
                F.Recipe("r-disp", "f-trt", 5.0, [("i-sew", 1.0)], []),
            ]);

        ProductionPlan plan = F.Run(master, [("i-p", 45.0)]);

        Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-disp");
        Assert.DoesNotContain(plan.FlowAdjustments, a => a.RecipeId == "r-disp");
    }

    [Fact(DisplayName = "DSP-17: カバー不足で処理ランが削られる")]
    public void CoverageCapShrinksDisposalRun()
    {
        // 散布機 1 台（カバー 4 機）に上書き。処理ランは 300 サイクル（20 機）を要するが
        // カバー上限の 4 機相当（60 サイクル）に削られ、残りは余剰に残る。
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true),
             F.Item("i-gas", "採取素材", TransportKind.Pipe, null, true),
             F.Item("i-p"), F.Item("i-sew")],
            [F.Facility("f-asm"), F.Facility("f-trt"), F.Facility("f-disp")],
            [
                F.Recipe("r-m", "f-asm", 4.0, [("i-ore", 1.0)], [("i-p", 1.0), ("i-sew", 1.0)]),
                F.Recipe("r-disp", [F.Pair("r-disp", "f-trt", 4.0, "env-gas")],
                    [("i-sew", 1.0)], []),
            ],
            [F.Env("env-gas", "f-disp", "i-gas", 360.0)]);

        ProductionPlan plan = F.Run(
            master, [("i-p", 300.0)],
            environmentOverrides: [new EnvironmentCountOverride("env-gas", 1)]);

        RecipeRun run = Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-disp");
        Assert.Equal(60.0, run.CyclesPerMinute, 6);
        ItemRequirement sew = Assert.Single(plan.ItemRequirements, r => r.ItemId == "i-sew");
        Assert.Equal(0.0, sew.UnmetPerMinute, 6);
        Assert.Equal(240.0, Assert.Single(plan.Surpluses, s => s.ItemId == "i-sew").ExcessPerMinute, 6);
    }

    [Fact(DisplayName = "DSP-18: 出力なしレシピは需要展開の選択に現れない")]
    public void DisposalRecipeIsNotAProductionSelection()
    {
        // DSP-14 と同じ構成。i-sew の生産選択が処理レシピへ流れないことを見る。
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true),
             F.Item("i-p"), F.Item("i-sew"), F.Item("i-use")],
            [F.Facility("f-m"), F.Facility("f-u"), F.Facility("f-trt"), F.Facility("f-sew")],
            [
                F.Recipe("r-m", "f-m", 4.0, [("i-ore", 1.0)], [("i-p", 1.0), ("i-sew", 1.0)]),
                F.Recipe("r-use", "f-u", 6.0, [("i-sew", 1.0)], [("i-use", 1.0)]),
                F.Recipe("r-sew", "f-sew", 4.0, [("i-ore", 1.0)], [("i-sew", 1.0)]),
                F.Recipe("r-disp", "f-trt", 4.0, [("i-sew", 1.0)], []),
            ]);

        ProductionPlan plan = F.Run(master, [("i-p", 30.0), ("i-use", 20.0)]);

        // i-sew の需要は r-m の副産物で賄われるため選択自体が起きないが、
        // 処理レシピが生産選択へ現れないことと、処理需要だけが計上されることを見る。
        Assert.DoesNotContain(plan.PairSelections, s => s.RecipeId == "r-disp");
        Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-disp");
        ItemRequirement sew = Assert.Single(plan.ItemRequirements, r => r.ItemId == "i-sew");
        Assert.Equal(30.0, sew.RequiredPerMinute, 6);
        Assert.Equal(0.0, sew.UnmetPerMinute, 6);
        Assert.All(sew.Supplies, s => Assert.NotEqual("r-disp", s.RecipeId));
    }

    [Fact(DisplayName = "DSP-19: 複数の対象アイテムが同一処理レシピを選ぶ")]
    public void SharedDisposalRecipeMergesIntoOneRun()
    {
        // i-p 10/分 → i-sewa 10/分・i-sewb 20/分が余剰。両者を入力に持つ r-disp は
        // 1 ランにまとまり、サイクル上限は 10/1 = 10（i-sewb の 20/1 より小さい方）。
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true),
             F.Item("i-p"), F.Item("i-sewa"), F.Item("i-sewb")],
            [F.Facility("f-m"), F.Facility("f-trt")],
            [
                F.Recipe("r-m", "f-m", 4.0, [("i-ore", 1.0)],
                    [("i-p", 1.0), ("i-sewa", 1.0), ("i-sewb", 2.0)]),
                F.Recipe("r-disp", "f-trt", 4.0, [("i-sewa", 1.0), ("i-sewb", 1.0)], []),
            ]);

        ProductionPlan plan = F.Run(master, [("i-p", 10.0)]);

        RecipeRun run = Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-disp");
        Assert.Equal(10.0, run.CyclesPerMinute, 6);
        Assert.DoesNotContain(plan.Surpluses, s => s.ItemId == "i-sewa");
        Assert.Equal(10.0, Assert.Single(plan.Surpluses, s => s.ItemId == "i-sewb").ExcessPerMinute, 6);
    }

    [Fact(DisplayName = "DSP-20: 処理ランが選択ペアを保持する")]
    public void DisposalRunKeepsSelectedPair()
    {
        // 同一設備に 8 秒・2 秒の 2 ペア。先頭ペアではなく既定（CycleTime 最小）の 2 秒が使われる。
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true), F.Item("i-p"), F.Item("i-sew")],
            [F.Facility("f-asm"), F.Facility("f-x")],
            [
                F.Recipe("r-m", "f-asm", 4.0, [("i-ore", 1.0)], [("i-p", 1.0), ("i-sew", 1.0)]),
                F.Recipe("r-disp", [F.Pair("r-disp", "f-x", 8.0), F.Pair("r-disp", "f-x", 2.0)],
                    [("i-sew", 1.0)], []),
            ]);

        ProductionPlan plan = F.Run(master, [("i-p", 30.0)]);

        RecipeRun run = Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-disp");
        Assert.NotNull(run.Pair);
        Assert.Equal(2.0, run.Pair!.CycleTime, 6);
        // 30 サイクル × 2 秒 = 実数 1.0 台（先頭ペアの 8 秒なら 4.0 台になる）。
        Assert.Equal(1.0,
            Assert.Single(plan.FacilityRequirements, f => f.FacilityId == "f-x").ExactCount, 6);
    }

    [Fact(DisplayName = "DSP-23: 非有効イベント所属の余剰は処理対象にならない")]
    public void InactiveByproductIsNotDisposed()
    {
        // i-sew が非有効イベント所属。生産量は供給として認められない（仕様決定 X）ため、
        // 処理対象にも需要計上にもならず、余剰のまま残る（Devin Review 対応の回帰）。
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true), F.Item("i-p"),
             F.Item("i-sew", "汚水", TransportKind.Belt, "ev-off")],
            [F.Facility("f-asm"), F.Facility("f-trt")],
            [
                F.Recipe("r-m", "f-asm", 4.0, [("i-ore", 1.0)], [("i-p", 1.0), ("i-sew", 1.0)]),
                F.Recipe("r-disp", "f-trt", 4.0, [("i-sew", 1.0)], []),
            ],
            gameEvents: [F.GameEvent("ev-off")]);

        ProductionPlan plan = F.Run(master, [("i-p", 30.0)]);

        Assert.DoesNotContain(plan.RecipeRuns, r => r.RecipeId == "r-disp");
        Assert.DoesNotContain(plan.ItemRequirements, r => r.ItemId == "i-sew");
        Assert.Equal(30.0,
            Assert.Single(plan.Surpluses, s => s.ItemId == "i-sew").ExcessPerMinute, 6);
    }

    [Fact(DisplayName = "DSP-24: 計算目標のアイテムは処理入力にもならない")]
    public void TargetInputIsNotConsumedByDisposal()
    {
        // 処理レシピの入力に計算目標のアイテムが混ざる構成。i-aux は r-m の副産物でも
        // 供給されるため余剰 20 を持つが、目標アイテムは処理対象にも補助入力にもならない（CA）。
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true),
             F.Item("i-p"), F.Item("i-sew"), F.Item("i-aux")],
            [F.Facility("f-m"), F.Facility("f-aux"), F.Facility("f-trt")],
            [
                F.Recipe("r-m", "f-m", 4.0, [("i-ore", 1.0)],
                    [("i-p", 1.0), ("i-sew", 1.0), ("i-aux", 1.0)]),
                F.Recipe("r-auxp", "f-aux", 4.0, [("i-ore", 1.0)], [("i-aux", 1.0)]),
                F.Recipe("r-disp", "f-trt", 4.0, [("i-sew", 1.0), ("i-aux", 1.0)], []),
            ]);

        ProductionPlan plan = F.Run(master, [("i-p", 30.0), ("i-aux", 10.0)]);

        // i-sew の残り余剰 30 だけが上限を決め、目標 i-aux は消費も確定もされない。
        Assert.Equal(30.0, Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-disp").CyclesPerMinute, 6);
        ItemRequirement aux = Assert.Single(plan.ItemRequirements, r => r.ItemId == "i-aux");
        Assert.Equal(10.0, aux.RequiredPerMinute, 6);
        Assert.Equal(20.0,
            Assert.Single(plan.Surpluses, s => s.ItemId == "i-aux").ExcessPerMinute, 6);
        Assert.DoesNotContain(plan.Surpluses, s => s.ItemId == "i-sew");
    }

    [Fact(DisplayName = "DSP-22: 補助入力の不足で処理が止まらない")]
    public void AuxInputDoesNotBlockDisposal()
    {
        // i-sew 余剰 10・i-aux 余剰 0 の構成。i-aux は補助入力として通常需要に計上され、
        // その分の生産（r-auxp）が許容される。i-sew の生産は起きない。
        MasterDataSnapshot master = F.Snapshot(
            [F.Item("i-ore", "採取素材", TransportKind.Belt, null, true),
             F.Item("i-p"), F.Item("i-sew"), F.Item("i-aux")],
            [F.Facility("f-m"), F.Facility("f-aux"), F.Facility("f-trt")],
            [
                F.Recipe("r-m", "f-m", 4.0, [("i-ore", 1.0)], [("i-p", 1.0), ("i-sew", 1.0)]),
                F.Recipe("r-auxp", "f-aux", 4.0, [("i-ore", 1.0)], [("i-aux", 1.0)]),
                F.Recipe("r-disp", "f-trt", 4.0, [("i-sew", 1.0), ("i-aux", 1.0)], []),
            ]);

        ProductionPlan plan = F.Run(master, [("i-p", 10.0)]);

        Assert.Equal(10.0, Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-disp").CyclesPerMinute, 6);
        Assert.Equal(10.0, Assert.Single(plan.RecipeRuns, r => r.RecipeId == "r-auxp").CyclesPerMinute, 6);
        ItemRequirement aux = Assert.Single(plan.ItemRequirements, r => r.ItemId == "i-aux");
        Assert.Equal(10.0, aux.RequiredPerMinute, 6);
        Assert.DoesNotContain(plan.Surpluses, s => s.ItemId == "i-sew");
    }
}
