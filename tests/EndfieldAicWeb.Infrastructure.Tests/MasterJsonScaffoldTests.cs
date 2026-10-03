using System.Text.Json;

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
