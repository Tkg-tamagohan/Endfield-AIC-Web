using System.Text.Json;

namespace EndfieldAicWeb.Infrastructure.Tests;

public class MasterJsonScaffoldTests
{
    // docs/requirements.md §5.9 のルートキー定義が根拠
    private static readonly string[] RequiredRootKeys =
    [
        "SchemaVersion", "DataVersion", "Items", "Facilities",
        "Environments", "GameEvents", "Recipes", "Icons",
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
}
