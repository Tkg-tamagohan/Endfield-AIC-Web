using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Application;

namespace EndfieldAicWeb.Application.Tests;

/// <summary>OPT: ペア代替選択の候補。</summary>
public class PairOptionTests
{
    // OPT-01: 環境条件を満たす全ペアが候補になる。
    [Fact]
    public void ListsAllEligiblePairs()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A01();

        var candidates = PairSelector.ListCandidates("i-part", snapshot, ApplicationFixtures.Context());

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, c => c.Pair.EnvironmentId is null && c.Pair.CycleTime == 4);
        Assert.Contains(candidates, c => c.Pair.EnvironmentId == "env-gas" && c.Pair.CycleTime == 3);
    }

    // OPT-02: 既定ペアには IsDefault が付く（A-01 の既定は 3 秒ペア）。
    [Fact]
    public void MarksExactlyOneDefault()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A01();

        var candidates = PairSelector.ListCandidates("i-part", snapshot, ApplicationFixtures.Context());

        PairSelector.CandidatePair single = Assert.Single(candidates, c => c.IsDefault);
        Assert.Equal(3, single.Pair.CycleTime);
    }

    // OPT-03: 限定イベント・所属イベントが無効なレシピと環境のペアは候補外。
    [Fact]
    public void ExcludesInactiveEventRecipesAndEnvironments()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A04();

        var candidates = PairSelector.ListCandidates("i-x", snapshot, ApplicationFixtures.Context());

        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, c => Assert.Equal("r-x-main", c.Recipe.Id));
        PairSelector.CandidatePair single = Assert.Single(candidates, c => c.IsDefault);
        Assert.Equal("f-b", single.Pair.FacilityId);
    }

    // OPT-03b: イベントを有効にすると限定レシピ・環境ペアが候補に復帰する。
    [Fact]
    public void IncludesEventRecipesAndEnvironmentsWhenActive()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A04();

        var candidates = PairSelector.ListCandidates("i-x", snapshot, ApplicationFixtures.Context("ev-off"));

        Assert.Equal(4, candidates.Count);
    }

    // OPT-04: 候補の Key は全要素を含むペア一意キーで衝突しない。
    [Fact]
    public void KeysAreDistinctAcrossPairs()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A01();
        var service = new CalculationService();

        CalculationOutcome outcome = service.Calculate(
            snapshot,
            [new ProductionTarget("i-part", 60)],
            ApplicationFixtures.Context(),
            [],
            []);

        IReadOnlyList<PairOption> options = outcome.PairOptionsByItemId["i-part"];
        Assert.Equal(2, options.Count);
        Assert.Equal(options.Count, options.Select(o => o.Key).Distinct().Count());
    }

    // OPT-05: ToOverride は全要素の組を持つ上書き指定になる。
    [Fact]
    public void ToOverrideCarriesFullPairIdentity()
    {
        var fc = new FixedConsumption { ItemId = "i-fuel", RatePerMinute = 30 };
        var option = new PairOption("key", "r-1", "f-1", 3.0, "env-1", fc, false);

        PairOverride actual = option.ToOverride("i-t");

        Assert.Equal("i-t", actual.ItemId);
        Assert.Equal("r-1", actual.RecipeId);
        Assert.Equal("f-1", actual.FacilityId);
        Assert.Equal(3.0, actual.CycleTime);
        Assert.Equal("env-1", actual.EnvironmentId);
        Assert.Same(fc, actual.FixedConsumption);
    }
}
