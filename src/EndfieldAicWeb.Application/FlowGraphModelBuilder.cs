using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application;

/// <summary>生産フローグラフのノード種別（仕様決定 AI）。</summary>
public enum FlowGraphNodeKind
{
    Item,
    Facility,
}

/// <summary>生産フローグラフのエッジ種別。</summary>
public enum FlowGraphEdgeKind
{
    RecipeInput,
    RecipeOutput,
    FixedConsumption,
    EnvironmentConsume,
}

/// <summary>
/// グラフのノード。描画側がラベル・アイコン・強調をそのまま使えるよう表示用の値を保持する。
/// RefId はリスト行へのスクロール対象（ItemId または FacilityId）。
/// GatheredPerMinute は計画内の採取供給量（採取ノード廃止後の緑化判定、仕様決定 BO）。
/// IsSpecifiedBase・ExternalProcuredPerMinute は基礎素材指定による外部調達（青色ノードと
/// メタ行の併記、仕様決定 DB）。
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
    double GatheredPerMinute,
    bool IsSpecifiedBase,
    double ExternalProcuredPerMinute,
    string? Note,
    IReadOnlyList<FlowGraphDisposal> Disposals);

/// <summary>
/// アイテムノードへ持たせる処理消費 1 件（仕様決定 CD）。
/// Count は計画の設備要件の切上げ台数（兼用設備の処理分だけの按分はしない、暫定解釈 4）。
/// </summary>
public sealed record FlowGraphDisposal(
    string FacilityId,
    string FacilityName,
    int Count,
    double PerMinute);

/// <summary>
/// グラフのエッジ。同一ノード対・同種別は流量を合算して 1 本にまとめる。
/// </summary>
public sealed record FlowGraphEdge(
    string FromId,
    string ToId,
    FlowGraphEdgeKind Kind,
    double RatePerMinute,
    bool IsByproduct);

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
/// expandFacilities=true のとき設備を切上台数ぶんのユニットノードへ展開する（仕様決定 AO）。
/// 出力を持たない環境供給設備（散布機）は利用設備の最小層へ固定してから再層割りする
/// （仕様決定 BM）。採取供給は共通ノードを持たずアイテムノードの属性として保持する
/// （仕様決定 BO）。
/// </summary>
public static partial class FlowGraphModelBuilder
{
    private const double Epsilon = 1e-9;
    private const string ItemPrefix = "item:";
    private const string FacilityPrefix = "fac:";
    private const string UnitPrefix = "facunit:";

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
        bool expandFacilities = false,
        IReadOnlyCollection<string>? specifiedBaseItemIds = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(targets);

        // 基礎素材指定のアイテム集合。呼び出し側（計算パネル）が計算へ渡した指定をそのまま渡す（仕様決定 CZ）。
        var specifiedBaseItems = new HashSet<string>(specifiedBaseItemIds ?? [], StringComparer.Ordinal);

        // 未調整ビューの倍率はラン単位（環境ランはカバー配分で個別に絞られるため、BR）。
        IReadOnlyList<double> runScales = unadjusted
            ? ResultViewBuilder.ComputeUnadjustedRunScales(plan, snapshot)
            : [];

        // 処理ラン（出力なしレシピのラン）はエッジ生成・ユニット割当の対象外とし、
        // そのランのみを占有する設備は設備ノードを持たない（仕様決定 CD）。
        var disposalRunIndex = new bool[plan.RecipeRuns.Count];
        for (int i = 0; i < plan.RecipeRuns.Count; i++)
        {
            disposalRunIndex[i] =
                snapshot.RecipesById.TryGetValue(plan.RecipeRuns[i].RecipeId, out Recipe? disposalRecipe)
                && disposalRecipe.Outputs.Count == 0;
        }

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

        // 設備ユニットへの割当。エッジのユニット宛分割と台数分表示のノード展開に使うため
        // 常に構築する。未調整ビューでは実機械の全速稼働を表すよう、設備倍率を掛けた
        // 機械数で再割当する。
        var unitsByFacility = FacilityUnitLayout.Allocate(
            plan.RecipeRuns, plan.FacilityRequirements, plan.EnvironmentRequirements,
            snapshot, plan.PairSelections, unadjusted ? runScales : null);

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

        // 環境供給設備の表示ノード → その環境を利用する設備の表示ノード群（仕様決定 BM）。
        // 「利用設備」は選択ペアの EnvironmentId が当該環境と一致するランを持つ設備で、
        // ラン→ペアの対応は固定消費と同じく pairByRecipe（recipe.Facilities への
        // フォールバック付き）で引く。利用設備が 0 件の環境は層割り側で従来規則へ退避する。
        // 消費者側の表示ノードはビューに従う: 台数分表示ではランを占有するユニットノード、
        // 集約表示では設備ノード。供給側も台数分表示ではその環境の散布機ユニット、
        // 集約表示では設備ノード（担う全環境の利用設備を合わせる）。
        var envConsumers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (EnvironmentRequirement env in plan.EnvironmentRequirements)
        {
            var users = new List<string>();
            for (int runIndex = 0; runIndex < plan.RecipeRuns.Count; runIndex++)
            {
                // 処理ランの設備はノードを持たないため利用設備には数えない（CD）。
                if (disposalRunIndex[runIndex])
                {
                    continue;
                }

                RecipeRun run = plan.RecipeRuns[runIndex];
                // ランに保持されたペアを最優先にする（処理ランは PairSelections に載らない、CC）。
                RecipeFacility? runPair = run.Pair ?? pairByRecipe.GetValueOrDefault(run.RecipeId);
                if (runPair is null
                    && snapshot.RecipesById.TryGetValue(run.RecipeId, out Recipe? runRecipe))
                {
                    runPair = runRecipe.Facilities.FirstOrDefault(
                        p => p.FacilityId == run.FacilityId);
                }

                if (runPair?.EnvironmentId != env.EnvironmentId)
                {
                    continue;
                }

                if (expandFacilities
                    && unitsByFacility.TryGetValue(run.FacilityId, out List<FacilityUnitSlot>? runUnits)
                    && runUnits.Count > 1)
                {
                    users.AddRange(runUnits
                        .Where(u => u.RunShares.ContainsKey(runIndex))
                        .Select(u => UnitNodeId(run.FacilityId, u.Index)));
                }
                else
                {
                    users.Add(FacilityNodeId(run.FacilityId));
                }
            }

            List<string> providerNodes =
                expandFacilities
                && unitsByFacility.TryGetValue(env.ProviderFacilityId, out List<FacilityUnitSlot>? providerUnits)
                && providerUnits.Count > 1
                    ? providerUnits
                        .Where(u => u.DispenserEnvironmentId == env.EnvironmentId)
                        .Select(u => UnitNodeId(env.ProviderFacilityId, u.Index))
                        .ToList()
                    : [FacilityNodeId(env.ProviderFacilityId)];

            foreach (string providerNode in providerNodes)
            {
                if (envConsumers.TryGetValue(providerNode, out List<string>? consumers))
                {
                    consumers.AddRange(users);
                }
                else
                {
                    envConsumers[providerNode] = new List<string>(users);
                }
            }
        }

        var edgeParts = new List<FlowGraphEdge>();
        for (int runIndex = 0; runIndex < plan.RecipeRuns.Count; runIndex++)
        {
            RecipeRun run = plan.RecipeRuns[runIndex];
            if (disposalRunIndex[runIndex]
                || !snapshot.RecipesById.TryGetValue(run.RecipeId, out Recipe? recipe))
            {
                continue;
            }

            double scale = unadjusted && runIndex < runScales.Count ? runScales[runIndex] : 1.0;
            IReadOnlyList<(string NodeId, double Share)> runTargets =
                RunEdgeTargets(unitsByFacility, run.FacilityId, runIndex);
            foreach (RecipeInput input in recipe.Inputs)
            {
                foreach ((string nodeId, double share) in runTargets)
                {
                    edgeParts.Add(new FlowGraphEdge(
                        ItemNodeId(input.ItemId), nodeId, FlowGraphEdgeKind.RecipeInput,
                        run.CyclesPerMinute * scale * input.Quantity * share, false));
                }
            }

            foreach (RecipeOutput output in recipe.Outputs)
            {
                foreach ((string nodeId, double share) in runTargets)
                {
                    edgeParts.Add(new FlowGraphEdge(
                        nodeId, ItemNodeId(output.ItemId), FlowGraphEdgeKind.RecipeOutput,
                        run.CyclesPerMinute * scale * output.Quantity * share, output.SortOrder > 0));
                }
            }

            // 固定消費の乗数は最終切上台数（散布機分を含む FacilityRequirement.CeilCount）。
            // 台数分表示では全ユニットへ等量に分ける。ラン保持ペアを最優先に引く（CC）。
            RecipeFacility? pair = run.Pair
                ?? pairByRecipe.GetValueOrDefault(run.RecipeId)
                ?? recipe.Facilities.FirstOrDefault(p => p.FacilityId == run.FacilityId);
            if (pair?.FixedConsumption is { } fixedConsumption)
            {
                double total = fixedConsumption.RatePerMinute
                    * ceilByFacility.GetValueOrDefault(run.FacilityId);
                foreach ((string nodeId, double share) in FacilityShareTargets(unitsByFacility, run.FacilityId))
                {
                    edgeParts.Add(new FlowGraphEdge(
                        ItemNodeId(fixedConsumption.ItemId), nodeId,
                        FlowGraphEdgeKind.FixedConsumption, total * share, false));
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
                            FlowGraphEdgeKind.EnvironmentConsume, perUnit, false));
                    }
                }
                else
                {
                    // 退化ケースも表示対象のユニットへだけ分ける（ノードの無い空ユニットは宛先にしない）。
                    List<FacilityUnitSlot> shownUnits = envUnits
                        .Where(u => u.IsDispenser || u.Used > Epsilon)
                        .ToList();
                    if (shownUnits.Count > 0)
                    {
                        double perUnit = env.ConsumeRatePerMinuteTotal / shownUnits.Count;
                        foreach (FacilityUnitSlot unit in shownUnits)
                        {
                            edgeParts.Add(new FlowGraphEdge(
                                ItemNodeId(env.ConsumeItemId),
                                UnitNodeId(env.ProviderFacilityId, unit.Index),
                                FlowGraphEdgeKind.EnvironmentConsume, perUnit, false));
                        }
                    }
                }
                continue;
            }

            edgeParts.Add(new FlowGraphEdge(
                ItemNodeId(env.ConsumeItemId), FacilityNodeId(env.ProviderFacilityId),
                FlowGraphEdgeKind.EnvironmentConsume, env.ConsumeRatePerMinuteTotal, false));
        }

        string DisplayNodeId(string nodeId) => expandFacilities
            ? nodeId
            : unitFacility.TryGetValue(nodeId, out string? facilityId)
                ? FacilityNodeId(facilityId)
                : nodeId;

        // 同一ノード対・同種別のエッジは流量を合算して 1 本にまとめる。
        var edges = edgeParts
            .Where(e => e.RatePerMinute > Epsilon)
            .GroupBy(e => (DisplayNodeId(e.FromId), DisplayNodeId(e.ToId), e.Kind))
            .Select(g => new FlowGraphEdge(
                g.Key.Item1, g.Key.Item2, g.Key.Item3,
                g.Sum(e => e.RatePerMinute),
                g.All(e => e.IsByproduct)))
            .ToList();

        // 処理消費（アイテム → 消費ラン内訳）。調整済は計画値、未調整はラン倍率でスケールし
        // 利用可能量内にクランプした量（暫定解釈 8）。アイテムノードの紫化とメタ行に使う。
        IReadOnlyDictionary<string, List<(int RunIndex, double PerMinute)>> disposalByItem;
        IReadOnlyList<SurplusProduction> surpluses;
        if (unadjusted)
        {
            (surpluses, disposalByItem) = ResultViewBuilder.ComputeUnadjustedDisposalAndSurpluses(
                plan, snapshot, context, runScales);
        }
        else
        {
            surpluses = plan.Surpluses;
            disposalByItem = ResultViewBuilder.ComputeDisposalShown(plan, snapshot);
        }

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

        // 処理ランのみを占有する設備は設備ノードを持たない（CD）。
        // 生産ランや散布機を兼ねる設備は従来どおりノードを持つ。
        var productionFacilityIds = new HashSet<string>(StringComparer.Ordinal);
        var disposalFacilityIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < plan.RecipeRuns.Count; i++)
        {
            (disposalRunIndex[i] ? disposalFacilityIds : productionFacilityIds)
                .Add(plan.RecipeRuns[i].FacilityId);
        }

        foreach (FacilityRequirement f in plan.FacilityRequirements)
        {
            if (!productionFacilityIds.Contains(f.FacilityId)
                && disposalFacilityIds.Contains(f.FacilityId)
                && dispenserByFacility.GetValueOrDefault(f.FacilityId) <= 0)
            {
                continue;
            }

            facilityIds.Add(f.FacilityId);
        }

        foreach (FlowGraphEdge edge in edges)
        {
            CollectEndpoint(edge.FromId, itemIds, facilityIds, unitFacility);
            CollectEndpoint(edge.ToId, itemIds, facilityIds, unitFacility);
        }

        // ノード確定（ランク付け前の素集合）。
        var targetItemIds = targets.Select(t => t.ItemId).ToHashSet(StringComparer.Ordinal);
        var nodes = new Dictionary<string, FlowGraphNode>(StringComparer.Ordinal);
        foreach (string itemId in itemIds)
        {
            snapshot.ItemsById.TryGetValue(itemId, out Item? item);
            requirementByItem.TryGetValue(itemId, out ItemRequirement? req);
            // 採取供給は共通ノードではなくアイテムノードの属性として保持する（仕様決定 BO）。
            double gatheredPerMinute = req is null
                ? 0
                : req.Supplies
                    .Where(s => s.Kind == SupplyKind.Gathered)
                    .Sum(s => s.AmountPerMinute);
            // 基礎素材指定の外部調達も同じくノード属性（仕様決定 DB）。
            double externalProcuredPerMinute = req is null
                ? 0
                : req.Supplies
                    .Where(s => s.Kind == SupplyKind.ExternalProcurement)
                    .Sum(s => s.AmountPerMinute);
            // 処理消費のあるアイテムは処理設備・台数・処理量を保持する（CD）。
            IReadOnlyList<FlowGraphDisposal> disposals = [];
            if (disposalByItem.TryGetValue(itemId, out List<(int RunIndex, double PerMinute)>? entries))
            {
                disposals = entries
                    .Select(e =>
                    {
                        RecipeRun run = plan.RecipeRuns[e.RunIndex];
                        snapshot.FacilitiesById.TryGetValue(run.FacilityId, out Facility? dfac);
                        return new FlowGraphDisposal(
                            run.FacilityId,
                            dfac?.Name ?? run.FacilityId,
                            ceilByFacility.GetValueOrDefault(run.FacilityId),
                            e.PerMinute);
                    })
                    .ToList();
            }

            nodes[ItemNodeId(itemId)] = new FlowGraphNode(
                ItemNodeId(itemId), FlowGraphNodeKind.Item,
                item?.Name ?? itemId, item?.IconKey, itemId,
                0, 0,
                targetItemIds.Contains(itemId),
                req?.RequiredPerMinute ?? 0,
                req?.UnmetPerMinute ?? 0,
                surplusByItem.GetValueOrDefault(itemId),
                gatheredPerMinute,
                specifiedBaseItems.Contains(itemId),
                externalProcuredPerMinute,
                null,
                disposals);
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
                    // 出力なしランは割当対象外のため、占有なし・非散布機のユニットは
                    // 処理分として実体化しない（CD）。兼用設備は生産・散布機分のユニットのみ出る。
                    if (!unit.IsDispenser && unit.Used <= Epsilon)
                    {
                        continue;
                    }

                    // ラン占有ユニットは占有率、散布機ユニットは役割を注記する。
                    string unitNote = unit.IsDispenser
                        ? "散布機"
                        : $"稼働 {Math.Round(unit.Used * 100)}%";
                    string unitNodeId = UnitNodeId(facilityId, unit.Index);
                    nodes[unitNodeId] = new FlowGraphNode(
                        unitNodeId, FlowGraphNodeKind.Facility,
                        facility?.Name ?? facilityId, facility?.IconKey, facilityId,
                        0, 0, false, 0, 0, 0, 0, false, 0, unitNote, []);
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
                0, 0, false, 0, 0, 0, 0, false, 0, note, []);
        }

        (Dictionary<string, int> rank, Dictionary<string, int> order) = AssignRanks(
            nodes, edges, envConsumers);
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
            // 表示対象のユニット（占有または散布機）だけへ分ける。処理分として実体化しない
            // 空ユニットを宛先にするとノードが無く吊るしになる（CD）。
            List<FacilityUnitSlot> shown = units
                .Where(u => u.IsDispenser || u.Used > Epsilon)
                .ToList();
            if (shown.Count > 0)
            {
                double share = 1.0 / shown.Count;
                return shown.Select(u => (UnitNodeId(facilityId, u.Index), share)).ToList();
            }
        }
        return [(FacilityNodeId(facilityId), 1.0)];
    }
}
