using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application;

/// <summary>生産フローグラフのノード種別（仕様決定 AI）。</summary>
public enum FlowGraphNodeKind
{
    Item,
    Facility,
    Gather,
}

/// <summary>生産フローグラフのエッジ種別。</summary>
public enum FlowGraphEdgeKind
{
    RecipeInput,
    RecipeOutput,
    FixedConsumption,
    EnvironmentConsume,
    Gathered,
}

/// <summary>
/// グラフのノード。描画側がラベル・アイコン・強調をそのまま使えるよう表示用の値を保持する。
/// RefId はリスト行へのスクロール対象（ItemId または FacilityId、採取ノードは null）。
/// </summary>
public sealed record FlowGraphNode(
    string Id,
    FlowGraphNodeKind Kind,
    string Label,
    string? IconKey,
    string? RefId,
    int Rank,
    int Order,
    bool IsTarget,
    double RequiredPerMinute,
    double UnmetPerMinute,
    double SurplusPerMinute,
    bool OverCapacity,
    string? Note);

/// <summary>
/// グラフのエッジ。同一ノード対・同種別は流量を合算して 1 本にまとめる。
/// OverCapacity は終点アイテムの輸送容量超過に連動する。
/// </summary>
public sealed record FlowGraphEdge(
    string FromId,
    string ToId,
    FlowGraphEdgeKind Kind,
    double RatePerMinute,
    bool IsByproduct,
    bool OverCapacity);

/// <summary>生産フローグラフ全体。MaxRatePerMinute は粒子密度の正規化に使う。</summary>
public sealed record FlowGraphModel(
    IReadOnlyList<FlowGraphNode> Nodes,
    IReadOnlyList<FlowGraphEdge> Edges,
    double MaxRatePerMinute);

/// <summary>
/// ProductionPlan から生産フローグラフ（仕様決定 AI）の表示モデルを組み立てる。
/// エッジ流量は表示中ビューに合わせ、未調整ビューでは ResultViewBuilder と同じ設備倍率を掛ける。
/// 層割りは最長パス法。循環依存（CycleDetected で打ち切られた残存経路を含む）は
/// 後退エッジとして無視してレイアウトだけを確定させる。
/// </summary>
public static class FlowGraphModelBuilder
{
    private const double Epsilon = 1e-9;
    private const string ItemPrefix = "item:";
    private const string FacilityPrefix = "fac:";

    /// <summary>採取供給の共通ノード Id（計画に採取がある場合のみ存在する）。</summary>
    public const string GatherNodeId = "gather";

    public static string ItemNodeId(string itemId) => ItemPrefix + itemId;

    public static string FacilityNodeId(string facilityId) => FacilityPrefix + facilityId;

    public static FlowGraphModel Build(
        ProductionPlan plan,
        MasterDataSnapshot snapshot,
        ContextFilter context,
        IReadOnlyList<ProductionTarget> targets,
        bool unadjusted)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(targets);

        IReadOnlyDictionary<string, double> scales = unadjusted
            ? ResultViewBuilder.ComputeUnadjustedFacilityScales(plan, snapshot)
            : new Dictionary<string, double>(StringComparer.Ordinal);

        var ceilByFacility = plan.FacilityRequirements
            .ToDictionary(f => f.FacilityId, f => f.CeilCount, StringComparer.Ordinal);

        var dispenserByFacility = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (EnvironmentRequirement env in plan.EnvironmentRequirements)
        {
            dispenserByFacility[env.ProviderFacilityId] =
                dispenserByFacility.GetValueOrDefault(env.ProviderFacilityId) + env.DispenserCount;
        }

        // 確定ペア（固定消費の参照用）。レシピごと一意（ResultViewBuilder と同じ前提）。
        var pairByRecipe = plan.PairSelections
            .GroupBy(s => s.RecipeId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Pair, StringComparer.Ordinal);

        var edgeParts = new List<FlowGraphEdge>();
        foreach (RecipeRun run in plan.RecipeRuns)
        {
            if (!snapshot.RecipesById.TryGetValue(run.RecipeId, out Recipe? recipe))
            {
                continue;
            }

            double scale = unadjusted ? scales.GetValueOrDefault(run.FacilityId, 1.0) : 1.0;
            string facilityNode = FacilityNodeId(run.FacilityId);
            foreach (RecipeInput input in recipe.Inputs)
            {
                edgeParts.Add(new FlowGraphEdge(
                    ItemNodeId(input.ItemId), facilityNode, FlowGraphEdgeKind.RecipeInput,
                    run.CyclesPerMinute * scale * input.Quantity, false, false));
            }

            foreach (RecipeOutput output in recipe.Outputs)
            {
                edgeParts.Add(new FlowGraphEdge(
                    facilityNode, ItemNodeId(output.ItemId), FlowGraphEdgeKind.RecipeOutput,
                    run.CyclesPerMinute * scale * output.Quantity, output.SortOrder > 0, false));
            }

            // 固定消費の乗数は最終切上台数（散布機分を含む FacilityRequirement.CeilCount）。
            RecipeFacility? pair = pairByRecipe.GetValueOrDefault(run.RecipeId)
                ?? recipe.Facilities.FirstOrDefault(p => p.FacilityId == run.FacilityId);
            if (pair?.FixedConsumption is { } fixedConsumption)
            {
                edgeParts.Add(new FlowGraphEdge(
                    ItemNodeId(fixedConsumption.ItemId), facilityNode,
                    FlowGraphEdgeKind.FixedConsumption,
                    fixedConsumption.RatePerMinute * ceilByFacility.GetValueOrDefault(run.FacilityId),
                    false, false));
            }
        }

        foreach (EnvironmentRequirement env in plan.EnvironmentRequirements)
        {
            edgeParts.Add(new FlowGraphEdge(
                ItemNodeId(env.ConsumeItemId), FacilityNodeId(env.ProviderFacilityId),
                FlowGraphEdgeKind.EnvironmentConsume, env.ConsumeRatePerMinuteTotal, false, false));
        }

        var gatheredByItem = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (ItemRequirement req in plan.ItemRequirements)
        {
            double gathered = req.Supplies
                .Where(s => s.Kind == SupplyKind.Gathered)
                .Sum(s => s.AmountPerMinute);
            if (gathered > Epsilon)
            {
                gatheredByItem[req.ItemId] = gathered;
                edgeParts.Add(new FlowGraphEdge(
                    GatherNodeId, ItemNodeId(req.ItemId), FlowGraphEdgeKind.Gathered,
                    gathered, false, false));
            }
        }

        var edges = edgeParts
            .Where(e => e.RatePerMinute > Epsilon)
            .GroupBy(e => (e.FromId, e.ToId, e.Kind))
            .Select(g => new FlowGraphEdge(
                g.Key.FromId, g.Key.ToId, g.Key.Kind,
                g.Sum(e => e.RatePerMinute),
                g.All(e => e.IsByproduct),
                false))
            .ToList();

        IReadOnlyList<SurplusProduction> surpluses = unadjusted
            ? ResultViewBuilder.ComputeUnadjustedSurpluses(plan, snapshot, context, scales)
            : plan.Surpluses;
        var surplusByItem = surpluses
            .ToDictionary(s => s.ItemId, s => s.ExcessPerMinute, StringComparer.Ordinal);

        var requirementByItem = plan.ItemRequirements
            .ToDictionary(r => r.ItemId, StringComparer.Ordinal);

        var itemIds = new SortedSet<string>(StringComparer.Ordinal);
        var facilityIds = new SortedSet<string>(StringComparer.Ordinal);
        foreach (ItemRequirement req in plan.ItemRequirements)
        {
            itemIds.Add(req.ItemId);
        }

        foreach (SurplusProduction surplus in surpluses)
        {
            itemIds.Add(surplus.ItemId);
        }

        foreach (FacilityRequirement f in plan.FacilityRequirements)
        {
            facilityIds.Add(f.FacilityId);
        }

        foreach (FlowGraphEdge edge in edges)
        {
            CollectEndpoint(edge.FromId, itemIds, facilityIds);
            CollectEndpoint(edge.ToId, itemIds, facilityIds);
        }

        // 輸送容量超過の簡易判定。計算本体と同じく 需要・生産・採取 の最大流量を見る。
        var producedByItem = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (FlowGraphEdge edge in edges)
        {
            if (edge.Kind == FlowGraphEdgeKind.RecipeOutput && edge.ToId.StartsWith(ItemPrefix, StringComparison.Ordinal))
            {
                string itemId = edge.ToId[ItemPrefix.Length..];
                producedByItem[itemId] = producedByItem.GetValueOrDefault(itemId) + edge.RatePerMinute;
            }
        }

        var overCapacityItems = new HashSet<string>(StringComparer.Ordinal);
        foreach (string itemId in itemIds)
        {
            if (!snapshot.ItemsById.TryGetValue(itemId, out Item? item))
            {
                continue;
            }

            double cap = item.TransportKind switch
            {
                TransportKind.Belt => ProductionCalculator.BeltCapacityPerSecond,
                TransportKind.Pipe => ProductionCalculator.PipeCapacityPerSecond,
                _ => 0,
            };
            if (cap <= 0)
            {
                continue;
            }

            double flowPerMinute = Math.Max(
                requirementByItem.GetValueOrDefault(itemId)?.RequiredPerMinute ?? 0,
                Math.Max(producedByItem.GetValueOrDefault(itemId), gatheredByItem.GetValueOrDefault(itemId)));
            if (flowPerMinute / 60.0 > cap + Epsilon)
            {
                overCapacityItems.Add(itemId);
            }
        }

        if (overCapacityItems.Count > 0)
        {
            edges = edges
                .Select(e => e.ToId.StartsWith(ItemPrefix, StringComparison.Ordinal)
                    && overCapacityItems.Contains(e.ToId[ItemPrefix.Length..])
                    ? e with { OverCapacity = true }
                    : e)
                .ToList();
        }

        // ノード確定（ランク付け前の素集合）。
        var targetItemIds = targets.Select(t => t.ItemId).ToHashSet(StringComparer.Ordinal);
        var nodes = new Dictionary<string, FlowGraphNode>(StringComparer.Ordinal);
        foreach (string itemId in itemIds)
        {
            snapshot.ItemsById.TryGetValue(itemId, out Item? item);
            requirementByItem.TryGetValue(itemId, out ItemRequirement? req);
            nodes[ItemNodeId(itemId)] = new FlowGraphNode(
                ItemNodeId(itemId), FlowGraphNodeKind.Item,
                item?.Name ?? itemId, item?.IconKey, itemId,
                0, 0,
                targetItemIds.Contains(itemId),
                req?.RequiredPerMinute ?? 0,
                req?.UnmetPerMinute ?? 0,
                surplusByItem.GetValueOrDefault(itemId),
                overCapacityItems.Contains(itemId),
                null);
        }

        foreach (string facilityId in facilityIds)
        {
            snapshot.FacilitiesById.TryGetValue(facilityId, out Facility? facility);
            string note = $"×{ceilByFacility.GetValueOrDefault(facilityId)}";
            int dispensers = dispenserByFacility.GetValueOrDefault(facilityId);
            if (dispensers > 0)
            {
                note += $"（散布機 {dispensers}）";
            }

            nodes[FacilityNodeId(facilityId)] = new FlowGraphNode(
                FacilityNodeId(facilityId), FlowGraphNodeKind.Facility,
                facility?.Name ?? facilityId, facility?.IconKey, facilityId,
                0, 0, false, 0, 0, 0, false, note);
        }

        if (edges.Any(e => e.Kind == FlowGraphEdgeKind.Gathered))
        {
            nodes[GatherNodeId] = new FlowGraphNode(
                GatherNodeId, FlowGraphNodeKind.Gather,
                "採取", null, null,
                0, 0, false, 0, 0, 0, false, null);
        }

        (Dictionary<string, int> rank, Dictionary<string, int> order) = AssignRanks(nodes, edges);
        var nodeList = nodes.Values
            .Select(n => n with { Rank = rank[n.Id], Order = order[n.Id] })
            .OrderBy(n => n.Rank)
            .ThenBy(n => n.Order)
            .ToList();

        edges = edges
            .OrderBy(e => e.FromId, StringComparer.Ordinal)
            .ThenBy(e => e.ToId, StringComparer.Ordinal)
            .ThenBy(e => e.Kind)
            .ToList();

        return new FlowGraphModel(
            nodeList,
            edges,
            edges.Count > 0 ? edges.Max(e => e.RatePerMinute) : 0);
    }

    private static void CollectEndpoint(
        string nodeId,
        SortedSet<string> itemIds,
        SortedSet<string> facilityIds)
    {
        if (nodeId.StartsWith(ItemPrefix, StringComparison.Ordinal))
        {
            itemIds.Add(nodeId[ItemPrefix.Length..]);
        }
        else if (nodeId.StartsWith(FacilityPrefix, StringComparison.Ordinal))
        {
            facilityIds.Add(nodeId[FacilityPrefix.Length..]);
        }
    }

    /// <summary>
    /// 最長パスで層割りし、ランク内順序を先行ノードの重心（バリセンター）で並べる。
    /// 循環の残存ノードは後退エッジを無視して確定済み先行の次層に置く。
    /// </summary>
    private static (Dictionary<string, int> Rank, Dictionary<string, int> Order) AssignRanks(
        IReadOnlyDictionary<string, FlowGraphNode> nodes,
        IReadOnlyList<FlowGraphEdge> edges)
    {
        var preds = nodes.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        var indegree = nodes.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var succs = nodes.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (FlowGraphEdge edge in edges)
        {
            if (!nodes.ContainsKey(edge.FromId) || !nodes.ContainsKey(edge.ToId))
            {
                continue;
            }

            preds[edge.ToId].Add(edge.FromId);
            succs[edge.FromId].Add(edge.ToId);
            indegree[edge.ToId]++;
        }

        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string id in nodes.Keys)
        {
            if (indegree[id] == 0)
            {
                queue.Add(id);
            }
        }

        while (queue.Count > 0)
        {
            string id = queue.Min!;
            queue.Remove(id);
            rank[id] = preds[id].Count == 0 ? 0 : 1 + preds[id].Max(p => rank.GetValueOrDefault(p, -1));
            foreach (string succ in succs[id])
            {
                if (--indegree[succ] == 0)
                {
                    queue.Add(succ);
                }
            }
        }

        // 循環の残り: 確定済み先行だけを見て次層へ置く（後退エッジはランク計算に使わない）。
        foreach (string id in nodes.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (rank.ContainsKey(id))
            {
                continue;
            }

            rank[id] = 1 + preds[id].Select(p => rank.GetValueOrDefault(p, -1)).DefaultIfEmpty(-1).Max();
        }

        // ランク内順序: 初回は Id 昇順、以降は先行ノード位置の平均（バリセンター）で並べ替える。
        var nodesByRank = nodes.Keys
            .GroupBy(id => rank[id])
            .ToDictionary(g => g.Key, g => g.OrderBy(id => id, StringComparer.Ordinal).ToList());
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (List<string> ids in nodesByRank.Values)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                order[ids[i]] = i;
            }
        }

        for (int pass = 0; pass < 4; pass++)
        {
            var barycenter = nodes.Keys.ToDictionary(
                id => id,
                id => preds[id].Count > 0
                    ? preds[id].Average(p => rank[p] * 1_000_000.0 + order[p])
                    : rank[id] * 1_000_000.0 + order[id],
                StringComparer.Ordinal);
            foreach (List<string> ids in nodesByRank.Values)
            {
                ids.Sort((a, b) =>
                {
                    int cmp = barycenter[a].CompareTo(barycenter[b]);
                    return cmp != 0 ? cmp : StringComparer.Ordinal.Compare(a, b);
                });
                for (int i = 0; i < ids.Count; i++)
                {
                    order[ids[i]] = i;
                }
            }
        }

        return (rank, order);
    }
}
