using EndfieldAicWeb.Application;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

/// <summary>MPS: マップ候補と既定上限。</summary>
public class MapSelectionTests
{
    // MPS-01: 候補は常設と有効イベントのマップ。
    [Fact]
    public void ListsPermanentAndActiveEventMaps()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A06();

        IReadOnlyList<GameMap> candidates = MapSelection.ListCandidates(snapshot, ["ev-on"]);

        Assert.Equal(["m-cap", "m-on", "m-empty"], candidates.Select(map => map.Id));
    }

    // MPS-02: 有効イベントなしの候補。
    [Fact]
    public void ListsMapsWhenNoEventsAreActive()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A06();

        IReadOnlyList<GameMap> candidates = MapSelection.ListCandidates(snapshot, []);

        Assert.Equal(["m-cap", "m-empty"], candidates.Select(map => map.Id));
    }

    // MPS-03: 使えるマップの判定。
    [Fact]
    public void ChecksMapAvailability()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A06();

        Assert.True(MapSelection.IsAvailable(snapshot, "m-cap", ["ev-on"]));
        Assert.True(MapSelection.IsAvailable(snapshot, "m-on", ["ev-on"]));
        Assert.False(MapSelection.IsAvailable(snapshot, "m-off", ["ev-on"]));
        Assert.False(MapSelection.IsAvailable(snapshot, "m-ghost", ["ev-on"]));
    }

    // MPS-04: マップ未選択の既定上限。
    [Fact]
    public void ReturnsUnlimitedCapWhenNoMapIsSelected()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A06();
        var context = new ContextFilter { MapId = null };

        Assert.Null(MapSelection.DefaultGatherCap(snapshot, context, "i-ore"));
    }

    // MPS-05: 上限行、無限行、行なしの既定上限。
    [Fact]
    public void ReturnsCapsFromMapRows()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A06();

        Assert.Equal(60, MapSelection.DefaultGatherCap(snapshot, new ContextFilter { MapId = "m-cap" }, "i-ore"));
        Assert.Null(MapSelection.DefaultGatherCap(snapshot, new ContextFilter { MapId = "m-cap" }, "i-gas"));
        Assert.Equal(0, MapSelection.DefaultGatherCap(snapshot, new ContextFilter { MapId = "m-empty" }, "i-ore"));
    }

    // MPS-06: 使えないマップの既定上限。
    [Fact]
    public void ReturnsZeroCapForUnavailableMaps()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A06();

        Assert.Equal(0, MapSelection.DefaultGatherCap(
            snapshot,
            new ContextFilter { MapId = "m-off" },
            "i-ore"));
        Assert.Equal(0, MapSelection.DefaultGatherCap(
            snapshot,
            new ContextFilter { MapId = "m-ghost" },
            "i-ore"));
    }

    // MPS-07: 採取レート入力行の対象。
    [Fact]
    public void ReturnsGatherableItemIdsInRequirementOrder()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A06();
        CalculationOutcome outcome = new CalculationService().Calculate(
            snapshot,
            [new ProductionTarget("i-part", 60)],
            new ContextFilter { MapId = "m-cap" },
            [],
            [],
            []);

        IReadOnlyList<string> itemIds = MapSelection.GatherableItemIds(outcome.Plan, snapshot);

        Assert.Equal(["i-ore", "i-gas"], itemIds);
    }

    // MPS-08: 既定上限と計算の一致。
    [Fact]
    public void GatheredRateMatchesDefaultCap()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A06();
        var context = new ContextFilter { MapId = "m-cap" };
        CalculationOutcome outcome = new CalculationService().Calculate(
            snapshot,
            [new ProductionTarget("i-part", 60)],
            new ContextFilter { MapId = "m-cap" },
            [],
            [],
            []);
        ItemRequirement ore = Assert.Single(outcome.Plan.ItemRequirements, requirement => requirement.ItemId == "i-ore");
        double gathered = ore.Supplies
            .Where(supply => supply.Kind == SupplyKind.Gathered)
            .Sum(supply => supply.AmountPerMinute);
        double? defaultCap = MapSelection.DefaultGatherCap(snapshot, context, "i-ore");

        Assert.True(defaultCap.HasValue);
        Assert.Equal(60, defaultCap.Value);
        Assert.Equal(defaultCap.Value, gathered);
    }
}
