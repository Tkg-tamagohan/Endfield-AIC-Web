using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Domain.Validation;

public static partial class MasterValidator
{
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
}
