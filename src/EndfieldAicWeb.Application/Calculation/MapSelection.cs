using System.Globalization;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Calculation;

/// <summary>
/// 計算に使うマップ候補と採取レートを導出する。
/// </summary>
public static class MapSelection
{
    /// <summary>常設または有効イベント所属のマップを、マスタの並び順で返す。</summary>
    public static IReadOnlyList<GameMap> ListCandidates(
        MasterDataSnapshot snapshot,
        IReadOnlyCollection<string> activeGameEventIds)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(activeGameEventIds);

        return snapshot.Maps
            .Where(map => map.GameEventId is null || activeGameEventIds.Contains(map.GameEventId))
            .ToList();
    }

    /// <summary>マップが存在し、所属イベントが有効であるかを返す。</summary>
    public static bool IsAvailable(
        MasterDataSnapshot snapshot,
        string mapId,
        IReadOnlyCollection<string> activeGameEventIds)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(mapId);
        ArgumentNullException.ThrowIfNull(activeGameEventIds);

        return snapshot.MapsById.TryGetValue(mapId, out GameMap? map)
            && (map.GameEventId is null || activeGameEventIds.Contains(map.GameEventId));
    }

    /// <summary>マップによる上書きなしの採取上限を返す。null は上限なし。</summary>
    public static double? DefaultGatherCap(MasterDataSnapshot snapshot, ContextFilter context, string itemId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(itemId);

        if (context.MapId is null)
        {
            return null;
        }

        if (!IsAvailable(snapshot, context.MapId, context.ActiveGameEventIds))
        {
            return 0;
        }

        GameMap map = snapshot.MapsById[context.MapId];
        GatherRate? row = map.GatherRates.FirstOrDefault(rate => rate.ItemId == itemId);
        return row switch
        {
            null => 0,
            { IsUnlimited: true } => null,
            _ => row.RatePerMinute ?? 0,
        };
    }

    /// <summary>計画の要求順で、採取レート入力行を出す採取素材の Id を返す。</summary>
    public static IReadOnlyList<string> GatherableItemIds(ProductionPlan plan, MasterDataSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);

        return plan.ItemRequirements
            .Where(requirement =>
                snapshot.ItemsById.TryGetValue(requirement.ItemId, out Item? item)
                && item.IsGatherable)
            .Select(requirement => requirement.ItemId)
            .ToList();
    }

    /// <summary>計画内でそのアイテムへ採取として充当された合計レート（個/分）。計画に登場しなければ 0。</summary>
    public static double GatheredRate(ProductionPlan plan, string itemId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(itemId);

        return plan.ItemRequirements
            .FirstOrDefault(requirement => requirement.ItemId == itemId)?
            .Supplies.Where(supply => supply.Kind == SupplyKind.Gathered)
            .Sum(supply => supply.AmountPerMinute) ?? 0;
    }

    /// <summary>計画内のそのアイテムの要求レート（個/分）。計画に登場しなければ 0。</summary>
    public static double RequiredRate(ProductionPlan plan, string itemId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(itemId);

        return plan.ItemRequirements
            .FirstOrDefault(requirement => requirement.ItemId == itemId)?
            .RequiredPerMinute ?? 0;
    }

    /// <summary>
    /// 採取レートのユーザー上書きを反映した有効採取上限（個/分）。null は上限なし。
    /// 入力が 0 以上の有限値のときその値を採用し、空欄・非数値・負・非有限はマップ既定値へ戻す。
    /// </summary>
    public static double? EffectiveGatherCap(
        MasterDataSnapshot snapshot,
        ContextFilter context,
        string itemId,
        string? rateText)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(itemId);

        if (!string.IsNullOrWhiteSpace(rateText)
            && double.TryParse(rateText, NumberStyles.Float, CultureInfo.InvariantCulture, out double cap)
            && double.IsFinite(cap)
            && cap >= 0)
        {
            return cap;
        }

        return DefaultGatherCap(snapshot, context, itemId);
    }

    /// <summary>
    /// 採取充当が有効上限へ到達し、なお需要が上限を超えて残るか（「上限到達」表示の判定）。
    /// 上限なしの素材は到達しえない。
    /// </summary>
    public static bool IsGatherCapReached(
        ProductionPlan plan,
        MasterDataSnapshot snapshot,
        ContextFilter context,
        string itemId,
        string? rateText)
    {
        ArgumentNullException.ThrowIfNull(plan);

        double? cap = EffectiveGatherCap(snapshot, context, itemId, rateText);
        return cap is double value
            && double.IsFinite(value)
            && GatheredRate(plan, itemId) >= value - 1e-9
            && RequiredRate(plan, itemId) > value - 1e-9;
    }
}
