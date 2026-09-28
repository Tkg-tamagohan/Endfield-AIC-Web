using EndfieldAicWeb.Domain.Models;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Domain.Validation;

/// <summary>
/// マスタデータの値域と意味レベルの検証。JSON・画面入力を問わず同一規則を適用する。
/// 違反は例外ではなく <see cref="MasterValidationError"/> の一覧として集約して返す。
/// </summary>
public static class MasterValidator
{
    /// <summary>単一 Item のフィールド内規則を検査する（参照整合性は含まない）。</summary>
    public static void ValidateItem(Item item, ICollection<MasterValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(errors);

        CheckCommonFields(item, "Item", errors);
        RequireNonEmpty(item.Category, "Item", item.Id, "Category", errors);
        RequireEnum(item.TransportKind, "Item", item.Id, "TransportKind", errors);
    }

    /// <summary>単一 Facility のフィールド内規則を検査する（参照整合性は含まない）。</summary>
    public static void ValidateFacility(Facility facility, ICollection<MasterValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(facility);
        ArgumentNullException.ThrowIfNull(errors);

        CheckCommonFields(facility, "Facility", errors);

        if (!double.IsFinite(facility.Width) || facility.Width <= 0)
        {
            errors.Add(new MasterValidationError(
                "Facility", facility.Id, "Width",
                $"Width は 0 より大きい有限値である必要があります: {facility.Width}"));
        }

        if (!double.IsFinite(facility.Height) || facility.Height <= 0)
        {
            errors.Add(new MasterValidationError(
                "Facility", facility.Id, "Height",
                $"Height は 0 より大きい有限値である必要があります: {facility.Height}"));
        }

        if (!double.IsFinite(facility.PowerConsumption) || facility.PowerConsumption < 0)
        {
            errors.Add(new MasterValidationError(
                "Facility", facility.Id, "PowerConsumption",
                $"PowerConsumption は 0 以上の有限値である必要があります: {facility.PowerConsumption}"));
        }
    }

    /// <summary>単一 Environment のフィールド内規則を検査する（参照整合性は含まない）。</summary>
    public static void ValidateEnvironment(Environment environment, ICollection<MasterValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(errors);

        CheckCommonFields(environment, "Environment", errors);
        RequireNonEmpty(environment.ProviderFacilityId, "Environment", environment.Id, "ProviderFacilityId", errors);
        RequireNonEmpty(environment.ConsumeItemId, "Environment", environment.Id, "ConsumeItemId", errors);

        if (!double.IsFinite(environment.ConsumeRatePerSecond) || environment.ConsumeRatePerSecond <= 0)
        {
            errors.Add(new MasterValidationError(
                "Environment", environment.Id, "ConsumeRatePerSecond",
                $"ConsumeRatePerSecond は 0 より大きい有限値である必要があります: {environment.ConsumeRatePerSecond}"));
        }
    }

    /// <summary>単一 GameEvent のフィールド内規則を検査する。</summary>
    public static void ValidateGameEvent(GameEvent gameEvent, ICollection<MasterValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);
        ArgumentNullException.ThrowIfNull(errors);

        CheckCommonFields(gameEvent, "GameEvent", errors);

        if (gameEvent.ActiveFrom is not null && gameEvent.ActiveTo is not null
            && gameEvent.ActiveFrom >= gameEvent.ActiveTo)
        {
            errors.Add(new MasterValidationError(
                "GameEvent", gameEvent.Id, "ActiveFrom/ActiveTo",
                $"ActiveFrom は ActiveTo より前である必要があります: {gameEvent.ActiveFrom} >= {gameEvent.ActiveTo}"));
        }
    }

    /// <summary>単一 Recipe のフィールド内規則を検査する（参照整合性は ValidateAll）。</summary>
    public static void ValidateRecipe(Recipe recipe, ICollection<MasterValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(errors);

        CheckCommonFields(recipe, "Recipe", errors);

        if (recipe.Outputs is null || recipe.Outputs.Count == 0)
        {
            errors.Add(new MasterValidationError(
                "Recipe", recipe.Id, "Outputs", "Outputs は 1 件以上必要です。"));
        }

        if (recipe.Facilities is null || recipe.Facilities.Count == 0)
        {
            errors.Add(new MasterValidationError(
                "Recipe", recipe.Id, "Facilities", "Facilities は 1 件以上必要です。"));
        }

        ValidateRecipeItems(recipe.Inputs, i => i.ItemId, i => i.Quantity, recipe.Id, "Inputs", errors);
        ValidateRecipeItems(recipe.Outputs, o => o.ItemId, o => o.Quantity, recipe.Id, "Outputs", errors);
        ValidatePairs(recipe, errors);
    }

    /// <summary>レシピのペア一覧のフィールド内規則と一意性（仕様決定 P）を検査する。</summary>
    private static void ValidatePairs(Recipe recipe, ICollection<MasterValidationError> errors)
    {
        if (recipe.Facilities is null)
        {
            return;
        }

        var seen = new HashSet<(string, double, string?, string?, double?)>();
        for (int i = 0; i < recipe.Facilities.Count; i++)
        {
            RecipeFacility pair = recipe.Facilities[i];
            string name = $"Facilities[{i}]";

            RequireNonEmpty(pair.FacilityId, "Recipe", recipe.Id, $"{name}.FacilityId", errors);

            if (pair.RecipeId is not null && pair.RecipeId != recipe.Id)
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipe.Id, $"{name}.RecipeId",
                    $"{name}.RecipeId は所属レシピの Id と一致する必要があります: {pair.RecipeId} != {recipe.Id}"));
            }

            if (!double.IsFinite(pair.CycleTime) || pair.CycleTime <= 0)
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipe.Id, $"{name}.CycleTime",
                    $"{name}.CycleTime は 0 より大きい有限値である必要があります: {pair.CycleTime}"));
            }

            if (pair.FixedConsumption is FixedConsumption fixedConsumption)
            {
                if (string.IsNullOrWhiteSpace(fixedConsumption.ItemId))
                {
                    errors.Add(new MasterValidationError(
                        "Recipe", recipe.Id, $"{name}.FixedConsumption.ItemId",
                        $"{name}.FixedConsumption.ItemId は必須です。"));
                }

                if (!double.IsFinite(fixedConsumption.RatePerSecond) || fixedConsumption.RatePerSecond <= 0)
                {
                    errors.Add(new MasterValidationError(
                        "Recipe", recipe.Id, $"{name}.FixedConsumption.RatePerSecond",
                        $"{name}.FixedConsumption.RatePerSecond は 0 より大きい有限値である必要があります: {fixedConsumption.RatePerSecond}"));
                }
            }

            // 一意キーは全要素の組（仕様決定 P）。
            var key = (
                pair.FacilityId,
                pair.CycleTime,
                pair.EnvironmentId,
                pair.FixedConsumption?.ItemId,
                pair.FixedConsumption?.RatePerSecond);
            if (!seen.Add(key))
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipe.Id, "Facilities",
                    $"Facilities に全要素が一致するペアの重複があります: {pair.FacilityId} / {pair.CycleTime}s / 環境={pair.EnvironmentId ?? "なし"}"));
            }
        }
    }

    private static void ValidateRecipeItems<T>(
        IReadOnlyList<T>? items,
        Func<T, string?> itemIdOf,
        Func<T, double> quantityOf,
        string recipeId,
        string memberName,
        ICollection<MasterValidationError> errors)
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
            else if (!seen.Add(itemId))
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipeId, memberName,
                    $"{memberName} に同一アイテムが重複しています: {itemId}"));
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
        ICollection<MasterValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(facilities);
        ArgumentNullException.ThrowIfNull(environments);
        ArgumentNullException.ThrowIfNull(gameEvents);
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(errors);

        foreach (Item item in items)
        {
            ValidateItem(item, errors);
        }

        foreach (Facility facility in facilities)
        {
            ValidateFacility(facility, errors);
        }

        foreach (Environment environment in environments)
        {
            ValidateEnvironment(environment, errors);
        }

        foreach (GameEvent gameEvent in gameEvents)
        {
            ValidateGameEvent(gameEvent, errors);
        }

        foreach (Recipe recipe in recipes)
        {
            ValidateRecipe(recipe, errors);
        }

        EnsureUniqueIds(items.Select(i => i.Id), "Items", errors);
        EnsureUniqueIds(facilities.Select(f => f.Id), "Facilities", errors);
        EnsureUniqueIds(environments.Select(e => e.Id), "Environments", errors);
        EnsureUniqueIds(gameEvents.Select(e => e.Id), "GameEvents", errors);
        EnsureUniqueIds(recipes.Select(r => r.Id), "Recipes", errors);

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
            CheckGameEventRef("Item", item.Id, item.GameEventId, gameEventIds, errors);
        }

        foreach (Environment environment in environments)
        {
            if (!string.IsNullOrEmpty(environment.ProviderFacilityId)
                && !facilityIds.Contains(environment.ProviderFacilityId))
            {
                errors.Add(new MasterValidationError(
                    "Environment", environment.Id, "ProviderFacilityId",
                    $"ProviderFacilityId が参照する設備が存在しません: {environment.ProviderFacilityId}"));
            }

            if (!string.IsNullOrEmpty(environment.ConsumeItemId)
                && !itemsById.ContainsKey(environment.ConsumeItemId))
            {
                errors.Add(new MasterValidationError(
                    "Environment", environment.Id, "ConsumeItemId",
                    $"ConsumeItemId が参照するアイテムが存在しません: {environment.ConsumeItemId}"));
            }

            CheckGameEventRef("Environment", environment.Id, environment.GameEventId, gameEventIds, errors);
        }

        foreach (Recipe recipe in recipes)
        {
            ValidateRecipeInContext(recipe, itemsById, facilityIds, environmentIds, gameEventIds, errors);
        }
    }

    /// <summary>
    /// 参照先集合が既知のレシピについて、参照整合性・仮想アイテム規則を検査する。
    /// </summary>
    private static void ValidateRecipeInContext(
        Recipe recipe,
        IReadOnlyDictionary<string, Item> itemsById,
        IReadOnlyCollection<string> facilityIds,
        IReadOnlyCollection<string> environmentIds,
        IReadOnlyCollection<string> gameEventIds,
        ICollection<MasterValidationError> errors)
    {
        CheckGameEventRef("Recipe", recipe.Id, recipe.GameEventId, gameEventIds, errors);

        foreach (RecipeInput input in recipe.Inputs ?? [])
        {
            if (!string.IsNullOrEmpty(input.ItemId) && !itemsById.ContainsKey(input.ItemId))
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipe.Id, "Inputs",
                    $"Inputs が参照するアイテムが存在しません: {input.ItemId}"));
            }
        }

        foreach (RecipeOutput output in recipe.Outputs ?? [])
        {
            if (!string.IsNullOrEmpty(output.ItemId) && !itemsById.ContainsKey(output.ItemId))
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipe.Id, "Outputs",
                    $"Outputs が参照するアイテムが存在しません: {output.ItemId}"));
            }
        }

        foreach (RecipeFacility pair in recipe.Facilities ?? [])
        {
            if (!string.IsNullOrEmpty(pair.FacilityId) && !facilityIds.Contains(pair.FacilityId))
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipe.Id, "Facilities",
                    $"Facilities の FacilityId が参照する設備が存在しません: {pair.FacilityId}"));
            }

            if (!string.IsNullOrEmpty(pair.EnvironmentId) && !environmentIds.Contains(pair.EnvironmentId))
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipe.Id, "Facilities",
                    $"Facilities の EnvironmentId が参照する環境が存在しません: {pair.EnvironmentId}"));
            }

            if (pair.FixedConsumption is FixedConsumption fixedConsumption
                && !string.IsNullOrEmpty(fixedConsumption.ItemId)
                && !itemsById.ContainsKey(fixedConsumption.ItemId))
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipe.Id, "Facilities",
                    $"FixedConsumption.ItemId が参照するアイテムが存在しません: {fixedConsumption.ItemId}"));
            }
        }

        // 仮想アイテム規則（継承分のみ）: レシピ入力に TransportKind.None を含めない。
        // 参照先が欠けている入力は判定不能なためその入力のみ飛ばす。
        foreach (RecipeInput input in recipe.Inputs ?? [])
        {
            if (!string.IsNullOrEmpty(input.ItemId)
                && itemsById.TryGetValue(input.ItemId, out Item? inputItem)
                && inputItem.TransportKind == TransportKind.None)
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipe.Id, "Inputs",
                    $"Inputs に仮想アイテム（TransportKind.None）を含めることはできません: {input.ItemId}"));
            }
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
        ICollection<MasterValidationError> errors)
    {
        if (!string.IsNullOrEmpty(gameEventId) && !gameEventIds.Contains(gameEventId))
        {
            errors.Add(new MasterValidationError(
                entityKind, entityId, "GameEventId",
                $"GameEventId が参照するイベントが存在しません: {gameEventId}"));
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
        ICollection<MasterValidationError> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? id in ids)
        {
            if (!string.IsNullOrEmpty(id) && !seen.Add(id))
            {
                errors.Add(new MasterValidationError(
                    collectionName, id!, "Id", $"{collectionName} に ID の重複があります: {id}"));
            }
        }
    }
}
