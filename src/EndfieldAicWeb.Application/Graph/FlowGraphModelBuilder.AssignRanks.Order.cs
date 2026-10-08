namespace EndfieldAicWeb.Application.Graph;

public static partial class FlowGraphModelBuilder
{
    /// <summary>
    /// 順序段（仕様決定 CR・CS）。ランク内順序を双方向バリセンター掃引の候補探索と
    /// 隣接ペア入替後処理で決め、order（ランク内の順序番号）を返す。
    /// </summary>
    private static Dictionary<string, int> OrderNodesWithinRanks(
        IReadOnlyDictionary<string, FlowGraphNode> nodes,
        IReadOnlyList<FlowGraphEdge> edges,
        IReadOnlyDictionary<string, int> rank,
        IReadOnlyDictionary<string, int> envFixed,
        IReadOnlyDictionary<string, List<string>> virtualPreds)
    {
        // ランク内順序（仕様決定 CR）: 初回は Id 昇順。上流側（先行ノード位置）と
        // 下流側（後続ノード位置）のバリセンター掃引を交互に行い、掃引で確定した
        // 各順序候補の交差数を計測して最小の順序を採用する。バリセンターは加重平均で、
        // 目標アイテムへの出力エッジに支配的重みを付ける。同率は Id 昇順（従来どおり）。
        var nodesByRank = nodes.Keys
            .GroupBy(id => rank[id])
            .ToDictionary(g => g.Key, g => g.OrderBy(id => id, StringComparer.Ordinal).ToList());
        var order = new Dictionary<string, int>(StringComparer.Ordinal);

        RefreshOrder(nodesByRank, order);

        // 順序付けに使う描画エッジ（両端ノードが存在するものだけ層割りと同じ前提で残す）。
        var drawnEdges = edges
            .Where(e => nodes.ContainsKey(e.FromId) && nodes.ContainsKey(e.ToId))
            .ToList();

        // 各ノードに接続する描画エッジ数（そのエッジ自身を含む度）。目標出力エッジの
        // 重みは両端点の度の大きい方で、両端点の加重平均において他の全隣接エッジ
        // （重み 1・度−1 本）の合計を必ず上回る最小の整数になる（仕様決定 CR）。
        var degree = nodes.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (FlowGraphEdge edge in drawnEdges)
        {
            degree[edge.FromId]++;
            degree[edge.ToId]++;
        }

        int EdgeWeight(FlowGraphEdge edge) =>
            edge.Kind == FlowGraphEdgeKind.RecipeOutput && nodes[edge.ToId].IsTarget
                ? Math.Max(degree[edge.FromId], degree[edge.ToId])
                : 1;

        // 上流側の隣接集合は従来どおり preds に環境供給設備の仮想先行（最小層を取った
        // 利用設備、仕様決定 BM）を重み 1 で加える。下流側は終端宛を含む全描画
        // エッジの宛先を使う（層割りで除外した終端宛エッジも描画対象のため順序付け
        // に含める）。
        var upNeighbors = nodes.Keys.ToDictionary(
            id => id, _ => new List<(string Id, double Weight)>(), StringComparer.Ordinal);
        var downNeighbors = nodes.Keys.ToDictionary(
            id => id, _ => new List<(string Id, double Weight)>(), StringComparer.Ordinal);
        foreach (FlowGraphEdge edge in drawnEdges)
        {
            double w = EdgeWeight(edge);
            upNeighbors[edge.ToId].Add((edge.FromId, w));
            downNeighbors[edge.FromId].Add((edge.ToId, w));
        }

        foreach ((string id, List<string> extra) in virtualPreds)
        {
            foreach (string p in extra)
            {
                upNeighbors[id].Add((p, 1.0));
            }
        }

        // 掃引で採用する候補を選ぶ（仕様決定 CR）。従来方式と初期順序＋隣接挿入の
        // 順序も候補に含め、採用順序の交差数が従来方式を上回らないことを保証する。
        (Dictionary<int, List<string>> bestByRank, int adoptedCrossings) = FindBestOrderByRank(
            nodes, nodesByRank, order, rank, drawnEdges, upNeighbors, downNeighbors,
            envFixed, virtualPreds);

        // 最良候補を採用して order を確定する。
        nodesByRank = bestByRank;
        RefreshOrder(nodesByRank, order);

        // 隣接ペア入替の後処理（仕様決定 CS）。
        ApplyAdjacentPairSwaps(nodesByRank, order, rank, drawnEdges, envFixed, virtualPreds,
            adoptedCrossings);

        return order;
    }

    /// <summary>
    /// 順序候補の探索（仕様決定 CR）。従来方式（上流のみ・重みなし掃引の固定 4 パス）と
    /// 初期順序＋BM 隣接挿入、双方向バリセンター掃引の各パス後の順序を候補として評価し、
    /// 交差数最小（同数は目標出力エッジ両端の順序番号差が最小、同量は先出し）の順序を返す。
    /// </summary>
    private static (Dictionary<int, List<string>> BestByRank, int BestCrossings) FindBestOrderByRank(
        IReadOnlyDictionary<string, FlowGraphNode> nodes,
        Dictionary<int, List<string>> nodesByRank,
        Dictionary<string, int> order,
        IReadOnlyDictionary<string, int> rank,
        IReadOnlyList<FlowGraphEdge> drawnEdges,
        IReadOnlyDictionary<string, List<(string Id, double Weight)>> upNeighbors,
        IReadOnlyDictionary<string, List<(string Id, double Weight)>> downNeighbors,
        IReadOnlyDictionary<string, int> envFixed,
        IReadOnlyDictionary<string, List<string>> virtualPreds)
    {
        var bestByRank = new Dictionary<int, List<string>>();
        int bestCrossings = -1;
        double bestGap = -1;

        // 現在の順序を候補として評価する。採用は交差数最小・同数は第 2 キー最小・
        // 同量は先に評価した候補を維持する。
        void EvaluateCandidate()
        {
            int crossings = CountCrossings(drawnEdges, nodesByRank, rank, order);
            double gap = TargetOutputGap(drawnEdges, nodes, order);
            if (bestCrossings < 0 || crossings < bestCrossings
                || (crossings == bestCrossings && gap < bestGap))
            {
                bestCrossings = crossings;
                bestGap = gap;
                bestByRank = SnapshotOrder(nodesByRank);
            }
        }

        // 初期順序はランク内の Id 昇順（仕様決定 CR）。BM の隣接挿入は各パスの
        // ソート後に適用するため、初期順序そのものには含めない。
        Dictionary<int, List<string>> initialByRank = SnapshotOrder(nodesByRank);

        // 従来方式（上流側のみ・重みなしのバリセンター掃引と BM 隣接挿入を固定
        // 4 パスで行った順序）を最初の候補として評価する。交差数と順序番号差が
        // 完全に並んだときは従来の配置が維持される（仕様決定 CR）。逆方向
        // タイブレークは双方向掃引だけに適用し、従来方式側は改訂前の比較規則
        // （方向キーのみ、同率は Id 昇順）のままにする（仕様決定 CS）。
        for (int pass = 0; pass < 4; pass++)
        {
            RunPass(nodes, nodesByRank, order, rank, upNeighbors, downNeighbors,
                envFixed, virtualPreds, downstream: false, weighted: false, reverseTiebreak: false);
        }

        EvaluateCandidate();

        // 初期順序に BM の隣接挿入を適用した配置も候補に加える（暫定解釈。
        // 挿入前の Id 昇順そのものは環境供給設備の隣接規則を満たさないため、
        // 採用候補は隣接を保証した側とする。従来方式の直後に評価するため、
        // 完全同点では従来の配置が維持される）。
        nodesByRank = initialByRank.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value));
        RefreshOrder(nodesByRank, order);
        foreach (List<string> ids in nodesByRank.Values)
        {
            InsertDispensers(ids, order, envFixed, virtualPreds);
        }

        EvaluateCandidate();

        // 双方向掃引は従来方式の評価とは別に、初期順序（ランク内 Id 昇順）から
        // やり直す。
        nodesByRank = initialByRank.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value));
        RefreshOrder(nodesByRank, order);

        // 上流側から始めて一往復（上流・下流の 2 パス）ずつ掃引し、各パス後に
        // 順序を候補として評価する。直近の一往復で全ランクの順序が不変になった
        // 時点、または上限パス数に達した時点で打ち切る（上限は暫定解釈の 12）。
        const int MaxSweepPasses = 12;
        for (int pass = 0; pass < MaxSweepPasses; pass += 2)
        {
            Dictionary<int, List<string>> roundStart = SnapshotOrder(nodesByRank);
            RunPass(nodes, nodesByRank, order, rank, upNeighbors, downNeighbors,
                envFixed, virtualPreds, downstream: false, weighted: true, reverseTiebreak: true);
            EvaluateCandidate();
            RunPass(nodes, nodesByRank, order, rank, upNeighbors, downNeighbors,
                envFixed, virtualPreds, downstream: true, weighted: true, reverseTiebreak: true);
            EvaluateCandidate();
            bool unchanged = roundStart.Count == nodesByRank.Count
                && roundStart.All(kv =>
                    nodesByRank.TryGetValue(kv.Key, out List<string>? ids)
                    && ids.SequenceEqual(kv.Value));
            if (unchanged)
            {
                break;
            }
        }

        return (bestByRank, bestCrossings);
    }

    /// <summary>
    /// 隣接ペア入替の後処理（仕様決定 CS）。全ランクを昇順に 1 巡し、ランク内の
    /// 隣接ペア（位置 i, i+1）の入替を左から順に試す。いずれかのノードが環境
    /// 供給設備（envFixed）であるペアは試行せず、入替ごとに BM の隣接挿入を
    /// 再適用して order を更新し、全体の交差数が採用交差数より厳密に減るとき
    /// だけ受理する。受理・非受理に関わらず i+1 へ進み、全ランクの走査が
    /// 終わった時点で打ち切る（反復はしない）。受理判定は交差数のみで行い、
    /// CR の第 2 キー（順序番号差）は見ない（暫定解釈）。
    /// </summary>
    private static void ApplyAdjacentPairSwaps(
        Dictionary<int, List<string>> nodesByRank,
        Dictionary<string, int> order,
        IReadOnlyDictionary<string, int> rank,
        IReadOnlyList<FlowGraphEdge> drawnEdges,
        IReadOnlyDictionary<string, int> envFixed,
        IReadOnlyDictionary<string, List<string>> virtualPreds,
        int adoptedCrossings)
    {
        foreach (int r in nodesByRank.Keys.OrderBy(k => k))
        {
            List<string> ids = nodesByRank[r];
            for (int i = 0; i + 1 < ids.Count; i++)
            {
                if (envFixed.ContainsKey(ids[i]) || envFixed.ContainsKey(ids[i + 1]))
                {
                    continue;
                }

                var before = new List<string>(ids);
                (ids[i], ids[i + 1]) = (ids[i + 1], ids[i]);
                InsertDispensers(ids, order, envFixed, virtualPreds);
                int crossings = CountCrossings(drawnEdges, nodesByRank, rank, order);
                if (crossings < adoptedCrossings)
                {
                    adoptedCrossings = crossings;
                }
                else
                {
                    ids.Clear();
                    ids.AddRange(before);
                    for (int k = 0; k < ids.Count; k++)
                    {
                        order[ids[k]] = k;
                    }
                }
            }
        }
    }

    /// <summary>nodesByRank の並びをそのまま order（ランク内順序番号）へ反映する。</summary>
    private static void RefreshOrder(
        Dictionary<int, List<string>> nodesByRank,
        Dictionary<string, int> order)
    {
        foreach (List<string> ids in nodesByRank.Values)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                order[ids[i]] = i;
            }
        }
    }

    /// <summary>
    /// 環境供給設備はバリセンターの値ではなく、ソート後に最小層を取った
    /// 利用設備の直後へ挿入し直す（仕様決定 BM の隣接規則）。キー比較では
    /// 無関係なノードが間に割り込みうるため、隣接は挿入で保証する。
    /// 利用設備が同じランクにいない場合は行末へ退避する（通常は起きない
    /// 防御的経路）。同じ利用設備を共有する供給設備が複数あるときは、
    /// ソート後の相対順を保ったまま連続して挿入する（暫定解釈。逐次
    /// userIdx+1 への挿入は供給設備同士の順序を毎パス反転させて掃引を
    /// 振動させるため）。
    /// </summary>
    private static void InsertDispensers(
        List<string> ids,
        Dictionary<string, int> order,
        IReadOnlyDictionary<string, int> envFixed,
        IReadOnlyDictionary<string, List<string>> virtualPreds)
    {
        var insertCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string provider in ids.Where(envFixed.ContainsKey).ToList())
        {
            ids.Remove(provider);
            int userIdx = virtualPreds.TryGetValue(provider, out List<string>? users)
                ? users.Select(u => ids.IndexOf(u)).Where(i => i >= 0).DefaultIfEmpty(-1).Min()
                : -1;
            if (userIdx < 0)
            {
                ids.Insert(ids.Count, provider);
                continue;
            }

            string user = users!.First(u => ids.IndexOf(u) == userIdx);
            insertCounts.TryGetValue(user, out int count);
            ids.Insert(userIdx + 1 + count, provider);
            insertCounts[user] = count + 1;
        }

        for (int i = 0; i < ids.Count; i++)
        {
            order[ids[i]] = i;
        }
    }

    /// <summary>位置のキーは従来どおり rank * 1_000_000 + order の大域位置を両方向で共用する。</summary>
    private static double Position(
        string id,
        IReadOnlyDictionary<string, int> rank,
        IReadOnlyDictionary<string, int> order) =>
        rank[id] * 1_000_000.0 + order[id];

    /// <summary>
    /// パス方向の隣接位置の加重平均をバリセンターキーとして計算する。
    /// その方向に隣接ノードを持たないノードは現在位置を維持する
    /// （従来の退縮規則を両方向に適用）。weighted=false のとき全隣接を
    /// 重み 1 とする（従来方式の再現用）。
    /// </summary>
    private static Dictionary<string, double> BarycenterKey(
        IReadOnlyDictionary<string, FlowGraphNode> nodes,
        bool downstream,
        bool weighted,
        IReadOnlyDictionary<string, List<(string Id, double Weight)>> upNeighbors,
        IReadOnlyDictionary<string, List<(string Id, double Weight)>> downNeighbors,
        IReadOnlyDictionary<string, int> rank,
        IReadOnlyDictionary<string, int> order) =>
        nodes.Keys.ToDictionary(
            id => id,
            id =>
            {
                List<(string Id, double Weight)> neighbors =
                    (downstream ? downNeighbors : upNeighbors)[id];
                if (neighbors.Count == 0)
                {
                    return Position(id, rank, order);
                }

                double sum = 0;
                double weightSum = 0;
                foreach ((string n, double w) in neighbors)
                {
                    double ww = weighted ? w : 1.0;
                    sum += ww * Position(n, rank, order);
                    weightSum += ww;
                }

                return sum / weightSum;
            },
            StringComparer.Ordinal);

    /// <summary>
    /// 1 パスの掃引: 隣接ノード位置の加重平均をキーに各ランクをソートし、ソート後に
    /// BM の隣接挿入（環境供給設備を最小層利用設備の直後へ移す規則）を適用して
    /// order を更新する。バリセンターはソート前の order に対して一括で計算する。
    /// weighted=false のとき全隣接を重み 1 とする（従来方式の再現用）。
    /// reverseTiebreak=true のとき方向キーの同率を、Id 昇順の前に逆方向の
    /// バリセンターキー（逆方向に隣接を持たないノードは現位置）で再比較する
    /// （仕様決定 CS）。従来方式の評価パスは false のまま比較規則を維持する。
    /// </summary>
    private static void RunPass(
        IReadOnlyDictionary<string, FlowGraphNode> nodes,
        Dictionary<int, List<string>> nodesByRank,
        Dictionary<string, int> order,
        IReadOnlyDictionary<string, int> rank,
        IReadOnlyDictionary<string, List<(string Id, double Weight)>> upNeighbors,
        IReadOnlyDictionary<string, List<(string Id, double Weight)>> downNeighbors,
        IReadOnlyDictionary<string, int> envFixed,
        IReadOnlyDictionary<string, List<string>> virtualPreds,
        bool downstream,
        bool weighted,
        bool reverseTiebreak)
    {
        var key = BarycenterKey(nodes, downstream, weighted, upNeighbors, downNeighbors, rank, order);
        Dictionary<string, double>? reverseKey =
            reverseTiebreak
                ? BarycenterKey(nodes, !downstream, weighted, upNeighbors, downNeighbors, rank, order)
                : null;
        foreach (List<string> ids in nodesByRank.Values)
        {
            ids.Sort((a, b) =>
            {
                int cmp = key[a].CompareTo(key[b]);
                if (cmp == 0 && reverseKey is not null)
                {
                    cmp = reverseKey[a].CompareTo(reverseKey[b]);
                }

                return cmp != 0 ? cmp : StringComparer.Ordinal.Compare(a, b);
            });
            InsertDispensers(ids, order, envFixed, virtualPreds);
        }
    }

    /// <summary>
    /// 順序位置: 端点ノードがランク r にあればその order、なければ両端点の
    /// (rank, order) の線形補間（仕様決定 CR）。後退エッジ（rank が小さい側へ
    /// 戻る辺）も両端点の大小で補間が決まるため同じ式で扱う。
    /// </summary>
    private static double OrderAtRank(
        FlowGraphEdge edge,
        int r,
        IReadOnlyDictionary<string, int> rank,
        IReadOnlyDictionary<string, int> order)
    {
        int rF = rank[edge.FromId];
        int rT = rank[edge.ToId];
        if (rF == rT)
        {
            return order[edge.FromId];
        }

        double t = (r - rF) / (double)(rT - rF);
        return order[edge.FromId] + (order[edge.ToId] - order[edge.FromId]) * t;
    }

    /// <summary>
    /// 交差数: ランク境界 k|k+1 を跨ぐエッジをその境界における区間として扱い、
    /// 両端の順序位置の差の符号が厳密に逆転する（積が負）区間対を全境界で合計する。
    /// 端点を共有する区間対は共有位置で差が 0 になるため積は負にならず、
    /// 交差として数えられない。
    /// </summary>
    private static int CountCrossings(
        IReadOnlyList<FlowGraphEdge> drawnEdges,
        IReadOnlyDictionary<int, List<string>> nodesByRank,
        IReadOnlyDictionary<string, int> rank,
        IReadOnlyDictionary<string, int> order)
    {
        if (drawnEdges.Count == 0 || nodesByRank.Count == 0)
        {
            return 0;
        }

        int maxRank = nodesByRank.Keys.Max();
        int total = 0;
        for (int k = 0; k < maxRank; k++)
        {
            var spans = new List<(double AtK, double AtK1)>();
            foreach (FlowGraphEdge edge in drawnEdges)
            {
                int lo = Math.Min(rank[edge.FromId], rank[edge.ToId]);
                int hi = Math.Max(rank[edge.FromId], rank[edge.ToId]);
                if (lo <= k && hi >= k + 1)
                {
                    spans.Add((OrderAtRank(edge, k, rank, order), OrderAtRank(edge, k + 1, rank, order)));
                }
            }

            for (int i = 0; i < spans.Count; i++)
            {
                for (int j = i + 1; j < spans.Count; j++)
                {
                    double dK = spans[i].AtK - spans[j].AtK;
                    double dK1 = spans[i].AtK1 - spans[j].AtK1;
                    if (dK * dK1 < 0)
                    {
                        total++;
                    }
                }
            }
        }

        return total;
    }

    /// <summary>
    /// 第 2 キー: 加重対象の目標出力エッジについて、両端ノードの順序番号差の
    /// 絶対値の合計（仕様決定 CR）。加重で目標隣接側へ寄った候補が交差数最小に
    /// 並んだとき、順序番号差が最小の候補として採用される。ランクは候補間で
    /// 変わらないため、ランク成分を含む大域位置差ではなく order の差で計る
    /// （大域位置差はカラムの遠い端点ペアを有利にしてしまう）。
    /// </summary>
    private static double TargetOutputGap(
        IReadOnlyList<FlowGraphEdge> drawnEdges,
        IReadOnlyDictionary<string, FlowGraphNode> nodes,
        IReadOnlyDictionary<string, int> order)
    {
        double sum = 0;
        foreach (FlowGraphEdge edge in drawnEdges)
        {
            if (edge.Kind == FlowGraphEdgeKind.RecipeOutput && nodes[edge.ToId].IsTarget)
            {
                sum += Math.Abs(order[edge.FromId] - order[edge.ToId]);
            }
        }

        return sum;
    }

    /// <summary>現在のランク内順序のスナップショット（候補保存・一往復不変判定用）。</summary>
    private static Dictionary<int, List<string>> SnapshotOrder(
        Dictionary<int, List<string>> nodesByRank) =>
        nodesByRank.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value));
}
