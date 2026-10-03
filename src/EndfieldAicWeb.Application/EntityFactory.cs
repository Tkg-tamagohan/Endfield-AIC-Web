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
    /// <summary>新規エンティティの VersionAdded 既定値（仕様決定 AV）。実装されたゲームバージョンなのでデータ版とは別に固定する。</summary>
    public const string DefaultVersionAdded = "1.0.0";
    /// <summary><see cref="NewRecipe"/> が置く仮の名前。未編集のプレースホルダ判定に使う。</summary>
    public const string PlaceholderRecipeName = "新規レシピ";
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

    /// <summary><see cref="SuggestId"/> が採番したレシピの仮 Id（<c>recipe-NNN</c>）かを判定する。</summary>
    public static bool IsPlaceholderRecipeId(string id) =>
        id.StartsWith("recipe-", StringComparison.Ordinal)
        && id["recipe-".Length..] is { Length: >= 3 } suffix
        && suffix.All(char.IsDigit);

    /// <summary>ItemId からスラッグ部を取る。先頭の <c>item-</c> を除き、始まらない ItemId は全体を使う。</summary>
    public static string ItemSlug(string itemId) =>
        itemId.StartsWith("item-", StringComparison.Ordinal) ? itemId["item-".Length..] : itemId;

    /// <summary>
    /// レシピの新規 Id 提案（仕様決定 BU）。<c>recipe-&lt;slug&gt;</c> を返し、
    /// 既存レシピと衝突するときのみ &lt;slug&gt; に 2 桁連番（01 から）を付けて最初の空きを採番する。
    /// </summary>
    public static string SuggestRecipeId(IEnumerable<string> existingIds, string itemId)
    {
        var taken = new HashSet<string>(existingIds, StringComparer.Ordinal);
        string baseId = $"recipe-{ItemSlug(itemId)}";
        if (!taken.Contains(baseId))
        {
            return baseId;
        }

        for (int i = 1; ; i++)
        {
            string candidate = $"{baseId}{i:00}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// レシピの主産物（<see cref="Recipe.Outputs"/> の SortOrder 最小行。同率は先頭行）のアイテム。
    /// Outputs が空、または主産物の ItemId が文書のアイテムに存在しないときは null。
    /// </summary>
    public static Item? MainProductItem(MasterDocument doc, Recipe recipe)
    {
        RecipeOutput? main = recipe.Outputs.OrderBy(o => o.SortOrder).FirstOrDefault();
        return main is null ? null : doc.Items.FirstOrDefault(i => i.Id == main.ItemId);
    }

    /// <summary>レシピ名の提案値（主産物アイテムの名前。仕様決定 BU）。主産物を解決できないとき null。</summary>
    public static string? SuggestRecipeName(MasterDocument doc, Recipe recipe) =>
        MainProductItem(doc, recipe)?.Name;

    public static Item NewItem(string id, string versionAdded = DefaultVersionAdded) => new()
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

    public static Facility NewFacility(string id, string versionAdded = DefaultVersionAdded) => new()
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
        string versionAdded = DefaultVersionAdded,
        string? providerFacilityId = null,
        string? consumeItemId = null) => new()
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
        // 現行のガス散布機の実測値に揃える暫定値（仕様決定 BP、implementation-plan-phase26 §3.1）。
        CoverableMachines = 4,
        GameEventId = null,
    };

    public static GameEvent NewGameEvent(string id, string versionAdded = DefaultVersionAdded) => new()
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

    public static GameMap NewGameMap(string id, string versionAdded = DefaultVersionAdded) => new()
    {
        Id = id,
        Name = "新規マップ",
        Description = "",
        IconKey = null,
        VersionAdded = versionAdded,
        VersionRemoved = null,
        GatherRates = [],
        GameEventId = null,
    };

    /// <summary>Outputs と Facilities は各 1 件必要なため、候補があれば 1 行ずつ入れて返す。</summary>
    public static Recipe NewRecipe(
        string id,
        string versionAdded = DefaultVersionAdded,
        string? outputItemId = null,
        string? facilityId = null) => new()
    {
        Id = id,
        Name = PlaceholderRecipeName,
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
            : [new RecipeFacility { RecipeId = id, FacilityId = facilityId, CycleTime = 2 }],
    };
}
