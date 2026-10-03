using EndfieldAicWeb.Domain.Models;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Domain.Validation;

/// <summary>
/// マスタデータの値域と意味レベルの検証。JSON・画面入力を問わず同一規則を適用する。
/// 違反は例外ではなく <see cref="MasterValidationError"/> の一覧として集約して返す。
/// </summary>
public static class MasterValidator
{
    /// <summary>
    /// 単一 Item のフィールド内規則を検査する（参照整合性は含まない）。
    /// display は文面のエンティティ参照を `名前（Id）` へ整形する解決器（任意、省略時は Id のみ出力）。
    /// </summary>
    public static void ValidateItem(Item item, ICollection<MasterValidationError> errors, EntityDisplay? display = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(errors);

        CheckCommonFields(item, "Item", errors);
        RequireNonEmpty(item.Category, "Item", item.Id, "Category", errors);
        RequireEnum(item.TransportKind, "Item", item.Id, "TransportKind", errors);
    }

    /// <summary>
    /// 単一 Facility のフィールド内規則を検査する（参照整合性は含まない）。
    /// display は文面のエンティティ参照を `名前（Id）` へ整形する解決器（任意、省略時は Id のみ出力）。
    /// </summary>
    public static void ValidateFacility(Facility facility, ICollection<MasterValidationError> errors, EntityDisplay? display = null)
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

    /// <summary>
    /// 単一 Environment のフィールド内規則を検査する（参照整合性は含まない）。
    /// display は文面のエンティティ参照を `名前（Id）` へ整形する解決器（任意、省略時は Id のみ出力）。
    /// </summary>
    public static void ValidateEnvironment(Environment environment, ICollection<MasterValidationError> errors, EntityDisplay? display = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(errors);

        CheckCommonFields(environment, "Environment", errors);
        RequireNonEmpty(environment.ProviderFacilityId, "Environment", environment.Id, "ProviderFacilityId", errors);
        RequireNonEmpty(environment.ConsumeItemId, "Environment", environment.Id, "ConsumeItemId", errors);
        RequireNoWhitespace(environment.ProviderFacilityId, "Environment", environment.Id, "ProviderFacilityId", errors);
        RequireNoWhitespace(environment.ConsumeItemId, "Environment", environment.Id, "ConsumeItemId", errors);

        if (!double.IsFinite(environment.ConsumeRatePerMinute) || environment.ConsumeRatePerMinute <= 0)
        {
            errors.Add(new MasterValidationError(
                "Environment", environment.Id, "ConsumeRatePerMinute",
                $"ConsumeRatePerMinute は 0 より大きい有限値である必要があります: {environment.ConsumeRatePerMinute}"));
        }

        if (environment.CoverableMachines <= 0)
        {
            errors.Add(new MasterValidationError(
                "Environment", environment.Id, "CoverableMachines",
                $"CoverableMachines は 0 より大きい整数である必要があります: {environment.CoverableMachines}"));
        }
    }

    /// <summary>
    /// 単一 GameEvent のフィールド内規則を検査する。
    /// display は文面のエンティティ参照を `名前（Id）` へ整形する解決器（任意、省略時は Id のみ出力）。
    /// </summary>
    public static void ValidateGameEvent(GameEvent gameEvent, ICollection<MasterValidationError> errors, EntityDisplay? display = null)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);
        ArgumentNullException.ThrowIfNull(errors);

        CheckCommonFields(gameEvent, "GameEvent", errors);

        if (gameEvent.ActiveFrom is { Kind: DateTimeKind.Unspecified }
            || gameEvent.ActiveTo is { Kind: DateTimeKind.Unspecified })
        {
            errors.Add(new MasterValidationError(
                "GameEvent", gameEvent.Id, "ActiveFrom/ActiveTo",
                "ActiveFrom/ActiveTo はタイムゾーン（Z またはオフセット）付きの日時で指定する必要があります（仕様決定 Z）"));
        }
        else if (gameEvent.ActiveFrom is { } from && gameEvent.ActiveTo is { } to
            && from.ToUniversalTime() >= to.ToUniversalTime())
        {
            errors.Add(new MasterValidationError(
                "GameEvent", gameEvent.Id, "ActiveFrom/ActiveTo",
                $"ActiveFrom は ActiveTo より前である必要があります: {gameEvent.ActiveFrom} >= {gameEvent.ActiveTo}"));
        }
    }

    /// <summary>
    /// 単一 Recipe のフィールド内規則を検査する（参照整合性は ValidateAll）。
    /// display は文面のエンティティ参照を `名前（Id）` へ整形する解決器（任意、省略時は Id のみ出力）。
    /// </summary>
    public static void ValidateRecipe(Recipe recipe, ICollection<MasterValidationError> errors, EntityDisplay? display = null)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(errors);
        EntityDisplay resolver = display ?? EntityDisplay.Empty;

        CheckCommonFields(recipe, "Recipe", errors);

        // Outputs は 0 件を許容する（出力なしレシピ＝処理レシピ、仕様決定 BZ）。
        if (recipe.Inputs is null || recipe.Inputs.Count == 0)
        {
            errors.Add(new MasterValidationError(
                "Recipe", recipe.Id, "Inputs", "Inputs は 1 件以上必要です。"));
        }

        if (recipe.Facilities is null || recipe.Facilities.Count == 0)
        {
            errors.Add(new MasterValidationError(
                "Recipe", recipe.Id, "Facilities", "Facilities は 1 件以上必要です。"));
        }

        if (recipe.Outputs is not null)
        {
            for (int i = 0; i < recipe.Outputs.Count; i++)
            {
                if (recipe.Outputs[i].SortOrder < 0)
                {
                    errors.Add(new MasterValidationError(
                        "Recipe", recipe.Id, $"Outputs[{i}].SortOrder",
                        $"Outputs[{i}].SortOrder は 0 以上である必要があります: {recipe.Outputs[i].SortOrder}"));
                }
            }
        }

        ValidateRecipeItems(recipe.Inputs, i => i.ItemId, i => i.Quantity, recipe.Id, "Inputs", errors, resolver);
        ValidateRecipeItems(recipe.Outputs, o => o.ItemId, o => o.Quantity, recipe.Id, "Outputs", errors, resolver);
        ValidatePairs(recipe, errors, resolver);
    }

    /// <summary>レシピのペア一覧のフィールド内規則と一意性（仕様決定 P）を検査する。</summary>
    private static void ValidatePairs(Recipe recipe, ICollection<MasterValidationError> errors, EntityDisplay display)
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
            RequireNoWhitespace(pair.FacilityId, "Recipe", recipe.Id, $"{name}.FacilityId", errors);
            if (pair.EnvironmentId is not null)
            {
                RequireNoWhitespace(pair.EnvironmentId, "Recipe", recipe.Id, $"{name}.EnvironmentId", errors);
            }

            if (pair.RecipeId is not null && pair.RecipeId != recipe.Id)
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipe.Id, $"{name}.RecipeId",
                    $"{name}.RecipeId は所属レシピの Id と一致する必要があります: {display.Recipe(pair.RecipeId)} != {display.Recipe(recipe.Id)}"));
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
                else
                {
                    RequireNoWhitespace(
                        fixedConsumption.ItemId, "Recipe", recipe.Id,
                        $"{name}.FixedConsumption.ItemId", errors);
                }

                if (!double.IsFinite(fixedConsumption.RatePerMinute) || fixedConsumption.RatePerMinute <= 0)
                {
                    errors.Add(new MasterValidationError(
                        "Recipe", recipe.Id, $"{name}.FixedConsumption.RatePerMinute",
                        $"{name}.FixedConsumption.RatePerMinute は 0 より大きい有限値である必要があります: {fixedConsumption.RatePerMinute}"));
                }
            }

            // 一意キーは全要素の組（仕様決定 P）。
            var key = (
                pair.FacilityId,
                pair.CycleTime,
                pair.EnvironmentId,
                pair.FixedConsumption?.ItemId,
                pair.FixedConsumption?.RatePerMinute);
            if (!seen.Add(key))
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipe.Id, "Facilities",
                    $"Facilities に全要素が一致するペアの重複があります: {display.Facility(pair.FacilityId)} / {pair.CycleTime}s / 環境={(pair.EnvironmentId is { } envId ? display.Environment(envId) : "なし")}"));
            }
        }
    }

    /// <summary>
    /// 単一 GameMap のフィールド内規則を検査する（参照整合性は ValidateAll）。
    /// display は文面のエンティティ参照を `名前（Id）` へ整形する解決器（任意、省略時は Id のみ出力）。
    /// </summary>
    public static void ValidateGameMap(GameMap map, ICollection<MasterValidationError> errors, EntityDisplay? display = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(errors);
        EntityDisplay resolver = display ?? EntityDisplay.Empty;

        CheckCommonFields(map, "GameMap", errors);

        if (map.GatherRates is null)
        {
            errors.Add(new MasterValidationError(
                "GameMap", map.Id, "GatherRates", "GatherRates は必須です。"));
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < map.GatherRates.Count; i++)
        {
            GatherRate? rate = map.GatherRates[i];
            string field = $"GatherRates[{i}]";
            if (rate is null)
            {
                errors.Add(new MasterValidationError(
                    "GameMap", map.Id, "GatherRates", $"{field} が null です。"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(rate.ItemId))
            {
                errors.Add(new MasterValidationError(
                    "GameMap", map.Id, $"{field}.ItemId", $"{field}.ItemId は必須です。"));
            }
            else
            {
                RequireNoWhitespace(rate.ItemId, "GameMap", map.Id, $"{field}.ItemId", errors);
                if (!seen.Add(rate.ItemId))
                {
                    errors.Add(new MasterValidationError(
                        "GameMap", map.Id, "GatherRates",
                        $"GatherRates に同一アイテムが重複しています: {resolver.Item(rate.ItemId)}"));
                }
            }

            if (!rate.IsUnlimited && (rate.RatePerMinute is not { } boundedRate
                || !double.IsFinite(boundedRate) || boundedRate <= 0))
            {
                errors.Add(new MasterValidationError(
                    "GameMap", map.Id, $"{field}.RatePerMinute",
                    $"{field}.RatePerMinute は 0 より大きい有限値である必要があります: {rate.RatePerMinute}"));
            }
            else if (rate.IsUnlimited && rate.RatePerMinute is not null)
            {
                errors.Add(new MasterValidationError(
                    "GameMap", map.Id, $"{field}.RatePerMinute",
                    $"{field}.RatePerMinute は無限の場合 null である必要があります: {rate.RatePerMinute}"));
            }
        }
    }

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

    private static void ValidateGameMapInContext(
        GameMap map,
        IReadOnlyDictionary<string, Item> itemsById,
        IReadOnlyCollection<string> gameEventIds,
        EntityDisplay display,
        ICollection<MasterValidationError> errors)
    {
        CheckGameEventRef("GameMap", map.Id, map.GameEventId, gameEventIds, display, errors);

        if (map.GatherRates is null)
        {
            return;
        }

        for (int i = 0; i < map.GatherRates.Count; i++)
        {
            GatherRate? rate = map.GatherRates[i];
            if (rate is null || string.IsNullOrEmpty(rate.ItemId))
            {
                continue;
            }

            string field = $"GatherRates[{i}].ItemId";
            if (!itemsById.TryGetValue(rate.ItemId, out Item? item))
            {
                errors.Add(new MasterValidationError(
                    "GameMap", map.Id, field,
                    $"GatherRates が参照するアイテムが存在しません: {display.Item(rate.ItemId)}"));
            }
            else if (!item.IsGatherable)
            {
                errors.Add(new MasterValidationError(
                    "GameMap", map.Id, field,
                    $"GatherRates が参照するアイテムは採取素材ではありません: {display.Item(rate.ItemId)}"));
            }
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
        EntityDisplay display,
        ICollection<MasterValidationError> errors)
    {
        CheckGameEventRef("Recipe", recipe.Id, recipe.GameEventId, gameEventIds, display, errors);

        foreach (RecipeInput input in recipe.Inputs ?? [])
        {
            if (!string.IsNullOrEmpty(input.ItemId) && !itemsById.ContainsKey(input.ItemId))
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipe.Id, "Inputs",
                    $"Inputs が参照するアイテムが存在しません: {display.Item(input.ItemId)}"));
            }
        }

        foreach (RecipeOutput output in recipe.Outputs ?? [])
        {
            if (!string.IsNullOrEmpty(output.ItemId) && !itemsById.ContainsKey(output.ItemId))
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipe.Id, "Outputs",
                    $"Outputs が参照するアイテムが存在しません: {display.Item(output.ItemId)}"));
            }
        }

        foreach (RecipeFacility pair in recipe.Facilities ?? [])
        {
            if (!string.IsNullOrEmpty(pair.FacilityId) && !facilityIds.Contains(pair.FacilityId))
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipe.Id, "Facilities",
                    $"Facilities の FacilityId が参照する設備が存在しません: {display.Facility(pair.FacilityId)}"));
            }

            if (!string.IsNullOrEmpty(pair.EnvironmentId) && !environmentIds.Contains(pair.EnvironmentId))
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipe.Id, "Facilities",
                    $"Facilities の EnvironmentId が参照する環境が存在しません: {display.Environment(pair.EnvironmentId)}"));
            }

            if (pair.FixedConsumption is FixedConsumption fixedConsumption
                && !string.IsNullOrEmpty(fixedConsumption.ItemId)
                && !itemsById.ContainsKey(fixedConsumption.ItemId))
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipe.Id, "Facilities",
                    $"FixedConsumption.ItemId が参照するアイテムが存在しません: {display.Item(fixedConsumption.ItemId)}"));
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
                    $"Inputs に仮想アイテム（TransportKind.None）を含めることはできません: {display.Item(input.ItemId)}"));
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
