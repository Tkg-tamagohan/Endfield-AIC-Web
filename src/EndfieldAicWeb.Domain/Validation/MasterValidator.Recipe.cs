using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Domain.Validation;

public static partial class MasterValidator
{
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
}
