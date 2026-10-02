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
/// OverCapacity は設備への入力流量が輸送容量を超えたエッジに立つ（仕様決定 AN）。
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
/// 容量超過判定は設備への入力エッジ単位（仕様決定 AN）。
/// expandFacilities=true のとき設備を切上台数ぶんのユニットノードへ展開する（仕様決定 AO）。
/// </summary>
public static class FlowGraphModelBuilder
{
    private const double Epsilon = 1e-9;
    private const string ItemPrefix = "item:";
    private const string FacilityPrefix = "fac:";
    private const char UnitSeparator = '#';

    /// <summary>採取供給の共通ノード Id（計画に採取がある場合のみ存在する）。</summary>
    public const string GatherNodeId = "gather";

    public static string ItemNodeId(string itemId) => ItemPrefix + itemId;

    public static string FacilityNodeId(string facilityId) => FacilityPrefix + facilityId;

    /// <summary>台数分表示でのユニットノード Id（ユニット番号は 0 起き）。</summary>
    public static string UnitNodeId(string facilityId, int unitIndex) =>
        $"{FacilityPrefix}{facilityId}{UnitSeparator}{unitIndex}";

    public static FlowGraphModel Build(
        ProductionPlan plan,
        MasterDataSnapshot snapshot,
        ContextFilter context,
        IReadOnlyList<ProductionTarget> targets,
        bool unadjusted,
        bool expandFacilities = false)
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

        // 確定ペア（固定消費・ラン機械数の参照用）。レシピごと一意（ResultViewBuilder と同じ前提）。
        var pairByRecipe = plan.PairSelections
            .GroupBy(s => s.RecipeId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Pair, StringComparer.Ordinal);

        // 設備ユニットへの割当。容量超過判定がユニット単位の入力で行われるため常に構築する
        // （台数分表示で正常なら集約表示も正常、となるのが仕様決定 AO の基準）。
        var unitsByFacility = FacilityUnitLayout.Allocate(
            plan.RecipeRuns, plan.FacilityRequirements, plan.EnvironmentRequirements,
            snapshot, plan.PairSelections);

        var edgeParts = new List<FlowGraphEdge>();
        for (int runIndex = 0; runIndex < plan.RecipeRuns.Count; runIndex++)
        {
            RecipeRun run = plan.RecipeRuns[runIndex];
            if (!snapshot.RecipesById.TryGetValue(run.RecipeId, out Recipe? recipe))
            {
                continue;
            }

            double scale = unadjusted ? scales.GetValueOrDefault(run.FacilityId, 1.0) : 1.0;
            IReadOnlyList<(string NodeId, double Share)> runTargets =
                RunEdgeTargets(unitsByFacility, run.FacilityId, runIndex);
            foreach (RecipeInput input in recipe.Inputs)
            {
                foreach ((string nodeId, double share) in runTargets)
                {
                    edgeParts.Add(new FlowGraphEdge(
                        ItemNodeId(input.ItemId), nodeId, FlowGraphEdgeKind.RecipeInput,
                        run.CyclesPerMinute * scale * input.Quantity * share, false, false));
                }
            }

            foreach (RecipeOutput output in recipe.Outputs)
            {
                foreach ((string nodeId, double share) in runTargets)
                {
                    edgeParts.Add(new FlowGraphEdge(
                        nodeId, ItemNodeId(output.ItemId), FlowGraphEdgeKind.RecipeOutput,
                        run.CyclesPerMinute * scale * output.Quantity * share, output.SortOrder > 0, false));
                }
            }

            // 固定消費の乗数は最終切上台数（散布機分を含む FacilityRequirement.CeilCount）。
            // 台数分表示では全ユニットへ等量に分ける。
            RecipeFacility? pair = pairByRecipe.GetValueOrDefault(run.RecipeId)
                ?? recipe.Facilities.FirstOrDefault(p => p.FacilityId == run.FacilityId);
            if (pair?.FixedConsumption is { } fixedConsumption)
            {
                double total = fixedConsumption.RatePerMinute
                    * ceilByFacility.GetValueOrDefault(run.FacilityId);
                foreach ((string nodeId, double share) in FacilityShareTargets(unitsByFacility, run.FacilityId))
                {
                    edgeParts.Add(new FlowGraphEdge(
                        ItemNodeId(fixedConsumption.ItemId), nodeId,
                        FlowGraphEdgeKind.FixedConsumption, total * share, false, false));
                }
            }
        }

        foreach (EnvironmentRequirement env in plan.EnvironmentRequirements)
        {
            if (unitsByFacility.TryGetValue(env.ProviderFacilityId, out List<FacilityUnitSlot>? envUnits)
                && envUnits.Count > 1)
            {
                // その環境を担当する散布機ユニットへ等量に振り分ける。
                // 台数が合わない退化ケースでは全ユニットへ等量。
                List<FacilityUnitSlot> dispensers = envUnits
                    .Where(u => u.DispenserEnvironmentId == env.EnvironmentId)
                    .ToList();
                if (env.DispenserCount > 0 && dispensers.Count == env.DispenserCount)
                {
                    double perUnit = env.ConsumeRatePerMinuteTotal / env.DispenserCount;
                    foreach (FacilityUnitSlot unit in dispensers)
                    {
                        edgeParts.Add(new FlowGraphEdge(
                            ItemNodeId(env.ConsumeItemId),
                            UnitNodeId(env.ProviderFacilityId, unit.Index),
                            FlowGraphEdgeKind.EnvironmentConsume, perUnit, false, false));
                    }
                }
                else
                {
                    double perUnit = env.ConsumeRatePerMinuteTotal / envUnits.Count;
                    foreach (FacilityUnitSlot unit in envUnits)
                    {
                        edgeParts.Add(new FlowGraphEdge(
                            ItemNodeId(env.ConsumeItemId),
                            UnitNodeId(env.ProviderFacilityId, unit.Index),
                            FlowGraphEdgeKind.EnvironmentConsume, perUnit, false, false));
                    }
                }
                continue;
            }

            edgeParts.Add(new FlowGraphEdge(
                ItemNodeId(env.ConsumeItemId), FacilityNodeId(env.ProviderFacilityId),
                FlowGraphEdgeKind.EnvironmentConsume, env.ConsumeRatePerMinuteTotal, false, false));
        }

        foreach (ItemRequirement req in plan.ItemRequirements)
        {
            double gathered = req.Supplies
                .Where(s => s.Kind == SupplyKind.Gathered)
                .Sum(s => s.AmountPerMinute);
            if (gathered > Epsilon)
            {
                edgeParts.Add(new FlowGraphEdge(
                    GatherNodeId, ItemNodeId(req.ItemId), FlowGraphEdgeKind.Gathered,
                    gathered, false, false));
            }
        }

        string DisplayNodeId(string nodeId) => expandFacilities
            ? nodeId
            : nodeId.StartsWith(FacilityPrefix, StringComparison.Ordinal)
                ? FacilityNodeId(BaseFacilityId(nodeId))
                : nodeId;

        // ユニット単位で容量判定し、集約表示では構成エッジのいずれかが超過していれば引き継ぐ
        // （台数分表示と集約表示で判定結果が一致する、仕様決定 AN・AO）。
        var edges = edgeParts
            .Where(e => e.RatePerMinute > Epsilon)
            .GroupBy(e => (DisplayNodeId(e.FromId), DisplayNodeId(e.ToId), e.Kind))
            .Select(g => new FlowGraphEdge(
                g.Key.Item1, g.Key.Item2, g.Key.Item3,
                g.Sum(e => e.RatePerMinute),
                g.All(e => e.IsByproduct),
                g.Any(e => IsOverCapacity(e, snapshot))))
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

        // 超過エッジの両端ノード（アイテム・設備）を赤化対象とする（仕様決定 AN）。
        var overCapacityItems = new HashSet<string>(StringComparer.Ordinal);
        var overCapacityFacilities = new HashSet<string>(StringComparer.Ordinal);
        foreach (FlowGraphEdge edge in edges)
        {
            if (edge.OverCapacity)
            {
                overCapacityItems.Add(edge.FromId[ItemPrefix.Length..]);
                overCapacityFacilities.Add(edge.ToId);
            }
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
            if (expandFacilities
                && unitsByFacility.TryGetValue(facilityId, out List<FacilityUnitSlot>? units)
                && units.Count > 1)
            {
                foreach (FacilityUnitSlot unit in units)
                {
                    // ラン占有ユニットは占有率、散布機ユニットは役割を注記する。
                    string unitNote = unit.IsDispenser
                        ? "散布機"
                        : $"稼働 {Math.Round(unit.Used * 100)}%";
                    string unitNodeId = UnitNodeId(facilityId, unit.Index);
                    nodes[unitNodeId] = new FlowGraphNode(
                        unitNodeId, FlowGraphNodeKind.Facility,
                        facility?.Name ?? facilityId, facility?.IconKey, facilityId,
                        0, 0, false, 0, 0, 0,
                        overCapacityFacilities.Contains(unitNodeId), unitNote);
                }
                continue;
            }

            string note = $"×{ceilByFacility.GetValueOrDefault(facilityId)}";
            int dispensers = dispenserByFacility.GetValueOrDefault(facilityId);
            if (dispensers > 0)
            {
                note += $"（散布機 {dispensers}）";
            }

            string facilityNodeId = FacilityNodeId(facilityId);
            nodes[facilityNodeId] = new FlowGraphNode(
                facilityNodeId, FlowGraphNodeKind.Facility,
                facility?.Name ?? facilityId, facility?.IconKey, facilityId,
                0, 0, false, 0, 0, 0,
                overCapacityFacilities.Contains(facilityNodeId), note);
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
            facilityIds.Add(BaseFacilityId(nodeId));
        }
    }

    /// <summary>設備ノード Id から FacilityId を取り出す。ユニットノードは `#` 以降を落とす。</summary>
    private static string BaseFacilityId(string facilityNodeId)
    {
        string id = facilityNodeId[FacilityPrefix.Length..];
        int hash = id.IndexOf(UnitSeparator);
        return hash >= 0 ? id[..hash] : id;
    }

    /// <summary>設備への入力エッジの流量が輸送容量を超えるかの判定（仕様決定 AN）。</summary>
    private static bool IsOverCapacity(FlowGraphEdge edge, MasterDataSnapshot snapshot)
    {
        string itemId = edge.FromId[ItemPrefix.Length..];
        if (!snapshot.ItemsById.TryGetValue(itemId, out Item? item))
        {
            return false;
        }

        double cap = item.TransportKind switch
        {
            TransportKind.Belt => ProductionCalculator.BeltCapacityPerMinute,
            TransportKind.Pipe => ProductionCalculator.PipeCapacityPerMinute,
            _ => 0,
        };
        return cap > 0 && edge.RatePerMinute > cap + Epsilon;
    }

    /// <summary>ランの入出力エッジの宛先ユニットと流量比率。展開対象外は単一ノードへ share 1.0。</summary>
    private static IReadOnlyList<(string NodeId, double Share)> RunEdgeTargets(
        IReadOnlyDictionary<string, List<FacilityUnitSlot>> unitsByFacility,
        string facilityId,
        int runIndex)
    {
        if (unitsByFacility.TryGetValue(facilityId, out List<FacilityUnitSlot>? units) && units.Count > 1)
        {
            var targets = new List<(string, double)>();
            foreach (FacilityUnitSlot unit in units)
            {
                double share = unit.RunShares.GetValueOrDefault(runIndex);
                if (share > Epsilon)
                {
                    targets.Add((UnitNodeId(facilityId, unit.Index), share));
                }
            }
            return targets;
        }
        return [(FacilityNodeId(facilityId), 1.0)];
    }

    /// <summary>設備全体に掛かるエッジ（固定消費）の宛先ユニットと比率。展開時は等量分割。</summary>
    private static IReadOnlyList<(string NodeId, double Share)> FacilityShareTargets(
        IReadOnlyDictionary<string, List<FacilityUnitSlot>> unitsByFacility,
        string facilityId)
    {
        if (unitsByFacility.TryGetValue(facilityId, out List<FacilityUnitSlot>? units) && units.Count > 1)
        {
            double share = 1.0 / units.Count;
            return units.Select(u => (UnitNodeId(facilityId, u.Index), share)).ToList();
        }
        return [(FacilityNodeId(facilityId), 1.0)];
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
