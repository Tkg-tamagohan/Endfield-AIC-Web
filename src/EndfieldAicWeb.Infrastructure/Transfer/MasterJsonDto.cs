namespace EndfieldAicWeb.Infrastructure.Transfer;

/// <summary>
/// マスタ JSON のトップレベル構造（SchemaVersion=1、requirements §5.9）。
/// 読み込み・エクスポートで共有する。デシリアライズ用に nullable フィールドとし、
/// 欠落フィールドは構造検証（<see cref="MasterJsonReader"/>）で検出する。
/// </summary>
internal sealed class MasterJsonDocument
{
    public int? SchemaVersion { get; set; }
    public string? DataVersion { get; set; }
    public List<ItemJson?>? Items { get; set; }
    public List<FacilityJson?>? Facilities { get; set; }
    public List<EnvironmentJson?>? Environments { get; set; }
    public List<GameEventJson?>? GameEvents { get; set; }
    public List<RecipeJson?>? Recipes { get; set; }
    public List<IconJson?>? Icons { get; set; }
}

/// <summary>全エンティティの共通属性（仕様決定 N）。</summary>
internal abstract class EntityJson
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? IconKey { get; set; }
    public string? VersionAdded { get; set; }
    public string? VersionRemoved { get; set; }
}

internal sealed class ItemJson : EntityJson
{
    public string? Category { get; set; }
    public bool? IsBaseMaterial { get; set; }
    public string? TransportKind { get; set; }
    public string? GameEventId { get; set; }
}

internal sealed class FacilityJson : EntityJson
{
    public double? Width { get; set; }
    public double? Height { get; set; }
    public double? PowerConsumption { get; set; }
}

internal sealed class EnvironmentJson : EntityJson
{
    public string? ProviderFacilityId { get; set; }
    public string? ConsumeItemId { get; set; }
    public double? ConsumeRatePerSecond { get; set; }
    public string? GameEventId { get; set; }
}

internal sealed class GameEventJson : EntityJson
{
    public DateTime? ActiveFrom { get; set; }
    public DateTime? ActiveTo { get; set; }
}

internal sealed class RecipeJson : EntityJson
{
    public string? GameEventId { get; set; }
    public List<RecipeIoJson?>? Inputs { get; set; }
    public List<RecipeOutputJson?>? Outputs { get; set; }
    public List<RecipeFacilityJson?>? Facilities { get; set; }
}

internal sealed class RecipeIoJson
{
    public string? ItemId { get; set; }
    public double? Quantity { get; set; }
}

internal sealed class RecipeOutputJson
{
    public string? ItemId { get; set; }
    public double? Quantity { get; set; }
    public int? SortOrder { get; set; }
}

/// <summary>ペア（仕様決定 P）。RecipeId は JSON 上に持たず、所属レシピから与えられる。</summary>
internal sealed class RecipeFacilityJson
{
    public string? FacilityId { get; set; }
    public double? CycleTime { get; set; }
    public string? EnvironmentId { get; set; }
    public FixedConsumptionJson? FixedConsumption { get; set; }
}

internal sealed class FixedConsumptionJson
{
    public string? ItemId { get; set; }
    public double? RatePerSecond { get; set; }
}

/// <summary>Icons 節の 1 エントリ（仕様決定 R）。File は固定形式 icons/&lt;Key&gt;.png。</summary>
internal sealed class IconJson
{
    public string? Key { get; set; }
    public string? File { get; set; }
    public string? Sha256 { get; set; }
    public long? Bytes { get; set; }
}
