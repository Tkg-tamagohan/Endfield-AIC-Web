using System.Text.Json.Nodes;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;
using EndfieldAicWeb.Infrastructure.Transfer;

namespace EndfieldAicWeb.Infrastructure.Tests;

/// <summary>
/// MasterExporter・往復の検証テスト。ケース ID は docs/phases/test-specification-phase3.md に対応する。
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

    [Fact(DisplayName = "XPT-09: レシピ内の null 要素（Inputs/Outputs/Facilities）は MasterValidationException で拒否される")]
    public void Export_NullNestedElement_Throws()
    {
        MasterDocument nullInput = TestJson.LoadValidDocument();
        nullInput.Recipes[0].Inputs.Add(null!);
        Assert.Throws<MasterValidationException>(() => MasterExporter.Export(nullInput));

        MasterDocument nullPair = TestJson.LoadValidDocument();
        nullPair.Recipes[0].Facilities.Add(null!);
        Assert.Throws<MasterValidationException>(() => MasterExporter.Export(nullPair));

        MasterDocument nullOutputs = TestJson.LoadValidDocument();
        nullOutputs.Recipes[0].Outputs = null!;
        Assert.Throws<MasterValidationException>(() => MasterExporter.Export(nullOutputs));
    }

    [Fact(DisplayName = "XPT-10: Description=null のドキュメントは例外で拒否される")]
    public void Export_NullDescription_Throws()
    {
        MasterDocument document = TestJson.LoadValidDocument();
        document.Items[0].Description = null!;

        Assert.Throws<MasterValidationException>(() => MasterExporter.Export(document));
    }

    // ---------- RND: 往復（data/master.json を原本として使う） ----------

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
                recipeA.Facilities[i].FixedConsumption?.RatePerMinute,
                recipeB.Facilities[i].FixedConsumption?.RatePerMinute);
        }

        for (int i = 0; i < a.Icons.Count; i++)
        {
            Assert.Equal(a.Icons[i].Key, b.Icons[i].Key);
            Assert.Equal(a.Icons[i].File, b.Icons[i].File);
            Assert.Equal(a.Icons[i].Sha256, b.Icons[i].Sha256);
            Assert.Equal(a.Icons[i].Bytes, b.Icons[i].Bytes);
        }
    }

    [Fact(DisplayName = "XPT-11: SortOrder が負のドキュメントは例外で拒否される（再読み込み不能な出力を防ぐ）")]
    public void Export_NegativeSortOrder_Throws()
    {
        MasterDocument document = TestJson.LoadValidDocument();
        document.Recipes[0].Outputs[0].SortOrder = -1;

        Assert.Throws<MasterValidationException>(() => MasterExporter.Export(document));
    }

    [Fact(DisplayName = "XPT-12: dataVersion 引数は文書の値の代わりに出力され文書を変更しない")]
    public void Export_DataVersionOverride_DoesNotMutateDocument()
    {
        MasterDocument document = TestJson.LoadValidDocument();

        string json = MasterExporter.Export(document, dataVersion: "9.9.9");
        JsonObject root = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("9.9.9", root["DataVersion"]!.GetValue<string>());
        Assert.Equal("1.0.0", document.DataVersion);
    }

    [Fact(DisplayName = "XPT-13: icons 引数は文書の Icons の代わりに検証・出力される")]
    public void Export_IconsOverride_UsedForOutput()
    {
        MasterDocument document = TestJson.LoadValidDocument();
        List<IconEntry> manifest =
        [
            new IconEntry { Key = "icon-alt", File = "icons/icon-alt.png", Sha256 = new string('a', 64), Bytes = 2 },
        ];

        string json = MasterExporter.Export(document, icons: manifest);
        JsonObject root = JsonNode.Parse(json)!.AsObject();

        JsonArray icons = root["Icons"]!.AsArray();
        Assert.Single(icons);
        Assert.Equal("icon-alt", icons[0]!["Key"]!.GetValue<string>());
        Assert.Equal("icon-ore", document.Icons.Single().Key);
    }

    [Fact(DisplayName = "XPT-14: dataVersion 引数が空白なら例外で拒否される")]
    public void Export_BlankDataVersionOverride_Throws()
    {
        MasterDocument document = TestJson.LoadValidDocument();

        Assert.Throws<MasterValidationException>(() => MasterExporter.Export(document, dataVersion: " "));
    }

    [Fact(DisplayName = "RND-03: 同梱 master.json が正として読める（Errors 0 件）")]
    public void MasterJson_LoadsClean()
    {
        MasterJsonLoadResult result = MasterJsonLoader.Load(File.ReadAllText(TestJson.MasterJsonPath));

        Assert.True(result.Success, string.Join("\n", result.Errors.Select(e => e.Message)));
        Assert.Empty(result.Errors);
    }

    // ---------- REN: Phase 9 改称の I/O 契約 ----------

    [Fact(DisplayName = "REN-02: エクスポートは新フィールド名のみ出力する")]
    public void Export_EmitsOnlyNewFieldNames()
    {
        string json = MasterExporter.Export(TestJson.LoadValidDocument());
        JsonObject root = JsonNode.Parse(json)!.AsObject();

        foreach (JsonNode? node in root["Items"]!.AsArray())
        {
            Assert.True(node!.AsObject().ContainsKey("IsGatherable"));
            Assert.False(node.AsObject().ContainsKey("IsBaseMaterial"));
        }

        foreach (JsonNode? node in root["Environments"]!.AsArray())
        {
            Assert.True(node!.AsObject().ContainsKey("ConsumeRatePerMinute"));
            Assert.False(node.AsObject().ContainsKey("ConsumeRatePerSecond"));
        }

        foreach (JsonNode? node in root["Recipes"]!.AsArray())
        {
            foreach (JsonNode? pair in node!.AsObject()["Facilities"]!.AsArray())
            {
                JsonNode? fc = pair!.AsObject()["FixedConsumption"];
                if (fc is not null)
                {
                    Assert.True(fc.AsObject().ContainsKey("RatePerMinute"));
                    Assert.False(fc.AsObject().ContainsKey("RatePerSecond"));
                }
            }
        }
    }
}
