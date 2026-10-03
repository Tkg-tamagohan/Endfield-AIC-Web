using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;

namespace EndfieldAicWeb.Domain.Calculation;

/// <summary>
/// 採取素材ごとの有効採取上限（個/分）を解決する。
/// <see cref="ProductionCalculator"/> 専用の内部実装。
/// </summary>
internal static class GatherCapResolver
{
    /// <summary>
    /// 採取素材ごとの有効採取上限（個/分、無限は <see cref="double.PositiveInfinity"/>）を解決する。
    /// 解決順は 上書き → マップの採取レート行 → 行なしは 0 で、無限行は上限なし、
    /// マップ未選択は全採取素材を無制限とする（仕様決定 AC・AE）。
    /// マップが存在しない、または所属イベントが非有効なら全採取素材を採取不可（上限 0）とし、
    /// 採取レートのユーザー上書きは適用しない（仕様決定 AD、X と同型）。
    /// </summary>
    internal static Dictionary<string, double> Resolve(
        MasterDataSnapshot master,
        ContextFilter context,
        IReadOnlyList<GatherRateOverride> gatherOverrides,
        WarningBag warnings)
    {
        var gatherCaps = new Dictionary<string, double>(StringComparer.Ordinal);
        var display = new EntityDisplay(master);

        List<string> gatherableIds = master.Items.Where(i => i.IsGatherable).Select(i => i.Id).ToList();

        void CapAll(double cap)
        {
            foreach (string id in gatherableIds)
            {
                gatherCaps[id] = cap;
            }
        }

        if (context.MapId is null)
        {
            // 未選択は無制限。上書きは有効なマップ選択に対してのみ適用される（仕様決定 AE）。
            CapAll(double.PositiveInfinity);
            return gatherCaps;
        }

        if (!master.MapsById.TryGetValue(context.MapId, out GameMap? map))
        {
            warnings.Add(new CalculationWarning(
                WarningCode.InvalidGatherMap,
                $"選択されたマップ {display.GameMap(context.MapId)} はマスタに存在しないため、採取素材はすべて採取できません。"));
            CapAll(0);
            return gatherCaps;
        }

        if (map.GameEventId is not null && !context.ActiveGameEventIds.Contains(map.GameEventId))
        {
            warnings.Add(new CalculationWarning(
                WarningCode.GatherMapUnavailable,
                $"選択されたマップ {display.GameMap(map.Id)} はイベント {display.GameEvent(map.GameEventId)} が有効でないため、採取素材はすべて採取できません。"));
            CapAll(0);
            return gatherCaps;
        }

        // 上書きの検証。存在しない・採取素材でないアイテムへの指定と、負・非有限の値は無視する。
        var overrideByItem = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (GatherRateOverride gatherOverride in gatherOverrides)
        {
            if (!master.ItemsById.TryGetValue(gatherOverride.ItemId, out Item? overrideItem)
                || !overrideItem.IsGatherable)
            {
                warnings.Add(new CalculationWarning(
                    WarningCode.InvalidGatherRateOverride,
                    $"採取レートの上書きが採取素材でないアイテムを指しているため無視します: {display.Item(gatherOverride.ItemId)}"));
                continue;
            }

            if (!double.IsFinite(gatherOverride.RatePerMinute) || gatherOverride.RatePerMinute < 0)
            {
                warnings.Add(new CalculationWarning(
                    WarningCode.InvalidGatherRateOverride,
                    $"アイテム {display.Item(gatherOverride.ItemId)} の採取レート上書き {gatherOverride.RatePerMinute} は 0 以上の有限値ではないため無視します。"));
                continue;
            }

            // 同一アイテムへの重複上書きは先頭を採用する（散布機台数上書きと同型）。
            overrideByItem.TryAdd(gatherOverride.ItemId, gatherOverride.RatePerMinute);
        }

        foreach (string id in gatherableIds)
        {
            if (overrideByItem.TryGetValue(id, out double overridden))
            {
                gatherCaps[id] = overridden;
                continue;
            }

            GatherRate? row = map.GatherRates.FirstOrDefault(r => r.ItemId == id);
            gatherCaps[id] = row switch
            {
                null => 0,
                { IsUnlimited: true } => double.PositiveInfinity,
                _ => row.RatePerMinute ?? 0,
            };
        }

        return gatherCaps;
    }
}
