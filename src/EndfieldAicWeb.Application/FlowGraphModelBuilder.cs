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

/// <summary>生産フローグラフ全体。MaxRatePerMinute は粒子速度の正規化に使う（密度は絶対流量で飽和、仕様決定 AP）。</summary>
public sealed record FlowGraphModel(
    IReadOnlyList<FlowGraphNode> Nodes,
    IReadOnlyList<FlowGraphEdge> Edges,
    double MaxRatePerMinute);

/// <summary>
/// ProductionPlan から生産フローグラフ（仕様決定 AI）の表示モデルを組み立てる。
/// エッジ流量は表示中ビューに合わせ、未調整ビューでは ResultViewBuilder と同じ設備倍率を掛ける。
/// 層割りは目標・未消費アイテムを Layer0 とする出口側起点の最長距離（仕様決定 BF）。
/// 循環依存（CycleDetected で打ち切られた残存経路を含む）は
/// 後退エッジとして層割りに使わずレイアウトだけを確定させる（仕様決定 BH）。
/// 容量超過判定は設備への入力エッジ単位（仕様決定 AN）。
/// expandFacilities=true のとき設備を切上台数ぶんのユニットノードへ展開する（仕様決定 AO）。
/// </summary>
public static class FlowGraphModelBuilder
{
    private const double Epsilon = 1e-9;
    private const string ItemPrefix = "item:";
    private const string FacilityPrefix = "fac:";
    private const string UnitPrefix = "facunit:";

    /// <summary>採取供給の共通ノード Id（計画に採取がある場合のみ存在する）。</summary>
    public const string GatherNodeId = "gather";

    public static string ItemNodeId(string itemId) => ItemPrefix + itemId;

    public static string FacilityNodeId(string facilityId) => FacilityPrefix + facilityId;

    /// <summary>
    /// 台数分表示でのユニットノード Id（ユニット番号は 0 起き）。
    /// 設備ノードと別のプレフィックスにし、FacilityId に `#` が含まれても衝突しない形にする。
    /// </summary>
    public static string UnitNodeId(string facilityId, int unitIndex) =>
        $"{UnitPrefix}{facilityId}#{unitIndex}";

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
        // 未調整ビューでは実機械の全速稼働を表すよう、設備倍率を掛けた機械数で再割当する。
        var unitsByFacility = FacilityUnitLayout.Allocate(
            plan.RecipeRuns, plan.FacilityRequirements, plan.EnvironmentRequirements,
            snapshot, plan.PairSelections, unadjusted ? scales : null);

        // ユニットノード Id から FacilityId への引き戻し。Id を文字列分割で解釈せず明示的に持つ
        // （FacilityId に `#` が含まれても誤って別設備へ解決されない）。
        var unitFacility = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string facilityId, List<FacilityUnitSlot> units) in unitsByFacility)
        {
            foreach (FacilityUnitSlot unit in units)
            {
                unitFacility[UnitNodeId(facilityId, unit.Index)] = facilityId;
            }
        }

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
            : unitFacility.TryGetValue(nodeId, out string? facilityId)
                ? FacilityNodeId(facilityId)
                : nodeId;

        // 容量判定はユニットごとの同一アイテム入力合計で行う（計算本体の警告と同じ集計、仕様決定 AN）。
        // ランをまたいだ入力や固定消費との合算で容量を超える場合も取りこぼさない。
        var inputTotals = new Dictionary<(string FromId, string ToId), double>();
        foreach (FlowGraphEdge part in edgeParts)
        {
            if (IsFacilityInput(part, unitFacility))
            {
                (string, string) key = (part.FromId, part.ToId);
                inputTotals[key] = inputTotals.GetValueOrDefault(key) + part.RatePerMinute;
            }
        }

        var overInputs = new HashSet<(string FromId, string ToId)>();
        foreach (((string fromId, string toId), double total) in inputTotals)
        {
            double cap = TransportCapacityOf(fromId, snapshot);
            if (cap > 0 && total > cap + Epsilon)
            {
                overInputs.Add((fromId, toId));
            }
        }

        // 集約表示では構成エッジのいずれかが超過していれば引き継ぐ
        // （台数分表示と集約表示で判定結果が一致する、仕様決定 AN・AO）。
        var edges = edgeParts
            .Where(e => e.RatePerMinute > Epsilon)
            .GroupBy(e => (DisplayNodeId(e.FromId), DisplayNodeId(e.ToId), e.Kind))
            .Select(g => new FlowGraphEdge(
                g.Key.Item1, g.Key.Item2, g.Key.Item3,
                g.Sum(e => e.RatePerMinute),
                g.All(e => e.IsByproduct),
                g.Any(e => overInputs.Contains((e.FromId, e.ToId)))))
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
            CollectEndpoint(edge.FromId, itemIds, facilityIds, unitFacility);
            CollectEndpoint(edge.ToId, itemIds, facilityIds, unitFacility);
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
        SortedSet<string> facilityIds,
        IReadOnlyDictionary<string, string> unitFacility)
    {
        if (unitFacility.TryGetValue(nodeId, out string? facilityId))
        {
            facilityIds.Add(facilityId);
        }
        else if (nodeId.StartsWith(ItemPrefix, StringComparison.Ordinal))
        {
            itemIds.Add(nodeId[ItemPrefix.Length..]);
        }
        else if (nodeId.StartsWith(FacilityPrefix, StringComparison.Ordinal))
        {
            facilityIds.Add(nodeId[FacilityPrefix.Length..]);
        }
    }

    /// <summary>設備ノードまたはユニットノードへの入力エッジかの判定。</summary>
    private static bool IsFacilityInput(
        FlowGraphEdge edge,
        IReadOnlyDictionary<string, string> unitFacility) =>
        edge.FromId.StartsWith(ItemPrefix, StringComparison.Ordinal)
        && (unitFacility.ContainsKey(edge.ToId)
            || edge.ToId.StartsWith(FacilityPrefix, StringComparison.Ordinal));

    /// <summary>アイテムの輸送容量（ベルト 30 個/分・パイプ 60 個/分。対象外は 0）。</summary>
    private static double TransportCapacityOf(string itemNodeId, MasterDataSnapshot snapshot)
    {
        string itemId = itemNodeId[ItemPrefix.Length..];
        if (!snapshot.ItemsById.TryGetValue(itemId, out Item? item))
        {
            return 0;
        }

        return item.TransportKind switch
        {
            TransportKind.Belt => ProductionCalculator.BeltCapacityPerMinute,
            TransportKind.Pipe => ProductionCalculator.PipeCapacityPerMinute,
            _ => 0,
        };
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
    /// 出口側起点の最長距離で層割りし、ランク内順序を先行ノードの重心（バリセンター）で並べる。
    /// 層は Layer0（目標・未消費アイテム）から上流へ遡る距離で、同じノードが複数の層に
    /// 出る場合は大きい層にまとめる（仕様決定 BF）。消費される目標もこの規則に従う（BG）。
    /// 循環の残存ノードは後退エッジを無視し、確定済みの後続だけで層を決める（BH）。
    /// 複数の出口を持つ循環では、自分自身を経由しない後続からより深い層が届く限り
    /// 層を緩和して最深へ引き直す。散布機など出力を持たない設備ノードは層割りの
    /// 対象外とし、消費アイテムの直下流に置く。表示は Layer0 を右端列とするため、
    /// 返すランクは最大層からの反転値。
    /// </summary>
    private static (Dictionary<string, int> Rank, Dictionary<string, int> Order) AssignRanks(
        IReadOnlyDictionary<string, FlowGraphNode> nodes,
        IReadOnlyList<FlowGraphEdge> edges)
    {
        var preds = nodes.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        var succs = nodes.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (FlowGraphEdge edge in edges)
        {
            if (!nodes.ContainsKey(edge.FromId) || !nodes.ContainsKey(edge.ToId))
            {
                continue;
            }

            preds[edge.ToId].Add(edge.FromId);
            succs[edge.FromId].Add(edge.ToId);
        }

        // 出力を持たない設備などの終端ノードへのエッジは層割りに使わない。
        // 終端ノードは最後に、最も深い消費アイテムの直下流へ置く。
        var terminals = new HashSet<string>(
            nodes.Keys.Where(id => nodes[id].Kind != FlowGraphNodeKind.Item && succs[id].Count == 0),
            StringComparer.Ordinal);
        var effSuccs = nodes.Keys.ToDictionary(
            id => id,
            id => succs[id].Where(s => !terminals.Contains(s)).ToList(),
            StringComparer.Ordinal);

        // Layer0 の起点: 目標アイテム、およびどの設備にも消費されない（下流エッジのない）
        // アイテム（未消費の副産物や余剰を含む）。採取ノードは最深の供給先の次層に沈む。
        bool IsAnchor(string id) =>
            nodes[id].Kind == FlowGraphNodeKind.Item
            && (nodes[id].IsTarget || effSuccs[id].Count == 0);

        // 下流側が確定した順に層を確定する。pending は未確定の後続ノード数。
        var layer = new Dictionary<string, int>(StringComparer.Ordinal);
        var pending = nodes.Keys.ToDictionary(id => id, id => effSuccs[id].Count, StringComparer.Ordinal);
        var queue = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string id in nodes.Keys)
        {
            if (pending[id] == 0 && !terminals.Contains(id))
            {
                queue.Add(id);
            }
        }

        int MaxAssignedSucc(string id, out string? via)
        {
            via = null;
            int best = -1;
            foreach (string s in effSuccs[id])
            {
                if (layer.TryGetValue(s, out int l) && l > best)
                {
                    best = l;
                    via = s;
                }
            }

            return best;
        }

        while (queue.Count > 0)
        {
            string id = queue.Min!;
            queue.Remove(id);
            layer[id] = 1 + MaxAssignedSucc(id, out _);
            foreach (string pred in preds[id])
            {
                if (--pending[pred] == 0)
                {
                    queue.Add(pred);
                }
            }
        }

        // 循環の残り: 起点を種に、確定済みの後続だけを見て層を決める（後退エッジは使わない）。
        var deferred = nodes.Keys
            .Where(id => !layer.ContainsKey(id) && !terminals.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        var deferredSet = new HashSet<string>(deferred, StringComparer.Ordinal);
        // support: そのノードの層がどの循環内後続を経由して決まったか。緩和で自分自身を
        // 経由する後続を除外し、後退エッジを逆流させないために使う。
        var support = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        while (deferred.Count > 0)
        {
            var next = new List<string>();
            foreach (string id in deferred)
            {
                // 起点でなく後続も未確定のノードは、循環の向こう側へ展開が戻るまで待つ。
                if (!IsAnchor(id) && !effSuccs[id].Any(layer.ContainsKey))
                {
                    next.Add(id);
                    continue;
                }

                layer[id] = 1 + MaxAssignedSucc(id, out string? via);
                support[id] = via is not null && deferredSet.Contains(via)
                    ? new HashSet<string>(support[via], StringComparer.Ordinal) { via }
                    : new HashSet<string>(StringComparer.Ordinal);
            }

            if (next.Count == deferred.Count)
            {
                // 起点に届かない閉じた循環（通常は発生しない）。確定済み後続だけで打ち切る。
                foreach (string id in next)
                {
                    layer[id] = 1 + MaxAssignedSucc(id, out _);
                    support[id] = new HashSet<string>(StringComparer.Ordinal);
                }
                break;
            }

            deferred = next;
        }

        // 緩和: 複数の出口を持つ循環では、仮確定より深い層が後続から届くことがある。
        // 自分自身を経由しない後続（support に含まれない）からの更新だけを取り込み、
        // 最長距離が確定するまで繰り返す。Layer0 の起点は固定のまま動かさない。
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (string id in deferredSet)
            {
                if (IsAnchor(id))
                {
                    continue;
                }

                foreach (string succ in effSuccs[id])
                {
                    if (!deferredSet.Contains(succ) || support[succ].Contains(id))
                    {
                        continue;
                    }

                    int candidate = 1 + layer[succ];
                    if (candidate > layer[id])
                    {
                        layer[id] = candidate;
                        support[id] = new HashSet<string>(support[succ], StringComparer.Ordinal) { succ };
                        changed = true;
                    }
                }
            }
        }

        // 終端ノード（散布機など出力を持たない設備）は最も深い消費アイテムの直下流に置く。
        foreach (string id in terminals)
        {
            layer[id] = preds[id]
                .Where(layer.ContainsKey)
                .Select(p => layer[p])
                .DefaultIfEmpty(1)
                .Max() - 1;
        }

        int maxLayer = layer.Count > 0 ? layer.Values.Max() : 0;
        var rank = nodes.Keys.ToDictionary(
            id => id, id => maxLayer - layer[id], StringComparer.Ordinal);

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
