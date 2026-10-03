using System.Text.Json;
using System.Text.RegularExpressions;

namespace EndfieldAicWeb.Infrastructure.Tests;

public class MasterJsonScaffoldTests
{
    // docs/requirements.md §5.10 のルートキー定義が根拠
    private static readonly string[] RequiredRootKeys =
    [
        "SchemaVersion", "DataVersion", "Items", "Facilities",
        "Environments", "GameEvents", "Recipes", "Maps", "Icons",
    ];

    [Fact(DisplayName = "SCAF-02: master.json がスキーマ v1 のルートキーを持つ")]
    public void MasterJsonHasSchemaV1RootKeys()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "master.json");
        Assert.True(File.Exists(path), $"master.json が見つからない: {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        foreach (var key in RequiredRootKeys)
        {
            Assert.True(root.TryGetProperty(key, out _), $"ルートキー {key} がない");
        }
        Assert.Equal(1, root.GetProperty("SchemaVersion").GetInt32());
    }

    [Fact(DisplayName = "MJS-10: スキーマの environment 定義は CoverableMachines を required に持つ")]
    public void EnvironmentSchemaRequiresCoverableMachines()
    {
        string root = FindRepoRoot();
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "data", "master.schema.json")));

        JsonElement env = document.RootElement
            .GetProperty("$defs").GetProperty("environment")
            .GetProperty("allOf")[1];

        Assert.Contains(
            env.GetProperty("required").EnumerateArray().Select(e => e.GetString()),
            name => name == "CoverableMachines");

        JsonElement property = env.GetProperty("properties").GetProperty("CoverableMachines");
        Assert.Equal("integer", property.GetProperty("type").GetString());
        Assert.Equal(0, property.GetProperty("exclusiveMinimum").GetInt32());
    }

    [Fact(DisplayName = "MJS-12: Id 系値フィールドが空白禁止 pattern を持つ（BW）")]
    public void IdFieldsHaveNoWhitespacePattern()
    {
        using var document = LoadSchema();
        JsonElement defs = document.RootElement.GetProperty("$defs");

        // idValue 定義そのものが空白禁止の pattern を持つ。
        JsonElement idValue = defs.GetProperty("idValue");
        Assert.Equal("string", idValue.GetProperty("type").GetString());
        Assert.True(idValue.TryGetProperty("pattern", out _));

        // 必須 ID 系値フィールドは idValue 参照。
        AssertIdValueRef(
            defs.GetProperty("commonAttributes").GetProperty("properties").GetProperty("Id"));
        JsonElement environment = EntityExtension(defs, "environment");
        AssertIdValueRef(environment.GetProperty("ProviderFacilityId"));
        AssertIdValueRef(environment.GetProperty("ConsumeItemId"));
        AssertIdValueRef(
            defs.GetProperty("recipeIo").GetProperty("properties").GetProperty("ItemId"));
        AssertIdValueRef(
            defs.GetProperty("recipeOutput").GetProperty("properties").GetProperty("ItemId"));
        AssertIdValueRef(
            defs.GetProperty("recipeFacility").GetProperty("properties").GetProperty("FacilityId"));
        AssertIdValueRef(
            defs.GetProperty("fixedConsumption").GetProperty("properties").GetProperty("ItemId"));
        AssertIdValueRef(
            defs.GetProperty("gatherRate").GetProperty("properties").GetProperty("ItemId"));

        // null 許容参照フィールドは anyOf[idValue, null]。
        AssertNullableIdValueRef(
            EntityExtension(defs, "item").GetProperty("GameEventId"));
        AssertNullableIdValueRef(environment.GetProperty("GameEventId"));
        AssertNullableIdValueRef(
            EntityExtension(defs, "recipe").GetProperty("GameEventId"));
        AssertNullableIdValueRef(
            EntityExtension(defs, "gameMap").GetProperty("GameEventId"));
        AssertNullableIdValueRef(
            defs.GetProperty("recipeFacility").GetProperty("properties").GetProperty("EnvironmentId"));
    }

    [Fact(DisplayName = "MJS-13: 空白禁止 pattern が空白入り値を拒否する（BW）")]
    public void IdValuePatternRejectsWhitespace()
    {
        using var document = LoadSchema();
        string pattern = document.RootElement
            .GetProperty("$defs").GetProperty("idValue").GetProperty("pattern").GetString()!;
        var regex = new Regex(pattern);

        // 負の検証経路が CI でも機能することの確認: 空白入り 3 値を拒否し fac-x を受理する。
        Assert.DoesNotMatch(regex, " fac-x ");
        Assert.DoesNotMatch(regex, "fac 1");
        Assert.DoesNotMatch(regex, "fac-x\n");
        Assert.Matches(regex, "fac-x");
    }

    /// <summary>data/master.schema.json を開いて返す。</summary>
    private static JsonDocument LoadSchema() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepoRoot(), "data", "master.schema.json")));

    /// <summary>allOf 構成エンティティの拡張プロパティ節（commonAttributes 以外の properties）を返す。</summary>
    private static JsonElement EntityExtension(JsonElement defs, string name) =>
        defs.GetProperty(name).GetProperty("allOf")[1].GetProperty("properties");

    private static void AssertIdValueRef(JsonElement property)
    {
        Assert.True(
            property.TryGetProperty("$ref", out JsonElement reference)
                && reference.GetString() == "#/$defs/idValue",
            $"idValue 参照ではありません: {property}");
    }

    private static void AssertNullableIdValueRef(JsonElement property)
    {
        Assert.True(property.TryGetProperty("anyOf", out JsonElement anyOf), $"anyOf がありません: {property}");
        JsonElement[] options = anyOf.EnumerateArray().ToArray();
        Assert.Equal(2, options.Length);
        Assert.Contains(options, o =>
            o.TryGetProperty("$ref", out JsonElement reference)
                && reference.GetString() == "#/$defs/idValue");
        Assert.Contains(options, o =>
            o.TryGetProperty("type", out JsonElement type) && type.GetString() == "null");
    }

    /// <summary>data/ を持つリポジトリルートを実行ディレクトリから遡って探す。</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "master.schema.json")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("data/master.schema.json を持つリポジトリルートが見つかりません。");
    }
}
