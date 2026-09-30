using EndfieldAicWeb.Domain.Models;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Application;

/// <summary>
/// 管理ツールの「新規追加」で使う、検証を通る既定値入りのエンティティ生成。
/// 参照項目（Environment の設備・アイテム、Recipe の出力・ペア設備）は呼び出し側が
/// 文書内の既知 Id を渡す。候補がない場合は必須違反として検証に残る値を入れる。
/// </summary>
public static class EntityFactory
{
    /// <summary>種別ごとの新規 Id 接頭辞。</summary>
    public static string SuggestId(IEnumerable<string> existingIds, string prefix)
    {
        var taken = new HashSet<string>(existingIds, StringComparer.Ordinal);
        for (int i = 1; ; i++)
        {
            string candidate = $"{prefix}-{i:000}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    public static Item NewItem(string id, string versionAdded) => new()
    {
        Id = id,
        Name = "新規アイテム",
        Description = "",
        IconKey = null,
        VersionAdded = versionAdded,
        VersionRemoved = null,
        Category = "一般",
        IsGatherable = false,
        TransportKind = TransportKind.Belt,
        GameEventId = null,
    };

    public static Facility NewFacility(string id, string versionAdded) => new()
    {
        Id = id,
        Name = "新規設備",
        Description = "",
        IconKey = null,
        VersionAdded = versionAdded,
        VersionRemoved = null,
        Width = 1,
        Height = 1,
        PowerConsumption = 0,
    };

    public static Environment NewEnvironment(
        string id,
        string versionAdded,
        string? providerFacilityId,
        string? consumeItemId) => new()
    {
        Id = id,
        Name = "新規環境",
        Description = "",
        IconKey = null,
        VersionAdded = versionAdded,
        VersionRemoved = null,
        ProviderFacilityId = providerFacilityId ?? "",
        ConsumeItemId = consumeItemId ?? "",
        ConsumeRatePerMinute = 60,
        GameEventId = null,
    };

    public static GameEvent NewGameEvent(string id, string versionAdded) => new()
    {
        Id = id,
        Name = "新規イベント",
        Description = "",
        IconKey = null,
        VersionAdded = versionAdded,
        VersionRemoved = null,
        ActiveFrom = null,
        ActiveTo = null,
    };

    /// <summary>Outputs と Facilities は各 1 件必要なため、候補があれば 1 行ずつ入れて返す。</summary>
    public static Recipe NewRecipe(
        string id,
        string versionAdded,
        string? outputItemId,
        string? facilityId) => new()
    {
        Id = id,
        Name = "新規レシピ",
        Description = "",
        IconKey = null,
        VersionAdded = versionAdded,
        VersionRemoved = null,
        GameEventId = null,
        Inputs = [],
        Outputs = outputItemId is null
            ? []
            : [new RecipeOutput { ItemId = outputItemId, Quantity = 1, SortOrder = 0 }],
        Facilities = facilityId is null
            ? []
            : [new RecipeFacility { RecipeId = id, FacilityId = facilityId, CycleTime = 4 }],
    };
}
