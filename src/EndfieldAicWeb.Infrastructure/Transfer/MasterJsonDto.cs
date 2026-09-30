using System.Text.Json;
using System.Text.Json.Serialization;

namespace EndfieldAicWeb.Infrastructure.Transfer;

/// <summary>
/// マスタ JSON のトップレベル構造（SchemaVersion=1、docs/requirements.md §5.9）。
/// 読み込み・エクスポートで共有する。
/// スキーマ required 準拠で全プロパティを <c>required</c> とし、キー欠落はデシリアライズ時に拒否する。
/// 値が null かどうかは構造検証（<see cref="MasterJsonReader"/>）で判定するため nullable フィールドとする。
/// </summary>
internal sealed class MasterJsonDocument
{
    public required int? SchemaVersion { get; set; }
    public required string? DataVersion { get; set; }
    public required List<ItemJson?>? Items { get; set; }
    public required List<FacilityJson?>? Facilities { get; set; }
    public required List<EnvironmentJson?>? Environments { get; set; }
    public required List<GameEventJson?>? GameEvents { get; set; }
    public required List<RecipeJson?>? Recipes { get; set; }
    public required List<IconJson?>? Icons { get; set; }

    /// <summary>スキーマ外プロパティの捕捉用（additionalProperties:false 準拠で構造検証が拒否する）。</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>全エンティティの共通属性（仕様決定 N）。</summary>
internal abstract class EntityJson
{
    public required string? Id { get; set; }
    public required string? Name { get; set; }
    public required string? Description { get; set; }
    public required string? IconKey { get; set; }
    public required string? VersionAdded { get; set; }
    public required string? VersionRemoved { get; set; }

    /// <summary>スキーマ外プロパティの捕捉用。</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; set; }
}

internal sealed class ItemJson : EntityJson
{
    public required string? Category { get; set; }
    public required bool? IsGatherable { get; set; }
    public required string? TransportKind { get; set; }
    public required string? GameEventId { get; set; }
}

internal sealed class FacilityJson : EntityJson
{
    public required double? Width { get; set; }
    public required double? Height { get; set; }
    public required double? PowerConsumption { get; set; }
}

internal sealed class EnvironmentJson : EntityJson
{
    public required string? ProviderFacilityId { get; set; }
    public required string? ConsumeItemId { get; set; }
    public required double? ConsumeRatePerMinute { get; set; }
    public required string? GameEventId { get; set; }
}

internal sealed class GameEventJson : EntityJson
{
    public required DateTime? ActiveFrom { get; set; }
    public required DateTime? ActiveTo { get; set; }
}

internal sealed class RecipeJson : EntityJson
{
    public required string? GameEventId { get; set; }
    public required List<RecipeIoJson?>? Inputs { get; set; }
    public required List<RecipeOutputJson?>? Outputs { get; set; }
    public required List<RecipeFacilityJson?>? Facilities { get; set; }
}

internal sealed class RecipeIoJson
{
    public required string? ItemId { get; set; }
    public required double? Quantity { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; set; }
}

internal sealed class RecipeOutputJson
{
    public required string? ItemId { get; set; }
    public required double? Quantity { get; set; }
    public required int? SortOrder { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>ペア（仕様決定 P）。RecipeId は JSON 上に持たず、所属レシピから与えられる。</summary>
internal sealed class RecipeFacilityJson
{
    public required string? FacilityId { get; set; }
    public required double? CycleTime { get; set; }
    public required string? EnvironmentId { get; set; }
    public required FixedConsumptionJson? FixedConsumption { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; set; }
}

internal sealed class FixedConsumptionJson
{
    public required string? ItemId { get; set; }
    public required double? RatePerMinute { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Icons 節の 1 エントリ（仕様決定 R）。File は固定形式 icons/&lt;Key&gt;.png。</summary>
internal sealed class IconJson
{
    public required string? Key { get; set; }
    public required string? File { get; set; }
    public required string? Sha256 { get; set; }
    public required long? Bytes { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; set; }
}
