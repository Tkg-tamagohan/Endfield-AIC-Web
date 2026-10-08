using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Domain.Validation;

public static partial class MasterValidator
{
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
}
