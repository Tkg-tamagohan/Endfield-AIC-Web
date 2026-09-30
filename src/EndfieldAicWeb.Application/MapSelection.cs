using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application;

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
}
