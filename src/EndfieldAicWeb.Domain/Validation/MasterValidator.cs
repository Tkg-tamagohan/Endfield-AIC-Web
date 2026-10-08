using EndfieldAicWeb.Domain.Models;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Domain.Validation;

/// <summary>
/// マスタデータの値域と意味レベルの検証。JSON・画面入力を問わず同一規則を適用する。
/// 違反は例外ではなく <see cref="MasterValidationError"/> の一覧として集約して返す。
/// </summary>
public static partial class MasterValidator
{
    private static void ValidateRecipeItems<T>(
        IReadOnlyList<T>? items,
        Func<T, string?> itemIdOf,
        Func<T, double> quantityOf,
        string recipeId,
        string memberName,
        ICollection<MasterValidationError> errors,
        EntityDisplay display)
    {
        if (items is null)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < items.Count; i++)
        {
            string? itemId = itemIdOf(items[i]);
            double quantity = quantityOf(items[i]);

            string name = $"{memberName}[{i}]";
            if (string.IsNullOrWhiteSpace(itemId))
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipeId, name, $"{name}.ItemId は必須です。"));
            }
            else
            {
                RequireNoWhitespace(itemId, "Recipe", recipeId, $"{name}.ItemId", errors);
                if (!seen.Add(itemId))
                {
                    errors.Add(new MasterValidationError(
                        "Recipe", recipeId, memberName,
                        $"{memberName} に同一アイテムが重複しています: {display.Item(itemId)}"));
                }
            }

            if (!double.IsFinite(quantity) || quantity <= 0)
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipeId, $"{name}.Quantity",
                    $"{name}.Quantity は 0 より大きい有限値である必要があります: {quantity}"));
            }
        }
    }

    /// <summary>
    /// 和集合の全体を対象に、単体規則・参照整合性・仮想アイテム規則・ペア一意性を検査する。
    /// </summary>
    public static void ValidateAll(
        IReadOnlyList<Item> items,
        IReadOnlyList<Facility> facilities,
        IReadOnlyList<Environment> environments,
        IReadOnlyList<GameEvent> gameEvents,
        IReadOnlyList<Recipe> recipes,
        IReadOnlyList<GameMap> maps,
        ICollection<MasterValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(facilities);
        ArgumentNullException.ThrowIfNull(environments);
        ArgumentNullException.ThrowIfNull(gameEvents);
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(errors);

        var display = new EntityDisplay(items, facilities, environments, recipes, maps, gameEvents);

        foreach (Item item in items)
        {
            ValidateItem(item, errors, display);
        }

        foreach (Facility facility in facilities)
        {
            ValidateFacility(facility, errors, display);
        }

        foreach (Environment environment in environments)
        {
            ValidateEnvironment(environment, errors, display);
        }

        foreach (GameEvent gameEvent in gameEvents)
        {
            ValidateGameEvent(gameEvent, errors, display);
        }

        foreach (Recipe recipe in recipes)
        {
            ValidateRecipe(recipe, errors, display);
        }

        foreach (GameMap map in maps)
        {
            ValidateGameMap(map, errors, display);
        }

        EnsureUniqueIds(items.Select(i => i.Id), "Items", display, errors);
        EnsureUniqueIds(facilities.Select(f => f.Id), "Facilities", display, errors);
        EnsureUniqueIds(environments.Select(e => e.Id), "Environments", display, errors);
        EnsureUniqueIds(gameEvents.Select(e => e.Id), "GameEvents", display, errors);
        EnsureUniqueIds(recipes.Select(r => r.Id), "Recipes", display, errors);
        EnsureUniqueIds(maps.Select(m => m.Id), "Maps", display, errors);

        var itemsById = new Dictionary<string, Item>(StringComparer.Ordinal);
        var facilityIds = new HashSet<string>(StringComparer.Ordinal);
        var environmentIds = new HashSet<string>(StringComparer.Ordinal);
        var gameEventIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (Item item in items)
        {
            if (!string.IsNullOrEmpty(item.Id))
            {
                itemsById[item.Id] = item;
            }
        }

        foreach (Facility facility in facilities)
        {
            if (!string.IsNullOrEmpty(facility.Id))
            {
                facilityIds.Add(facility.Id);
            }
        }

        foreach (Environment environment in environments)
        {
            if (!string.IsNullOrEmpty(environment.Id))
            {
                environmentIds.Add(environment.Id);
            }
        }

        foreach (GameEvent gameEvent in gameEvents)
        {
            if (!string.IsNullOrEmpty(gameEvent.Id))
            {
                gameEventIds.Add(gameEvent.Id);
            }
        }

        foreach (Item item in items)
        {
            CheckGameEventRef("Item", item.Id, item.GameEventId, gameEventIds, display, errors);
        }

        foreach (Environment environment in environments)
        {
            if (!string.IsNullOrEmpty(environment.ProviderFacilityId)
                && !facilityIds.Contains(environment.ProviderFacilityId))
            {
                errors.Add(new MasterValidationError(
                    "Environment", environment.Id, "ProviderFacilityId",
                    $"ProviderFacilityId が参照する設備が存在しません: {display.Facility(environment.ProviderFacilityId)}"));
            }

            if (!string.IsNullOrEmpty(environment.ConsumeItemId)
                && !itemsById.ContainsKey(environment.ConsumeItemId))
            {
                errors.Add(new MasterValidationError(
                    "Environment", environment.Id, "ConsumeItemId",
                    $"ConsumeItemId が参照するアイテムが存在しません: {display.Item(environment.ConsumeItemId)}"));
            }

            CheckGameEventRef("Environment", environment.Id, environment.GameEventId, gameEventIds, display, errors);
        }

        foreach (Recipe recipe in recipes)
        {
            ValidateRecipeInContext(recipe, itemsById, facilityIds, environmentIds, gameEventIds, display, errors);
        }

        foreach (GameMap map in maps)
        {
            ValidateGameMapInContext(map, itemsById, gameEventIds, display, errors);
        }
    }

    /// <summary>
    /// 共通属性（仕様決定 N）のフィールド内規則: Id・Name 必須、IconKey 文字種、
    /// VersionAdded の semver 形式と VersionRemoved&gt;VersionAdded。
    /// </summary>
    private static void CheckCommonFields(
        MasterEntity entity,
        string entityKind,
        ICollection<MasterValidationError> errors)
    {
        RequireNonEmpty(entity.Id, entityKind, entity.Id, "Id", errors);
        RequireNoWhitespace(entity.Id, entityKind, entity.Id, "Id", errors);
        RequireNonEmpty(entity.Name, entityKind, entity.Id, "Name", errors);
        CheckIconKey(entityKind, entity.Id, entity.IconKey, errors);

        SemVersion? added = null;
        if (string.IsNullOrWhiteSpace(entity.VersionAdded))
        {
            errors.Add(new MasterValidationError(
                entityKind, entity.Id, "VersionAdded", "VersionAdded は必須です。"));
        }
        else if (!SemVersion.TryParse(entity.VersionAdded, out SemVersion parsed))
        {
            errors.Add(new MasterValidationError(
                entityKind, entity.Id, "VersionAdded",
                $"VersionAdded は semver としてパース可能である必要があります: {entity.VersionAdded}"));
        }
        else
        {
            added = parsed;
        }

        if (entity.VersionRemoved is string removed)
        {
            if (!SemVersion.TryParse(removed, out SemVersion removedVersion))
            {
                errors.Add(new MasterValidationError(
                    entityKind, entity.Id, "VersionRemoved",
                    $"VersionRemoved は semver としてパース可能である必要があります: {removed}"));
            }
            else if (added is { } addedVersion && addedVersion.CompareTo(removedVersion) >= 0)
            {
                errors.Add(new MasterValidationError(
                    entityKind, entity.Id, "VersionRemoved",
                    $"VersionRemoved は VersionAdded より大きい必要があります: {entity.VersionAdded} >= {removed}"));
            }
        }
    }

    private static void CheckGameEventRef(
        string entityKind,
        string entityId,
        string? gameEventId,
        IReadOnlyCollection<string> gameEventIds,
        EntityDisplay display,
        ICollection<MasterValidationError> errors)
    {
        if (!string.IsNullOrEmpty(gameEventId))
        {
            RequireNoWhitespace(gameEventId, entityKind, entityId, "GameEventId", errors);
            if (!gameEventIds.Contains(gameEventId))
            {
                errors.Add(new MasterValidationError(
                    entityKind, entityId, "GameEventId",
                    $"GameEventId が参照するイベントが存在しません: {display.GameEvent(gameEventId)}"));
            }
        }
    }

    /// <summary>
    /// IconKey の文字種制約（仕様決定 AP）。null/空は未設定として許容する。
    /// </summary>
    private static void CheckIconKey(
        string entityKind,
        string entityId,
        string? iconKey,
        ICollection<MasterValidationError> errors)
    {
        if (iconKey is null || iconKey.Length == 0 || IconKeyRules.IsValid(iconKey))
        {
            return;
        }

        errors.Add(new MasterValidationError(
            entityKind, entityId, "IconKey",
            $"IconKey は英数字・ハイフン・アンダースコアの 1〜64 文字で指定してください: {iconKey}"));
    }

    private static void RequireNonEmpty(
        string? value,
        string entityKind,
        string entityId,
        string field,
        ICollection<MasterValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new MasterValidationError(
                entityKind, entityId, field, $"{field} は必須です。"));
        }
    }

    /// <summary>ID 系値は空白文字を含まない（仕様決定 BW）。null/空白のみは必須違反側に委ねる。</summary>
    private static void RequireNoWhitespace(
        string? value,
        string entityKind,
        string entityId,
        string field,
        ICollection<MasterValidationError> errors)
    {
        if (!string.IsNullOrEmpty(value) && value.Any(char.IsWhiteSpace))
        {
            errors.Add(new MasterValidationError(
                entityKind, entityId, field, $"{field} には空白を含めないでください: {value}"));
        }
    }

    private static void RequireEnum<TEnum>(
        TEnum value,
        string entityKind,
        string entityId,
        string field,
        ICollection<MasterValidationError> errors)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            errors.Add(new MasterValidationError(
                entityKind, entityId, field, $"{field} の値が不正です: {value}"));
        }
    }

    private static void EnsureUniqueIds(
        IEnumerable<string?> ids,
        string collectionName,
        EntityDisplay display,
        ICollection<MasterValidationError> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? id in ids)
        {
            if (!string.IsNullOrEmpty(id) && !seen.Add(id))
            {
                errors.Add(new MasterValidationError(
                    collectionName, id!, "Id", $"{collectionName} に ID の重複があります: {display.For(collectionName, id!)}"));
            }
        }
    }
}
