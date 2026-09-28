using System.Text.Json.Nodes;
using EndfieldAicWeb.Infrastructure.Transfer;

namespace EndfieldAicWeb.Infrastructure.Tests;

/// <summary>
/// MasterJsonLoader の検証テスト。ケース ID は docs/test-specification-phase3.md に対応する。
/// </summary>
public class MasterJsonLoaderTests
{
    // ---------- SYN: 構文レベル ----------

    [Theory(DisplayName = "SYN-01: 空文字・空白のみはエラー一覧に集約される（例外なし）")]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyJson_ReturnsErrors(string json)
    {
        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.Null(result.Document);
    }

    [Fact(DisplayName = "SYN-02: 構文が壊れた JSON はエラー一覧に集約される")]
    public void BrokenJson_ReturnsErrors()
    {
        MasterJsonLoadResult result = MasterJsonLoader.Load("{ \"Items\": [");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.Null(result.Document);
    }

    [Fact(DisplayName = "SYN-03: トップレベルが配列の JSON は拒否される")]
    public void ArrayRoot_ReturnsErrors()
    {
        MasterJsonLoadResult result = MasterJsonLoader.Load("[]");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.Null(result.Document);
    }

    [Theory(DisplayName = "SYN-04: 型不一致（文字列の SchemaVersion・Width）は拒否される")]
    [InlineData("SchemaVersion", "one")]
    [InlineData("Width", "abc")]
    public void WrongType_ReturnsErrors(string path, string wrongValue)
    {
        string json = TestJson.Mutate(root =>
        {
            if (path == "SchemaVersion")
            {
                root["SchemaVersion"] = wrongValue;
            }
            else
            {
                root["Facilities"]!.AsArray()[0]!.AsObject()["Width"] = wrongValue;
            }
        });

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    // ---------- STR: 構造検証 ----------

    [Fact(DisplayName = "STR-01: SchemaVersion 欠落はエラー")]
    public void MissingSchemaVersion_ReturnsError()
    {
        string json = TestJson.Mutate(root => root.Remove("SchemaVersion"));

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Field == "SchemaVersion");
    }

    [Theory(DisplayName = "STR-02: 未知 SchemaVersion (2, 0, 999) はそれぞれ拒否される")]
    [InlineData(2)]
    [InlineData(0)]
    [InlineData(999)]
    public void UnknownSchemaVersion_ReturnsError(int version)
    {
        string json = TestJson.Mutate(root => root["SchemaVersion"] = version);

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Field == "SchemaVersion");
    }

    [Theory(DisplayName = "STR-03: DataVersion 欠落・空文字はそれぞれエラー")]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidDataVersion_ReturnsError(bool remove)
    {
        string json = TestJson.Mutate(root =>
        {
            if (remove)
            {
                root.Remove("DataVersion");
            }
            else
            {
                root["DataVersion"] = "";
            }
        });

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Field == "DataVersion");
    }

    [Theory(DisplayName = "STR-04: 必須配列の欠落（Items・Icons）はそれぞれエラー")]
    [InlineData("Items")]
    [InlineData("Icons")]
    public void MissingArray_ReturnsError(string name)
    {
        string json = TestJson.Mutate(root => root.Remove(name));

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains(name));
    }

    [Fact(DisplayName = "STR-05: 配列の null 要素はエラー")]
    public void NullElement_ReturnsError()
    {
        string json = TestJson.Mutate(root => root["Items"] = new JsonArray((JsonNode?)null));

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("null"));
    }

    [Fact(DisplayName = "STR-06: Id 欠落・Name 空はそれぞれエラー")]
    public void MissingIdOrEmptyName_ReturnsError()
    {
        string missingId = TestJson.Mutate(root =>
            root["Items"]!.AsArray()[0]!.AsObject().Remove("Id"));
        string emptyName = TestJson.Mutate(root =>
            root["Items"]!.AsArray()[0]!.AsObject()["Name"] = "");

        MasterJsonLoadResult missingIdResult = MasterJsonLoader.Load(missingId);
        MasterJsonLoadResult emptyNameResult = MasterJsonLoader.Load(emptyName);

        Assert.False(missingIdResult.Success);
        Assert.Contains(missingIdResult.Errors, e => e.Field is not null && e.Field.Contains("Id"));
        Assert.False(emptyNameResult.Success);
        Assert.Contains(emptyNameResult.Errors, e => e.Field is not null && e.Field.Contains("Name"));
    }

    [Theory(DisplayName = "STR-07: enum 定義値外（Rocket・小文字 belt）はそれぞれエラー")]
    [InlineData("Rocket")]
    [InlineData("belt")]
    public void InvalidEnum_ReturnsError(string value)
    {
        string json = TestJson.Mutate(root =>
            root["Items"]!.AsArray()[0]!.AsObject()["TransportKind"] = value);

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Field is not null && e.Field.Contains("TransportKind"));
    }

    [Theory(DisplayName = "STR-08: 必須フィールド欠落はそれぞれエラー")]
    [InlineData("Width")]
    [InlineData("CycleTime")]
    [InlineData("IsBaseMaterial")]
    [InlineData("ConsumeRatePerSecond")]
    [InlineData("SortOrder")]
    public void MissingRequiredField_ReturnsError(string field)
    {
        string json = TestJson.Mutate(root =>
        {
            switch (field)
            {
                case "Width":
                    root["Facilities"]!.AsArray()[0]!.AsObject().Remove("Width");
                    break;
                case "CycleTime":
                    root["Recipes"]!.AsArray()[0]!.AsObject()["Facilities"]!
                        .AsArray()[0]!.AsObject().Remove("CycleTime");
                    break;
                case "IsBaseMaterial":
                    root["Items"]!.AsArray()[0]!.AsObject().Remove("IsBaseMaterial");
                    break;
                case "ConsumeRatePerSecond":
                    root["Environments"]!.AsArray()[0]!.AsObject().Remove("ConsumeRatePerSecond");
                    break;
                case "SortOrder":
                    root["Recipes"]!.AsArray()[0]!.AsObject()["Outputs"]!
                        .AsArray()[0]!.AsObject().Remove("SortOrder");
                    break;
            }
        });

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains(field));
    }

    [Fact(DisplayName = "STR-09: SortOrder が負ならエラー")]
    public void NegativeSortOrder_ReturnsError()
    {
        string json = TestJson.Mutate(root =>
            root["Recipes"]!.AsArray()[0]!.AsObject()["Outputs"]!
                .AsArray()[0]!.AsObject()["SortOrder"] = -1);

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("SortOrder"));
    }

    [Theory(DisplayName = "STR-10: FixedConsumption の ItemId・RatePerSecond 欠落はそれぞれエラー")]
    [InlineData("ItemId")]
    [InlineData("RatePerSecond")]
    public void MissingFixedConsumptionField_ReturnsError(string field)
    {
        string json = TestJson.Mutate(root =>
            root["Recipes"]!.AsArray()[0]!.AsObject()["Facilities"]!
                .AsArray()[1]!.AsObject()["FixedConsumption"]!
                .AsObject().Remove(field));

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains(field));
    }

    public static IEnumerable<object[]> IconStructuralViolations()
    {
        yield return ["Key重複", (Action<JsonObject>)(root =>
            root["Icons"]!.AsArray().Add(root["Icons"]!.AsArray()[0]!.DeepClone()))];
        yield return ["Key文字種違反", (Action<JsonObject>)(root =>
            root["Icons"]!.AsArray()[0]!.AsObject()["Key"] = "icon ore!")];
        yield return ["File不一致", (Action<JsonObject>)(root =>
            root["Icons"]!.AsArray()[0]!.AsObject()["File"] = "other/icon-ore.png")];
        yield return ["Sha256桁不足", (Action<JsonObject>)(root =>
            root["Icons"]!.AsArray()[0]!.AsObject()["Sha256"] = "abc123")];
        yield return ["Sha256大文字", (Action<JsonObject>)(root =>
            root["Icons"]!.AsArray()[0]!.AsObject()["Sha256"] = TestJson.IconOreSha256.ToUpperInvariant())];
        yield return ["Bytesが0", (Action<JsonObject>)(root =>
            root["Icons"]!.AsArray()[0]!.AsObject()["Bytes"] = 0)];
        yield return ["Key欠落", (Action<JsonObject>)(root =>
            root["Icons"]!.AsArray()[0]!.AsObject().Remove("Key"))];
    }

    [Theory(DisplayName = "STR-11: Icons 節の構造違反はそれぞれエラー")]
    [MemberData(nameof(IconStructuralViolations), DisableDiscoveryEnumeration = true)]
    public void InvalidIconEntry_ReturnsError(string label, Action<JsonObject> mutate)
    {
        string json = TestJson.Mutate(mutate);

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success, $"症例 {label} が受理されてしまった");
        Assert.NotEmpty(result.Errors);
    }

    [Fact(DisplayName = "STR-12: 正当な最小 JSON は Success=true・Document 非 null・Errors 0 件")]
    public void ValidJson_Succeeds()
    {
        MasterJsonLoadResult result = MasterJsonLoader.Load(TestJson.ValidJson());

        Assert.True(result.Success, string.Join("\n", result.Errors.Select(e => e.Message)));
        Assert.NotNull(result.Document);
        Assert.Empty(result.Errors);
        Assert.Equal(4, result.Document.Items.Count);
        Assert.Equal(2, result.Document.Facilities.Count);
        Assert.Single(result.Document.Environments);
        Assert.Single(result.Document.GameEvents);
        Assert.Single(result.Document.Recipes);
        Assert.Single(result.Document.Icons);
        Assert.Equal(2, result.Document.Recipes[0].Facilities.Count);
        Assert.Equal("r-part", result.Document.Recipes[0].Facilities[0].RecipeId);
    }

    // ---------- SEM: 意味検証（MasterValidator 同一規則） ----------

    [Fact(DisplayName = "SEM-01: 参照不整合はすべてエラー一覧に集約される")]
    public void BrokenReferences_Aggregated()
    {
        string json = TestJson.Mutate(root =>
        {
            JsonObject recipe = root["Recipes"]!.AsArray()[0]!.AsObject();
            recipe["Inputs"]!.AsArray()[0]!.AsObject()["ItemId"] = "i-ghost";
            recipe["GameEventId"] = "ev-ghost";

            JsonObject pair = recipe["Facilities"]!.AsArray()[1]!.AsObject();
            pair["FacilityId"] = "f-ghost";
            pair["EnvironmentId"] = "env-ghost";
            pair["FixedConsumption"]!.AsObject()["ItemId"] = "i-ghost2";

            JsonObject environment = root["Environments"]!.AsArray()[0]!.AsObject();
            environment["ProviderFacilityId"] = "f-ghost";
            environment["ConsumeItemId"] = "i-ghost2";
            environment["GameEventId"] = "ev-ghost";

            root["Items"]!.AsArray()[0]!.AsObject()["GameEventId"] = "ev-ghost";
        });

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
        // 入力・ペア・環境・イベント参照の 7 箇所すべてが報告される（最初の 1 件で止まらない）。
        Assert.True(result.Errors.Count >= 7, string.Join("\n", result.Errors.Select(e => e.Message)));
        Assert.NotNull(result.Document);
    }

    [Fact(DisplayName = "SEM-02: 同一レシピ内のペア重複はエラー")]
    public void DuplicatePair_ReturnsError()
    {
        string json = TestJson.Mutate(root =>
        {
            JsonArray facilities = root["Recipes"]!.AsArray()[0]!.AsObject()["Facilities"]!.AsArray();
            facilities.Add(facilities[0]!.DeepClone());
        });

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("重複"));
    }

    [Theory(DisplayName = "SEM-03: 範囲外数値はそれぞれエラー")]
    [InlineData("CycleTime", 0.0)]
    [InlineData("Width", -1.0)]
    [InlineData("Quantity", 0.0)]
    [InlineData("ConsumeRatePerSecond", 0.0)]
    [InlineData("RatePerSecond", -0.5)]
    [InlineData("PowerConsumption", -1.0)]
    public void OutOfRangeNumber_ReturnsError(string field, double value)
    {
        string json = TestJson.Mutate(root =>
        {
            switch (field)
            {
                case "CycleTime":
                    root["Recipes"]!.AsArray()[0]!.AsObject()["Facilities"]!
                        .AsArray()[0]!.AsObject()["CycleTime"] = value;
                    break;
                case "Width":
                    root["Facilities"]!.AsArray()[0]!.AsObject()["Width"] = value;
                    break;
                case "Quantity":
                    root["Recipes"]!.AsArray()[0]!.AsObject()["Inputs"]!
                        .AsArray()[0]!.AsObject()["Quantity"] = value;
                    break;
                case "ConsumeRatePerSecond":
                    root["Environments"]!.AsArray()[0]!.AsObject()["ConsumeRatePerSecond"] = value;
                    break;
                case "RatePerSecond":
                    root["Recipes"]!.AsArray()[0]!.AsObject()["Facilities"]!
                        .AsArray()[1]!.AsObject()["FixedConsumption"]!
                        .AsObject()["RatePerSecond"] = value;
                    break;
                case "PowerConsumption":
                    root["Facilities"]!.AsArray()[0]!.AsObject()["PowerConsumption"] = value;
                    break;
            }
        });

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Field is not null && e.Field.Contains(field));
    }

    [Fact(DisplayName = "SEM-04: Item Id の重複はエラー")]
    public void DuplicateItemId_ReturnsError()
    {
        string json = TestJson.Mutate(root =>
            root["Items"]!.AsArray().Add(root["Items"]!.AsArray()[0]!.DeepClone()));

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Message.Contains("重複"));
    }

    [Fact(DisplayName = "SEM-05: 仮想アイテム（TransportKind=None）を入力に使うとエラー")]
    public void VirtualItemInput_ReturnsError()
    {
        string json = TestJson.Mutate(root =>
            root["Recipes"]!.AsArray()[0]!.AsObject()["Inputs"]!
                .AsArray().Add(new JsonObject { ["ItemId"] = "i-power", ["Quantity"] = 1.0 }));

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.EntityKind == "Recipe");
    }

    [Fact(DisplayName = "SEM-06: 意味違反は例外ではなく Errors に集約される")]
    public void SemanticViolation_NoException()
    {
        string json = TestJson.Mutate(root =>
        {
            JsonArray facilities = root["Recipes"]!.AsArray()[0]!.AsObject()["Facilities"]!.AsArray();
            facilities.Add(facilities[0]!.DeepClone());
        });

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.NotNull(result.Document);
    }
}
