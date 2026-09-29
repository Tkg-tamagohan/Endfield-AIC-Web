using System.Security.Cryptography;
using System.Text.Json.Nodes;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Infrastructure.Transfer;
using Xunit;

namespace EndfieldAicWeb.Infrastructure.Tests;

/// <summary>
/// テスト仕様書 docs/phases/test-specification-phase3.md の J-01（最小有効 JSON）フィクスチャ。
/// JsonNode で構築し、各ケースは Mutate で構造を変化させる。
/// </summary>
internal static class TestJson
{
    /// <summary>J-01 の icon-ore エントリが指す実ファイル内容（PNG 形式である必要はない）。</summary>
    public static readonly byte[] IconOreContent = BuildIconOreContent();

    public static readonly string IconOreSha256 =
        Convert.ToHexString(SHA256.HashData(IconOreContent)).ToLowerInvariant();

    /// <summary>保存先マスタファイル master.json（出力ディレクトリへコピー済み）。</summary>
    public static readonly string MasterJsonPath =
        Path.Combine(AppContext.BaseDirectory, "master.json");

    /// <summary>J-01 相当の最小有効 JSON を返す。</summary>
    public static string ValidJson() => BuildValidDocument().ToJsonString();

    /// <summary>J-01 を変化させて返す。</summary>
    public static string Mutate(Action<JsonObject> edit)
    {
        JsonObject document = BuildValidDocument();
        edit(document);
        return document.ToJsonString();
    }

    /// <summary>J-01 を読み込んだ <see cref="MasterDocument"/> を返す（エクスポート系テスト用）。</summary>
    public static MasterDocument LoadValidDocument()
    {
        MasterJsonLoadResult result = MasterJsonLoader.Load(ValidJson());
        Assert.True(result.Success, string.Join("\n", result.Errors.Select(e => e.Message)));
        Assert.NotNull(result.Document);
        return result.Document;
    }

    private static JsonObject BuildValidDocument()
    {
        return new JsonObject
        {
            ["SchemaVersion"] = 1,
            ["DataVersion"] = "1.0.0",
            ["Items"] = new JsonArray(
                Entity("i-ore", "原鉱石").CloneWith(new
                {
                    Category = "基礎素材",
                    IsBaseMaterial = true,
                    TransportKind = "Belt",
                    GameEventId = (string?)null,
                }),
                Entity("i-part", "汎用部品").CloneWith(new
                {
                    Category = "部品",
                    IsBaseMaterial = false,
                    TransportKind = "Belt",
                    GameEventId = (string?)null,
                }),
                Entity("i-gas", "活性ガス").CloneWith(new
                {
                    Category = "基礎素材",
                    IsBaseMaterial = true,
                    TransportKind = "Pipe",
                    GameEventId = (string?)null,
                }),
                Entity("i-power", "電力").CloneWith(new
                {
                    Category = "エネルギー",
                    IsBaseMaterial = false,
                    TransportKind = "None",
                    GameEventId = (string?)null,
                })),
            ["Facilities"] = new JsonArray(
                Entity("f-asm", "加工機").CloneWith(new
                {
                    Width = 3.0,
                    Height = 3.0,
                    PowerConsumption = 50.0,
                }),
                Entity("f-disp", "ガス散布機").CloneWith(new
                {
                    Width = 2.0,
                    Height = 2.0,
                    PowerConsumption = 20.0,
                })),
            ["Environments"] = new JsonArray(
                Entity("env-gas", "ガス環境").CloneWith(new
                {
                    ProviderFacilityId = "f-disp",
                    ConsumeItemId = "i-gas",
                    ConsumeRatePerSecond = 6.0,
                    GameEventId = (string?)null,
                })),
            ["GameEvents"] = new JsonArray(
                Entity("ev-first", "初回開放イベント").CloneWith(new
                {
                    ActiveFrom = (string?)null,
                    ActiveTo = (string?)null,
                })),
            ["Recipes"] = new JsonArray(
                Entity("r-part", "汎用部品レシピ").CloneWith(new
                {
                    GameEventId = (string?)null,
                }).CloneWithRecipe()),
            ["Icons"] = new JsonArray(
                new JsonObject
                {
                    ["Key"] = "icon-ore",
                    ["File"] = "icons/icon-ore.png",
                    ["Sha256"] = IconOreSha256,
                    ["Bytes"] = IconOreContent.LongLength,
                }),
        };
    }

    /// <summary>6 基本フィールドを持つエンティティオブジェクトを返す（Description は空文字）。</summary>
    private static JsonObject Entity(string id, string name)
    {
        var entity = new JsonObject
        {
            ["Id"] = id,
            ["Name"] = name,
            ["Description"] = "",
            ["IconKey"] = null,
            ["VersionAdded"] = "1.0.0",
            ["VersionRemoved"] = null,
        };
        return entity;
    }

    private static byte[] BuildIconOreContent()
    {
        var bytes = new byte[32];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(0xA0 + i);
        }

        return bytes;
    }
}

internal static class TestJsonExtensions
{
    /// <summary>匿名型の各プロパティをオブジェクトへ追加して返す。</summary>
    public static JsonObject CloneWith(this JsonObject entity, object fields)
    {
        foreach (var property in fields.GetType().GetProperties())
        {
            object? value = property.GetValue(fields);
            entity[property.Name] = value switch
            {
                null => null,
                string s => s,
                bool b => b,
                double d => d,
                int i => i,
                _ => throw new InvalidOperationException($"未対応のフィールド型: {property.PropertyType}"),
            };
        }

        return entity;
    }

    /// <summary>レシピの入出力・設備ペア節を追加して返す。</summary>
    public static JsonObject CloneWithRecipe(this JsonObject recipe)
    {
        recipe["Inputs"] = new JsonArray(
            new JsonObject { ["ItemId"] = "i-ore", ["Quantity"] = 2.0 });
        recipe["Outputs"] = new JsonArray(
            new JsonObject { ["ItemId"] = "i-part", ["Quantity"] = 1.0, ["SortOrder"] = 0 });
        recipe["Facilities"] = new JsonArray(
            new JsonObject
            {
                ["FacilityId"] = "f-asm",
                ["CycleTime"] = 4.0,
                ["EnvironmentId"] = null,
                ["FixedConsumption"] = null,
            },
            new JsonObject
            {
                ["FacilityId"] = "f-asm",
                ["CycleTime"] = 3.0,
                ["EnvironmentId"] = "env-gas",
                ["FixedConsumption"] = new JsonObject
                {
                    ["ItemId"] = "i-gas",
                    ["RatePerSecond"] = 0.5,
                },
            });
        return recipe;
    }
}
