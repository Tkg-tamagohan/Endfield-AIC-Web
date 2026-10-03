using EndfieldAicWeb.Domain.Calculation;
using static EndfieldAicWeb.Domain.Tests.PlanAssert;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>ENV: 環境の計上（仕様決定 I、docs/phases/test-specification-phase2.md §3）。</summary>
public class EnvironmentTests
{
    [Fact(DisplayName = "ENV-01: 環境必要ペアで散布機・ガス・電力を計上")]
    public void EnvironmentPairAddsDispenserGasAndPower()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F10(), [("i-hp", 30.0)]);

        RecipeRun run = Assert.Single(plan.RecipeRuns);
        Assert.Equal("r-hp", run.RecipeId);
        Assert.Equal("f-asm", run.FacilityId);
        Assert.Equal(30.0, run.CyclesPerMinute, Precision);

        Assert.Equal(2, Fac(plan, "f-asm").CeilCount);
        Assert.Equal(1, Fac(plan, "f-disp").CeilCount);

        EnvironmentRequirement envReq = Assert.Single(plan.EnvironmentRequirements);
        Assert.Equal("env-gas", envReq.EnvironmentId);
        Assert.Equal(1, envReq.DispenserCount);

        ItemRequirement gas = Req(plan, "i-gas");
        Assert.Equal(360.0, gas.RequiredPerMinute, Precision);
        Assert.Equal(360.0, Supplied(plan, "i-gas", SupplyKind.Gathered), Precision);

        Assert.Equal(120.0, plan.TotalPowerConsumption, Precision);
    }

    [Fact(DisplayName = "ENV-02: 散布機既定台数は環境を要する稼働機械数÷カバー可能台数（仕様決定 BQ）")]
    public void DispenserDefaultCountsMachines()
    {
        // i-hp 30/分（4 秒ペア 2.0 機）＋ i-std 12/分（5 秒ペア 1.0 機）= 3.0 機 → 散布機 1 台。
        // 旧契約（稼働中レシピ数 2 → 2 台・720/分）から BQ への改訂分。
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F10(), [("i-hp", 30.0), ("i-std", 12.0)]);

        Assert.Equal(1, Fac(plan, "f-disp").CeilCount);
        Assert.Equal(360.0, Req(plan, "i-gas").RequiredPerMinute, Precision);
        Assert.Equal(170.0, plan.TotalPowerConsumption, Precision);
    }

    [Fact(DisplayName = "ENV-03: 散布機台数の上書き")]
    public void DispenserOverrideApplies()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F10(), [("i-hp", 30.0)],
            environmentOverrides: [new EnvironmentCountOverride("env-gas", 3)]);

        Assert.Equal(3, Fac(plan, "f-disp").CeilCount);
        Assert.Equal(1080.0, Req(plan, "i-gas").RequiredPerMinute, Precision);
        Assert.Equal(160.0, plan.TotalPowerConsumption, Precision);
    }

    [Fact(DisplayName = "ENV-04: ペア上書きで環境なし運用へ切替")]
    public void PairOverrideToNoEnvironmentRemovesDispenser()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F10(), [("i-hp", 30.0)],
            overrides: [CalculationFixtures.Override("i-hp", "r-hp", "f-asm", 8.0)]);

        Assert.Empty(plan.EnvironmentRequirements);
        Assert.False(HasFac(plan, "f-disp"));
        Assert.False(HasReq(plan, "i-gas"));
        Assert.Equal(4.0, Fac(plan, "f-asm").ExactCount, Precision);
    }

    [Fact(DisplayName = "ENV-05: 環境が非有効イベントならペアは候補外")]
    public void InactiveEventEnvironmentMakesPairIneligible()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F10WithInactiveEnv(), [("i-hp", 30.0)]);

        RecipeRun run = Assert.Single(plan.RecipeRuns);
        Assert.Equal("f-asm", run.FacilityId);
        Assert.Empty(plan.EnvironmentRequirements);
        Assert.False(HasFac(plan, "f-disp"));
        Assert.Equal(4.0, Fac(plan, "f-asm").ExactCount, Precision);
    }

    [Fact(DisplayName = "ENV-06: 負の散布機台数上書きは警告＋既定台数")]
    public void NegativeDispenserOverrideWarnsAndUsesDefault()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F10(), [("i-hp", 30.0)],
            environmentOverrides: [new EnvironmentCountOverride("env-gas", -2)]);

        Assert.True(HasWarning(plan, WarningCode.InvalidEnvironmentOverride));
        Assert.Equal(1, Fac(plan, "f-disp").CeilCount);
        Assert.Equal(360.0, Req(plan, "i-gas").RequiredPerMinute, Precision);
    }

    [Fact(DisplayName = "ENV-07: 機械数がカバー可能台数を超えると複数台になる")]
    public void MachinesExceedingCoverableNeedMultipleDispensers()
    {
        // i-hp 65/分 → 4 秒ペアで 65×4/60 ≈ 4.33 機 > 4 → 散布機 2 台（旧式では 1 台に留まる）。
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F10(), [("i-hp", 65.0)]);

        EnvironmentRequirement envReq = Assert.Single(plan.EnvironmentRequirements);
        Assert.Equal(2, envReq.DispenserCount);
        Assert.Equal(2, envReq.RequiredDispenserCount);
        Assert.Equal(720.0, Req(plan, "i-gas").RequiredPerMinute, Precision);
        Assert.Equal(5, Fac(plan, "f-asm").CeilCount);
    }

    [Fact(DisplayName = "ENV-08: 複数ランの機械数は合算される")]
    public void MachinesAcrossRunsSumForDispenserCount()
    {
        // i-hp 60/分（4.0 機）＋ i-std 12/分（1.0 機）= 5.0 機 → 散布機 2 台。
        // レシピ数 2 でなく機械数 5.0 が分母になる。
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F10(), [("i-hp", 60.0), ("i-std", 12.0)]);

        EnvironmentRequirement envReq = Assert.Single(plan.EnvironmentRequirements);
        Assert.Equal(2, envReq.DispenserCount);
        Assert.Equal(720.0, Req(plan, "i-gas").RequiredPerMinute, Precision);
    }

    [Fact(DisplayName = "ENV-09: 機械数がカバー可能台数ちょうどなら 1 台")]
    public void MachinesExactlyCoverableNeedOneDispenser()
    {
        // i-hp 60/分 → 4.0 機 = CoverableMachines 4 → 散布機 1 台。
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F10(), [("i-hp", 60.0)]);

        EnvironmentRequirement envReq = Assert.Single(plan.EnvironmentRequirements);
        Assert.Equal(1, envReq.DispenserCount);
        Assert.Equal(1, envReq.RequiredDispenserCount);
        Assert.Equal(360.0, Req(plan, "i-gas").RequiredPerMinute, Precision);
    }

    [Fact(DisplayName = "ENV-10: 上書きで機械数が頭打ちになり残りは未充足")]
    public void DispenserOverrideCapsMachines()
    {
        // 上書き 1 → カバー 4.0 機。4.33 機の需要は 4.0 機（60/分）で頭打ち、残り 5/分は未充足。
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F10(), [("i-hp", 65.0)],
            environmentOverrides: [new EnvironmentCountOverride("env-gas", 1)]);

        Assert.Equal(60.0, Supplied(plan, "i-hp", SupplyKind.Recipe), Precision);
        Assert.Equal(5.0, Req(plan, "i-hp").UnmetPerMinute, Precision);
        Assert.True(HasWarning(plan, WarningCode.EnvironmentCoverageExceeded));
    }

    [Fact(DisplayName = "ENV-11: 上書き 0 は環境生産を全量未充足にし環境行は残る")]
    public void ZeroDispenserOverrideBlocksAllEnvProduction()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F10(), [("i-hp", 30.0)],
            environmentOverrides: [new EnvironmentCountOverride("env-gas", 0)]);

        Assert.Equal(30.0, Req(plan, "i-hp").UnmetPerMinute, Precision);
        Assert.True(HasWarning(plan, WarningCode.EnvironmentCoverageExceeded));

        // 「自動 N 台まで」を表示するため、停止中の環境も要件行に残る。
        EnvironmentRequirement envReq = Assert.Single(plan.EnvironmentRequirements);
        Assert.Equal(0, envReq.DispenserCount);
        Assert.Equal(1, envReq.RequiredDispenserCount);
    }

    [Fact(DisplayName = "ENV-12: 上書きが必要台数ちょうどなら制限は発生しない")]
    public void OverrideMeetingRequirementDoesNotClamp()
    {
        // 上書き 2 → カバー 8.0 機 ≥ 4.33 機で全量生産。
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F10(), [("i-hp", 65.0)],
            environmentOverrides: [new EnvironmentCountOverride("env-gas", 2)]);

        Assert.Equal(65.0, Supplied(plan, "i-hp", SupplyKind.Recipe), Precision);
        Assert.False(HasWarning(plan, WarningCode.EnvironmentCoverageExceeded));
    }

    [Fact(DisplayName = "ENV-13: カバー不足でも代替ペアへ自動切替しない")]
    public void CoverageShortageDoesNotSwitchPair()
    {
        // 散布機 0 へ下げても r-hp の 8 秒非環境ペアへは切り替わらず未充足のみが出る。
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F10(), [("i-hp", 30.0)],
            environmentOverrides: [new EnvironmentCountOverride("env-gas", 0)]);

        Assert.Empty(plan.RecipeRuns);
        Assert.Equal(30.0, Req(plan, "i-hp").UnmetPerMinute, Precision);
        Assert.True(HasWarning(plan, WarningCode.EnvironmentCoverageExceeded));
    }

    [Fact(DisplayName = "ENV-14: 削減された機械分は必要台数の見積もりに含まれる")]
    public void BlockedMachinesCountTowardRequiredDispenserCount()
    {
        // 上書き 1 → 実稼働 4.0 機＋削減 0.33 機 → 必要台数 (4.0+0.33)/4 = 2。
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F10(), [("i-hp", 65.0)],
            environmentOverrides: [new EnvironmentCountOverride("env-gas", 1)]);

        EnvironmentRequirement envReq = Assert.Single(plan.EnvironmentRequirements);
        Assert.Equal(1, envReq.DispenserCount);
        Assert.Equal(2, envReq.RequiredDispenserCount);
    }

    [Fact(DisplayName = "ENV-15: 複数ランは同じ上限を展開順で分け合う")]
    public void CoverageIsOccupiedInExpansionOrder()
    {
        // 上書き 1 → カバー 4.0 機。先に展開される i-hp（4.0 機）が占有し i-std は全量未充足。
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F10(), [("i-hp", 60.0), ("i-std", 12.0)],
            environmentOverrides: [new EnvironmentCountOverride("env-gas", 1)]);

        Assert.Equal(60.0, Supplied(plan, "i-hp", SupplyKind.Recipe), Precision);
        Assert.Equal(12.0, Req(plan, "i-std").UnmetPerMinute, Precision);
        Assert.True(HasWarning(plan, WarningCode.EnvironmentCoverageExceeded));
    }

    [Fact(DisplayName = "ENV-16: 削減未充足が別経路で解消されると必要台数と警告が残存分に追従する")]
    public void ResolvedBlockedUnmetShrinksRequiredCountAndWarning()
    {
        // i-hp 125/分は 8.33 機分。上書き 1 → 4.0 機稼働・65/分を削減（必要台数 3）。
        // r-side の副産物で 50/分賄うと残存 15/分 → 有効削減 1.0 機 → 必要台数 2 へ下がるが警告は残る。
        ProductionPlan partial = CalculationFixtures.Run(
            CalculationFixtures.F10WithByproductRescue(), [("i-hp", 125.0), ("i-side", 10.0)],
            environmentOverrides: [new EnvironmentCountOverride("env-gas", 1)]);

        Assert.Equal(15.0, Req(partial, "i-hp").UnmetPerMinute, Precision);
        Assert.Equal(2, Assert.Single(partial.EnvironmentRequirements).RequiredDispenserCount);
        Assert.True(HasWarning(partial, WarningCode.EnvironmentCoverageExceeded));

        // 全量解消（副産物 65/分）なら必要台数は実稼働 4.0 機のみで 1、警告も出ない。
        ProductionPlan resolved = CalculationFixtures.Run(
            CalculationFixtures.F10WithByproductRescue(), [("i-hp", 125.0), ("i-side", 13.0)],
            environmentOverrides: [new EnvironmentCountOverride("env-gas", 1)]);

        Assert.Equal(0.0, Req(resolved, "i-hp").UnmetPerMinute, Precision);
        Assert.Equal(1, Assert.Single(resolved.EnvironmentRequirements).RequiredDispenserCount);
        Assert.False(HasWarning(resolved, WarningCode.EnvironmentCoverageExceeded));
    }
}
