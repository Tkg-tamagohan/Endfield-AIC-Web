using System.Text.Json.Nodes;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;
using EndfieldAicWeb.Infrastructure.Icons;
using EndfieldAicWeb.Infrastructure.Transfer;

namespace EndfieldAicWeb.Infrastructure.Tests;

public class MapJsonTests
{
    [Fact(DisplayName = "MJS-01: Maps の読み込み")]
    public void MapsLoadFromValidJson()
    {
        MasterJsonLoadResult result = MasterJsonLoader.Load(TestJson.ValidJson());

        Assert.True(result.Success, string.Join("\n", result.Errors.Select(e => e.Message)));
        GameMap map = Assert.Single(result.Document!.Maps);
        Assert.Equal("m-01", map.Id);
        Assert.Null(map.GameEventId);
        Assert.Collection(
            map.GatherRates,
            finite =>
            {
                Assert.Equal("i-ore", finite.ItemId);
                Assert.False(finite.IsUnlimited);
                Assert.Equal(60, finite.RatePerMinute);
            },
            unlimited =>
            {
                Assert.Equal("i-gas", unlimited.ItemId);
                Assert.True(unlimited.IsUnlimited);
                Assert.Null(unlimited.RatePerMinute);
            });
    }

    [Fact(DisplayName = "MJS-02: Maps キーの欠落")]
    public void MissingMapsKeyIsRejected()
    {
        MasterJsonLoadResult result = MasterJsonLoader.Load(TestJson.Mutate(root => root.Remove("Maps")));

        Assert.False(result.Success);
    }

    [Fact(DisplayName = "MJS-03: 行の必須キー欠落")]
    public void MissingGatherRateKeyIsRejected()
    {
        string json = TestJson.Mutate(root =>
            root["Maps"]!.AsArray()[0]!["GatherRates"]![0]!.AsObject().Remove("RatePerMinute"));

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
    }

    [Fact(DisplayName = "MJS-04: 未知プロパティの拒否")]
    public void UnknownMapAndGatherRatePropertiesAreRejected()
    {
        string unknownMapJson = TestJson.Mutate(root =>
            root["Maps"]!.AsArray()[0]!.AsObject()["UnknownMapProperty"] = true);
        string unknownRateJson = TestJson.Mutate(root =>
            root["Maps"]!.AsArray()[0]!["GatherRates"]![0]!.AsObject()["UnknownRateProperty"] = true);

        Assert.False(MasterJsonLoader.Load(unknownMapJson).Success);
        Assert.False(MasterJsonLoader.Load(unknownRateJson).Success);
    }

    [Fact(DisplayName = "MJS-05: null 要素の拒否")]
    public void NullMapAndGatherRateElementsAreRejected()
    {
        string nullMapJson = TestJson.Mutate(root =>
            root["Maps"]!.AsArray().Add((JsonNode?)null));
        string nullRateJson = TestJson.Mutate(root =>
            root["Maps"]!.AsArray()[0]!["GatherRates"]!.AsArray().Add((JsonNode?)null));

        Assert.False(MasterJsonLoader.Load(nullMapJson).Success);
        Assert.False(MasterJsonLoader.Load(nullRateJson).Success);
    }

    [Fact(DisplayName = "MJS-06: 意味違反の検出")]
    public void NonGatherableItemReferenceIsRejected()
    {
        string json = TestJson.Mutate(root =>
            root["Maps"]!.AsArray()[0]!["GatherRates"]![0]!["ItemId"] = "i-part");

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error =>
            error.EntityKind == "GameMap"
            && error.EntityId == "m-01"
            && error.Field == "GatherRates[0].ItemId");
    }

    [Fact(DisplayName = "MJS-07: 往復の保持")]
    public void MapRoundTripPreservesFieldsAndUnlimitedNull()
    {
        MasterJsonLoadResult first = MasterJsonLoader.Load(TestJson.ValidJson());
        Assert.True(first.Success, string.Join("\n", first.Errors.Select(e => e.Message)));

        string exported = MasterExporter.Export(first.Document!);
        Assert.Contains("\"RatePerMinute\": null", exported);
        JsonObject exportedRoot = JsonNode.Parse(exported)!.AsObject();
        JsonObject exportedUnlimitedRate = exportedRoot["Maps"]!.AsArray()[0]!
            ["GatherRates"]![1]!.AsObject();
        Assert.True(exportedUnlimitedRate.ContainsKey("RatePerMinute"));
        Assert.Null(exportedUnlimitedRate["RatePerMinute"]);

        MasterJsonLoadResult second = MasterJsonLoader.Load(exported);
        Assert.True(second.Success, string.Join("\n", second.Errors.Select(e => e.Message)));

        GameMap firstMap = Assert.Single(first.Document!.Maps);
        GameMap secondMap = Assert.Single(second.Document!.Maps);
        Assert.Equal(firstMap.Id, secondMap.Id);
        Assert.Equal(firstMap.Name, secondMap.Name);
        Assert.Equal(firstMap.Description, secondMap.Description);
        Assert.Equal(firstMap.IconKey, secondMap.IconKey);
        Assert.Equal(firstMap.VersionAdded, secondMap.VersionAdded);
        Assert.Equal(firstMap.VersionRemoved, secondMap.VersionRemoved);
        Assert.Equal(firstMap.GameEventId, secondMap.GameEventId);
        Assert.Equal(firstMap.GatherRates.Count, secondMap.GatherRates.Count);
        for (int i = 0; i < firstMap.GatherRates.Count; i++)
        {
            Assert.Equal(firstMap.GatherRates[i].ItemId, secondMap.GatherRates[i].ItemId);
            Assert.Equal(firstMap.GatherRates[i].IsUnlimited, secondMap.GatherRates[i].IsUnlimited);
            Assert.Equal(firstMap.GatherRates[i].RatePerMinute, secondMap.GatherRates[i].RatePerMinute);
        }
    }

    [Fact(DisplayName = "MJS-08: マップのアイコン参照")]
    public void MapIconIsIncludedInExportManifest()
    {
        MasterDocument document = TestJson.LoadValidDocument();
        GameMap map = Assert.Single(document.Maps);
        map.IconKey = "icon-map";
        byte[] iconBytes = [0x10, 0x20, 0x30];
        document.Icons.Add(IconExportPlanner.CreateEntry("icon-map", iconBytes));
        var iconFiles = new InMemoryIconFileProvider();
        Assert.True(iconFiles.Set("icons/icon-map.png", iconBytes));
        var errors = new List<MasterValidationError>();

        List<IconEntry> manifest = IconExportPlanner.BuildManifest(document, iconFiles, errors);

        Assert.Empty(errors);
        IconEntry entry = Assert.Single(manifest);
        Assert.Equal("icon-map", entry.Key);
    }
}
