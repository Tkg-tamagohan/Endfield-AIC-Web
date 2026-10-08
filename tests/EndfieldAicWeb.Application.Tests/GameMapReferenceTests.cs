using EndfieldAicWeb.Application.Calculation;
using EndfieldAicWeb.Application.MasterEditing;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;

namespace EndfieldAicWeb.Application.Tests;

public class GameMapReferenceTests
{
    private static MasterDocument Document() => new()
    {
        SchemaVersion = 1,
        DataVersion = "1.0.0",
        Items = [],
        Facilities = [],
        Environments = [],
        GameEvents = [],
        Recipes = [],
        Maps = [],
    };

    private static GameMap Map(string id = "map-01", string? eventId = null) => new()
    {
        Id = id,
        Name = "採取地",
        Description = "",
        VersionAdded = "1.0.0",
        GameEventId = eventId,
        GatherRates = [],
    };

    [Fact(DisplayName = "MRF-01: アイテム参照にマップ行を含む")]
    public void ItemReferencesIncludeMapGatherRate()
    {
        MasterDocument document = Document();
        GameMap map = Map();
        map.GatherRates = [new GatherRate { ItemId = "item-ore", IsUnlimited = false, RatePerMinute = 60 }];
        document.Maps.Add(map);

        Assert.Contains(
            new MasterReference("GameMap", "map-01", "GatherRates[0].ItemId"),
            MasterReferenceFinder.FindItemReferences(document, "item-ore"));
    }

    [Fact(DisplayName = "MRF-02: イベント参照にマップを含む")]
    public void GameEventReferencesIncludeMap()
    {
        MasterDocument document = Document();
        document.Maps.Add(Map(eventId: "event-01"));

        Assert.Contains(
            new MasterReference("GameMap", "map-01", "GameEventId"),
            MasterReferenceFinder.FindGameEventReferences(document, "event-01"));
    }

    [Fact(DisplayName = "MRF-03: 新規マップの既定値")]
    public void NewGameMapHasValidDefaults()
    {
        GameMap map = EntityFactory.NewGameMap("map-001", "1.0.0");
        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateGameMap(map, errors);

        Assert.Equal("map-001", map.Id);
        Assert.Equal("新規マップ", map.Name);
        Assert.Empty(map.GatherRates);
        Assert.Null(map.GameEventId);
        Assert.Empty(errors);
    }

    [Fact(DisplayName = "MRF-04: スナップショットへの反映")]
    public void SnapshotIncludesMapsAndMapIndex()
    {
        MasterDocument document = Document();
        GameMap map = Map();
        document.Maps.Add(map);

        MasterDataSnapshot snapshot = MasterSnapshotFactory.Create(document);

        Assert.Same(map, Assert.Single(snapshot.Maps));
        Assert.Same(map, snapshot.MapsById["map-01"]);
    }
}
