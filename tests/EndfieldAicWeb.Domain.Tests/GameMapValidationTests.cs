using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;

namespace EndfieldAicWeb.Domain.Tests;

public class GameMapValidationTests
{
    private static GameMap Map(string id = "map-01", string? eventId = null) => new()
    {
        Id = id,
        Name = "採取地",
        Description = "",
        VersionAdded = "1.0.0",
        GameEventId = eventId,
        GatherRates = [],
    };

    private static Item Item(string id, bool isGatherable = true) => new()
    {
        Id = id,
        Name = id,
        Category = "素材",
        IsGatherable = isGatherable,
        TransportKind = TransportKind.Belt,
        VersionAdded = "1.0.0",
    };

    private static List<MasterValidationError> ValidateMap(GameMap map)
    {
        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateGameMap(map, errors);
        return errors;
    }

    private static List<MasterValidationError> ValidateAll(
        IReadOnlyList<Item> items,
        IReadOnlyList<GameEvent> events,
        IReadOnlyList<GameMap> maps)
    {
        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateAll(items, [], [], events, [], maps, errors);
        return errors;
    }

    [Fact(DisplayName = "MAP-01: 正常なマップは違反なし")]
    public void ValidMapWithBoundedAndUnlimitedRatesHasNoErrors()
    {
        GameMap map = Map();
        map.GatherRates =
        [
            new GatherRate { ItemId = "item-ore", IsUnlimited = false, RatePerMinute = 60 },
            new GatherRate { ItemId = "item-gas", IsUnlimited = true, RatePerMinute = null },
        ];

        List<MasterValidationError> errors = ValidateAll(
            [Item("item-ore"), Item("item-gas")], [], [map]);

        Assert.Empty(errors);
    }

    [Fact(DisplayName = "MAP-02: 採取レート行なしも許容")]
    public void EmptyGatherRatesAreAllowed()
    {
        Assert.Empty(ValidateMap(Map()));
    }

    [Fact(DisplayName = "MAP-03: 共通属性の検証を適用")]
    public void EmptyNameIsRequired()
    {
        GameMap map = Map();
        map.Name = "";

        MasterValidationError error = Assert.Single(ValidateMap(map));

        Assert.Equal("GameMap", error.EntityKind);
        Assert.Equal("map-01", error.EntityId);
        Assert.Equal("Name", error.Field);
    }

    [Fact(DisplayName = "MAP-04: ItemId 必須")]
    public void GatherRateItemIdIsRequired()
    {
        GameMap map = Map();
        map.GatherRates = [new GatherRate { ItemId = "", IsUnlimited = false, RatePerMinute = 60 }];

        MasterValidationError error = Assert.Single(ValidateMap(map));

        Assert.Equal("GameMap", error.EntityKind);
        Assert.Equal("map-01", error.EntityId);
        Assert.Equal("GatherRates[0].ItemId", error.Field);
    }

    [Fact(DisplayName = "MAP-05: 同一マップ内の ItemId 重複")]
    public void DuplicateItemIdWithinMapIsRejected()
    {
        GameMap map = Map();
        map.GatherRates =
        [
            new GatherRate { ItemId = "item-ore", IsUnlimited = false, RatePerMinute = 60 },
            new GatherRate { ItemId = "item-ore", IsUnlimited = false, RatePerMinute = 30 },
        ];

        MasterValidationError error = Assert.Single(ValidateMap(map));

        Assert.Equal("GameMap", error.EntityKind);
        Assert.Equal("map-01", error.EntityId);
        Assert.Equal("GatherRates", error.Field);
    }

    [Fact(DisplayName = "MAP-06: 別マップ間の同一 ItemId は許容")]
    public void SameItemCanAppearOnDifferentMaps()
    {
        GameMap first = Map("map-01");
        GameMap second = Map("map-02");
        first.GatherRates = [new GatherRate { ItemId = "item-ore", IsUnlimited = false, RatePerMinute = 60 }];
        second.GatherRates = [new GatherRate { ItemId = "item-ore", IsUnlimited = false, RatePerMinute = 30 }];

        List<MasterValidationError> errors = ValidateAll([Item("item-ore")], [], [first, second]);

        Assert.Empty(errors);
    }

    [Fact(DisplayName = "MAP-07: 上限行のレート必須")]
    public void BoundedRateCannotBeNull()
    {
        GameMap map = Map();
        map.GatherRates = [new GatherRate { ItemId = "item-ore", IsUnlimited = false, RatePerMinute = null }];

        MasterValidationError error = Assert.Single(ValidateMap(map));

        Assert.Equal("GameMap", error.EntityKind);
        Assert.Equal("map-01", error.EntityId);
        Assert.Equal("GatherRates[0].RatePerMinute", error.Field);
    }

    [Theory(DisplayName = "MAP-08: 上限行のレートは正の有限値")]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void BoundedRateMustBePositiveAndFinite(double ratePerMinute)
    {
        GameMap map = Map();
        map.GatherRates =
        [
            new GatherRate { ItemId = "item-ore", IsUnlimited = false, RatePerMinute = ratePerMinute },
        ];

        MasterValidationError error = Assert.Single(ValidateMap(map));

        Assert.Equal("GameMap", error.EntityKind);
        Assert.Equal("map-01", error.EntityId);
        Assert.Equal("GatherRates[0].RatePerMinute", error.Field);
    }

    [Fact(DisplayName = "MAP-09: 無限行はレートを持たない")]
    public void UnlimitedRateMustBeNull()
    {
        GameMap map = Map();
        map.GatherRates = [new GatherRate { ItemId = "item-ore", IsUnlimited = true, RatePerMinute = 60 }];

        MasterValidationError error = Assert.Single(ValidateMap(map));

        Assert.Equal("GameMap", error.EntityKind);
        Assert.Equal("map-01", error.EntityId);
        Assert.Equal("GatherRates[0].RatePerMinute", error.Field);
    }

    [Fact(DisplayName = "MAP-10: 参照先アイテムの存在")]
    public void MissingItemReferenceIsRejected()
    {
        GameMap map = Map();
        map.GatherRates = [new GatherRate { ItemId = "item-missing", IsUnlimited = false, RatePerMinute = 60 }];

        MasterValidationError error = Assert.Single(ValidateAll([], [], [map]));

        Assert.Equal("GameMap", error.EntityKind);
        Assert.Equal("map-01", error.EntityId);
        Assert.Equal("GatherRates[0].ItemId", error.Field);
    }

    [Fact(DisplayName = "MAP-11: 参照先は採取素材のみ")]
    public void NonGatherableItemReferenceIsRejected()
    {
        GameMap map = Map();
        map.GatherRates = [new GatherRate { ItemId = "item-part", IsUnlimited = false, RatePerMinute = 60 }];

        MasterValidationError error = Assert.Single(ValidateAll([Item("item-part", isGatherable: false)], [], [map]));

        Assert.Equal("GameMap", error.EntityKind);
        Assert.Equal("map-01", error.EntityId);
        Assert.Equal("GatherRates[0].ItemId", error.Field);
    }

    [Fact(DisplayName = "MAP-12: 所属イベントの参照")]
    public void MissingGameEventReferenceIsRejected()
    {
        GameMap map = Map(eventId: "event-missing");

        MasterValidationError error = Assert.Single(ValidateAll([], [], [map]));

        Assert.Equal("GameMap", error.EntityKind);
        Assert.Equal("map-01", error.EntityId);
        Assert.Equal("GameEventId", error.Field);
    }

    [Fact(DisplayName = "MAP-13: Maps 内の Id 重複")]
    public void DuplicateMapIdIsRejected()
    {
        List<MasterValidationError> errors = ValidateAll([], [], [Map("map-duplicate"), Map("map-duplicate")]);

        MasterValidationError error = Assert.Single(errors);

        Assert.Equal("Maps", error.EntityKind);
        Assert.Equal("map-duplicate", error.EntityId);
        Assert.Equal("Id", error.Field);
    }
}
