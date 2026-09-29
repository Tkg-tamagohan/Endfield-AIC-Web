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
        Assert.Equal(360.0, Supplied(plan, "i-gas", SupplyKind.RawMaterial), Precision);

        Assert.Equal(120.0, plan.TotalPowerConsumption, Precision);
    }

    [Fact(DisplayName = "ENV-02: 散布機既定台数は環境を要する稼働中レシピ数")]
    public void DispenserDefaultCountsRunningPairs()
    {
        ProductionPlan plan = CalculationFixtures.Run(
            CalculationFixtures.F10(), [("i-hp", 30.0), ("i-std", 12.0)]);

        Assert.Equal(2, Fac(plan, "f-disp").CeilCount);
        Assert.Equal(720.0, Req(plan, "i-gas").RequiredPerMinute, Precision);
        Assert.Equal(190.0, plan.TotalPowerConsumption, Precision);
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
}
