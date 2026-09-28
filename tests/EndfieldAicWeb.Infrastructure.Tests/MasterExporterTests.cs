using System.Text.Json.Nodes;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;
using EndfieldAicWeb.Infrastructure.Transfer;

namespace EndfieldAicWeb.Infrastructure.Tests;

/// <summary>
/// MasterExporter・往復の検証テスト。ケース ID は docs/test-specification-phase3.md に対応する。
/// </summary>
public class MasterExporterTests
{
    // ---------- XPT: エクスポート ----------

    [Fact(DisplayName = "XPT-01: 正当なドキュメントが SchemaVersion=1・DataVersion・全配列キー付きで出力される")]
    public void Export_WritesAllRootKeys()
    {
        string json = MasterExporter.Export(TestJson.LoadValidDocument());
        JsonObject root = JsonNode.Parse(json)!.AsObject();

        Assert.Equal(1, root["SchemaVersion"]!.GetValue<int>());
        Assert.Equal("1.0.0", root["DataVersion"]!.GetValue<string>());
        foreach (string key in new[] { "Items", "Facilities", "Environments", "GameEvents", "Recipes", "Icons" })
        {
            Assert.True(root.ContainsKey(key), $"ルートキー {key} がない");
        }
    }

    [Fact(DisplayName = "XPT-02: null フィールドが null 値のまま出力される")]
    public void Export_WritesNullFields()
    {
        string json = MasterExporter.Export(TestJson.LoadValidDocument());
        JsonObject root = JsonNode.Parse(json)!.AsObject();

        JsonObject item = root["Items"]!.AsArray()[0]!.AsObject();
        Assert.True(item.ContainsKey("IconKey"));
        Assert.Null(item["IconKey"]);
        Assert.True(item.ContainsKey("VersionRemoved"));
        Assert.Null(item["VersionRemoved"]);
        Assert.True(item.ContainsKey("GameEventId"));
        Assert.Null(item["GameEventId"]);

        JsonObject pair = root["Recipes"]!.AsArray()[0]!.AsObject()["Facilities"]!.AsArray()[0]!.AsObject();
        Assert.True(pair.ContainsKey("EnvironmentId"));
        Assert.Null(pair["EnvironmentId"]);
        Assert.True(pair.ContainsKey("FixedConsumption"));
        Assert.Null(pair["FixedConsumption"]);
    }

    [Fact(DisplayName = "XPT-03: ペア要素に RecipeId キーを書かない")]
    public void Export_PairHasNoRecipeId()
    {
        string json = MasterExporter.Export(TestJson.LoadValidDocument());
        JsonObject root = JsonNode.Parse(json)!.AsObject();

        foreach (JsonNode? pair in root["Recipes"]!.AsArray()[0]!.AsObject()["Facilities"]!.AsArray())
        {
            Assert.False(pair!.AsObject().ContainsKey("RecipeId"), "ペアに RecipeId が出力されている");
        }
    }

    [Fact(DisplayName = "XPT-04: 検証違反を含むドキュメントは MasterValidationException で拒否される")]
    public void Export_InvalidDocument_Throws()
    {
        MasterDocument document = TestJson.LoadValidDocument();
        document.Recipes[0].Inputs[0].ItemId = "i-ghost";

        MasterValidationException exception =
            Assert.Throws<MasterValidationException>(() => MasterExporter.Export(document));

        Assert.NotEmpty(exception.Errors);
    }

    [Fact(DisplayName = "XPT-05: SchemaVersion=2 のドキュメントは例外で拒否される")]
    public void Export_UnsupportedSchemaVersion_Throws()
    {
        MasterDocument document = TestJson.LoadValidDocument();
        document.SchemaVersion = 2;

        Assert.Throws<MasterValidationException>(() => MasterExporter.Export(document));
    }

    [Fact(DisplayName = "XPT-06: DataVersion が空のドキュメントは例外で拒否される")]
    public void Export_EmptyDataVersion_Throws()
    {
        MasterDocument document = TestJson.LoadValidDocument();
        document.DataVersion = "";

        Assert.Throws<MasterValidationException>(() => MasterExporter.Export(document));
    }

    [Fact(DisplayName = "XPT-07: 日本語名が \\uXXXX エスケープではなく日本語文字のまま出力される")]
    public void Export_Japanese_NotEscaped()
    {
        string json = MasterExporter.Export(TestJson.LoadValidDocument());

        Assert.Contains("原鉱石", json);
        Assert.DoesNotContain("\\u539f", json);
    }

    [Fact(DisplayName = "XPT-08: Sha256 が 64 桁でない Icons エントリを含むドキュメントは例外で拒否される")]
    public void Export_InvalidIconEntry_Throws()
    {
        MasterDocument document = TestJson.LoadValidDocument();
        document.Icons[0].Sha256 = "xyz";

        Assert.Throws<MasterValidationException>(() => MasterExporter.Export(document));
    }

    // ---------- RND: 往復（data/master.json を正本として使う） ----------

    [Fact(DisplayName = "RND-01: master.json を読み込み→エクスポート→読み込み→エクスポートで 2 出力が文字列一致")]
    public void RoundTrip_StableOutput()
    {
        string original = File.ReadAllText(TestJson.MasterJsonPath);

        MasterJsonLoadResult first = MasterJsonLoader.Load(original);
        Assert.True(first.Success, string.Join("\n", first.Errors.Select(e => e.Message)));

        string exported1 = MasterExporter.Export(first.Document!);
        MasterJsonLoadResult second = MasterJsonLoader.Load(exported1);
        Assert.True(second.Success, string.Join("\n", second.Errors.Select(e => e.Message)));

        string exported2 = MasterExporter.Export(second.Document!);
        Assert.Equal(exported1, exported2);
    }

    [Fact(DisplayName = "RND-02: 往復で件数・代表フィールド・ペア・Icons が保存される")]
    public void RoundTrip_ContentPreserved()
    {
        string original = File.ReadAllText(TestJson.MasterJsonPath);
        MasterJsonLoadResult first = MasterJsonLoader.Load(original);
        Assert.True(first.Success);

        MasterJsonLoadResult second = MasterJsonLoader.Load(MasterExporter.Export(first.Document!));
        Assert.True(second.Success);

        MasterDocument a = first.Document!;
        MasterDocument b = second.Document!;

        Assert.Equal(a.Items.Count, b.Items.Count);
        Assert.Equal(a.Facilities.Count, b.Facilities.Count);
        Assert.Equal(a.Environments.Count, b.Environments.Count);
        Assert.Equal(a.GameEvents.Count, b.GameEvents.Count);
        Assert.Equal(a.Recipes.Count, b.Recipes.Count);
        Assert.Equal(a.Icons.Count, b.Icons.Count);

        Recipe recipeA = a.Recipes[0];
        Recipe recipeB = b.Recipes[0];
        Assert.Equal(recipeA.Id, recipeB.Id);
        Assert.Equal(recipeA.Facilities.Count, recipeB.Facilities.Count);
        for (int i = 0; i < recipeA.Facilities.Count; i++)
        {
            Assert.Equal(recipeA.Facilities[i].FacilityId, recipeB.Facilities[i].FacilityId);
            Assert.Equal(recipeA.Facilities[i].CycleTime, recipeB.Facilities[i].CycleTime);
            Assert.Equal(recipeA.Facilities[i].EnvironmentId, recipeB.Facilities[i].EnvironmentId);
            Assert.Equal(
                recipeA.Facilities[i].FixedConsumption?.ItemId,
                recipeB.Facilities[i].FixedConsumption?.ItemId);
            Assert.Equal(
                recipeA.Facilities[i].FixedConsumption?.RatePerSecond,
                recipeB.Facilities[i].FixedConsumption?.RatePerSecond);
        }

        for (int i = 0; i < a.Icons.Count; i++)
        {
            Assert.Equal(a.Icons[i].Key, b.Icons[i].Key);
            Assert.Equal(a.Icons[i].File, b.Icons[i].File);
            Assert.Equal(a.Icons[i].Sha256, b.Icons[i].Sha256);
            Assert.Equal(a.Icons[i].Bytes, b.Icons[i].Bytes);
        }
    }

    [Fact(DisplayName = "RND-03: 同梱 master.json が正として読める（Errors 0 件）")]
    public void MasterJson_LoadsClean()
    {
        MasterJsonLoadResult result = MasterJsonLoader.Load(File.ReadAllText(TestJson.MasterJsonPath));

        Assert.True(result.Success, string.Join("\n", result.Errors.Select(e => e.Message)));
        Assert.Empty(result.Errors);
    }
}
