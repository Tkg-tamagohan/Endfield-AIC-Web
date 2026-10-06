namespace EndfieldAicWeb.Application;

/// <summary>FlowGraphModelBuilder の順序ロジック部分（層割りとランク内順序）。</summary>
public static partial class FlowGraphModelBuilder
{
    /// <summary>
    /// 出口側起点の最長距離で層割りする。ランク内順序は上流・下流の双方向バリセンター掃引で
    /// 順序候補を生成し、交差数を計測して最良の候補を採用する（従来方式の結果も候補に含め
    /// 退化を防ぐ）。掃引ソートの同率は逆方向キーで再比較し、採用後は全ランクを 1 巡する
    /// 隣接ペア入替後処理で交差をさらに減らす（仕様決定 CR・CS）。
    /// 層は Layer0（目標・未消費アイテム）から上流へ遡る距離で、同じノードが複数の層に
    /// 出る場合は大きい層にまとめる（仕様決定 BF）。消費される目標もこの規則に従う（BG）。
    /// 循環の残存ノードは後退エッジを無視し、確定済みの後続だけで層を決める（BH）。
    /// 仮確定より深い経路が残るときは、自分自身を経由しない出口への最長単純経路で
    /// 層を引き直す。出力を持たない設備ノード（終端ノード）は第 1 段の層割り対象外とし、
    /// 環境供給設備は利用設備の最小層へ固定してから第 2 段で再伝播し、残りは消費アイテムの
    /// 直下流に置く（仕様決定 BM）。表示は Layer0 を右端列とするため、返すランクは
    /// 最大層からの反転値。
    /// envConsumers は環境供給設備の表示ノード → その環境を利用する設備の表示ノード群。
    /// </summary>
    private static (Dictionary<string, int> Rank, Dictionary<string, int> Order) AssignRanks(
        IReadOnlyDictionary<string, FlowGraphNode> nodes,
        IReadOnlyList<FlowGraphEdge> edges,
        IReadOnlyDictionary<string, List<string>> envConsumers)
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
        // アイテム（未消費の副産物や余剰を含む）。
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

        int MaxAssignedSucc(string id)
        {
            int best = -1;
            foreach (string s in effSuccs[id])
            {
                if (layer.TryGetValue(s, out int l) && l > best)
                {
                    best = l;
                }
            }

            return best;
        }

        while (queue.Count > 0)
        {
            string id = queue.Min!;
            queue.Remove(id);
            layer[id] = 1 + MaxAssignedSucc(id);
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

        // Layer0 に固定するのは循環の一部である起点（自分自身に戻れる目標）だけ。
        // 循環の外から循環へ供給するだけの目標は、消費される目標として自然な深さに置く（BG）。
        bool ReachesSelf(string start)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>();
            foreach (string succ in effSuccs[start])
            {
                stack.Push(succ);
            }

            while (stack.Count > 0)
            {
                string cur = stack.Pop();
                if (cur == start)
                {
                    return true;
                }

                if (!deferredSet.Contains(cur) || !visited.Add(cur))
                {
                    continue;
                }

                foreach (string succ in effSuccs[cur])
                {
                    stack.Push(succ);
                }
            }

            return false;
        }

        var cycleAnchors = new HashSet<string>(
            deferred.Where(id => IsAnchor(id) && ReachesSelf(id)), StringComparer.Ordinal);
        while (deferred.Count > 0)
        {
            var next = new List<string>();
            foreach (string id in deferred)
            {
                // 循環内の起点でなく後続も未確定のノードは、循環の向こう側へ展開が戻るまで待つ。
                if (!cycleAnchors.Contains(id) && !effSuccs[id].Any(layer.ContainsKey))
                {
                    next.Add(id);
                    continue;
                }

                // 循環内の起点（目標など）は常に Layer0。前方への分岐を持つ起点を
                // 後続の深さで埋めると Layer0 からずれる。
                layer[id] = cycleAnchors.Contains(id) ? 0 : 1 + MaxAssignedSucc(id);
            }

            if (next.Count == deferred.Count)
            {
                // 起点に届かない閉じた循環（通常は発生しない）。確定済み後続だけで打ち切る。
                foreach (string id in next)
                {
                    layer[id] = 1 + MaxAssignedSucc(id);
                }
                break;
            }

            deferred = next;
        }

        // 引き直し: 循環内ノードは、自分自身を経由しない出口への最長単純経路で層を決め直す。
        // 確定順や重なった循環に左右されず、除外すべき後退エッジ以外のエッジが
        // 後退しない。Layer0 の起点は固定のまま動かさない。探索は循環部分内だけに
        // 限定し、呼び出し回数に上限を設ける。
        int ProbeLayer(string id, HashSet<string> banned, ref int budget)
        {
            int best = -1;
            foreach (string succ in effSuccs[id])
            {
                int tail;
                if (!deferredSet.Contains(succ) || cycleAnchors.Contains(succ))
                {
                    // 確定済みノードと循環内の起点は経路の端点。循環外の目標は仮の層が
                    // 動きうるため端点にせず、経路の途中ノードとして再帰する。
                    tail = layer[succ];
                }
                else
                {
                    if (banned.Contains(succ) || budget <= 0)
                    {
                        continue;
                    }

                    budget--;
                    tail = ProbeLayer(
                        succ, new HashSet<string>(banned, StringComparer.Ordinal) { succ }, ref budget);
                    if (tail < 0)
                    {
                        continue;
                    }
                }

                if (tail > best)
                {
                    best = tail;
                }
            }

            return best < 0 ? -1 : best + 1;
        }

        foreach (string id in deferredSet)
        {
            if (cycleAnchors.Contains(id))
            {
                continue;
            }

            int budget = 4096;
            int probed = ProbeLayer(id, new HashSet<string>(StringComparer.Ordinal) { id }, ref budget);
            if (probed > layer[id])
            {
                layer[id] = probed;
            }
        }

        // 環境供給設備（散布機など）の終端ノードは、その環境を利用する設備の最小層へ固定する
        // （仕様決定 BM）。第 1 段の層で決め、後段の再伝播で利用設備が深く動いても追従しない。
        // 利用設備が 0 件の環境は従来規則へ退避する（計算機では散布機台数が稼働中ランの
        // 環境にのみ計上されるため到達しない防御的経路）。
        // virtualPreds は行内順を利用設備の隣へ寄せるためのバリセンター仮想先行ノードで、
        // 最小層を取った利用設備だけに絞る。
        var envFixed = new Dictionary<string, int>(StringComparer.Ordinal);
        var virtualPreds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string id in terminals)
        {
            if (!envConsumers.TryGetValue(id, out List<string>? users))
            {
                continue;
            }

            int minLayer = users
                .Where(layer.ContainsKey)
                .Select(u => layer[u])
                .DefaultIfEmpty(-1)
                .Min();
            if (minLayer < 0)
            {
                continue;
            }

            envFixed[id] = minLayer;
            layer[id] = minLayer;
            virtualPreds[id] = users.Where(u => layer.GetValueOrDefault(u, -1) == minLayer).ToList();
        }

        // 環境利用設備がその環境の消費アイテムを産出する場合、その出力エッジは環境
        // フィードバックの一部として第 2 段の伝播対象から外す。伝播させると消費
        // アイテムの沈下に利用設備が追従して、固定した供給設備と層が離れる。外した
        // エッジは層割りに使われないだけでモデルには残り、後退エッジとして描かれる。
        var envLoopEdges = new HashSet<(string FromId, string ToId)>();
        foreach (string provider in envFixed.Keys)
        {
            foreach (FlowGraphEdge consumeEdge in edges)
            {
                if (consumeEdge.Kind != FlowGraphEdgeKind.EnvironmentConsume
                    || consumeEdge.ToId != provider)
                {
                    continue;
                }

                foreach (string user in envConsumers[provider])
                {
                    if (edges.Any(e => e.Kind == FlowGraphEdgeKind.RecipeOutput
                        && e.FromId == user && e.ToId == consumeEdge.FromId))
                    {
                        envLoopEdges.Add((user, consumeEdge.FromId));
                    }
                }
            }
        }

        // 第 2 段: 固定した環境供給設備を消費者として層を再伝播する。消費アイテムは
        // 環境設備の層+1 以降へ沈み、消費アイテム→環境設備のエッジは順方向を保つ。
        // 第 1 段と同じく循環部分内のエッジは層割りに使わない。環境設備自体は層を
        // 変えないシンクとして扱う。
        if (envFixed.Count > 0)
        {
            var pass2Succs = nodes.Keys.ToDictionary(
                id => id,
                id => effSuccs[id]
                    .Where(s => (!deferredSet.Contains(id) || !deferredSet.Contains(s))
                        && !envLoopEdges.Contains((id, s)))
                    .Concat(succs[id].Where(envFixed.ContainsKey))
                    .ToList(),
                StringComparer.Ordinal);
            bool moved = true;
            while (moved)
            {
                moved = false;
                foreach (string id in nodes.Keys)
                {
                    if (envFixed.ContainsKey(id) || terminals.Contains(id))
                    {
                        continue;
                    }

                    int best = layer[id];
                    foreach (string s in pass2Succs[id])
                    {
                        if (layer.TryGetValue(s, out int sl) && sl + 1 > best)
                        {
                            best = sl + 1;
                        }
                    }

                    if (best != layer[id])
                    {
                        layer[id] = best;
                        moved = true;
                    }
                }
            }
        }

        // 残りの終端ノードは最も深い消費アイテムの直下流に置く。層は再伝播後の値で取る。
        foreach (string id in terminals)
        {
            if (envFixed.ContainsKey(id))
            {
                continue;
            }

            layer[id] = preds[id]
                .Where(layer.ContainsKey)
                .Select(p => layer[p])
                .DefaultIfEmpty(1)
                .Max() - 1;
        }

        int maxLayer = layer.Count > 0 ? layer.Values.Max() : 0;
        var rank = nodes.Keys.ToDictionary(
            id => id, id => maxLayer - layer[id], StringComparer.Ordinal);

        // ランク内順序（仕様決定 CR）: 初回は Id 昇順。上流側（先行ノード位置）と
        // 下流側（後続ノード位置）のバリセンター掃引を交互に行い、掃引で確定した
        // 各順序候補の交差数を計測して最小の順序を採用する。バリセンターは加重平均で、
        // 目標アイテムへの出力エッジに支配的重みを付ける。同率は Id 昇順（従来どおり）。
        var nodesByRank = nodes.Keys
            .GroupBy(id => rank[id])
            .ToDictionary(g => g.Key, g => g.OrderBy(id => id, StringComparer.Ordinal).ToList());
        var order = new Dictionary<string, int>(StringComparer.Ordinal);

        void RefreshOrder()
        {
            foreach (List<string> ids in nodesByRank.Values)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    order[ids[i]] = i;
                }
            }
        }

        RefreshOrder();

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

        // 位置のキーは従来どおり rank * 1_000_000 + order の大域位置を両方向で共用する。
        double Position(string id) => rank[id] * 1_000_000.0 + order[id];

        // 環境供給設備はバリセンターの値ではなく、ソート後に最小層を取った
        // 利用設備の直後へ挿入し直す（仕様決定 BM の隣接規則）。キー比較では
        // 無関係なノードが間に割り込みうるため、隣接は挿入で保証する。
        // 利用設備が同じランクにいない場合は行末へ退避する（通常は起きない
        // 防御的経路）。同じ利用設備を共有する供給設備が複数あるときは、
        // ソート後の相対順を保ったまま連続して挿入する（暫定解釈。逐次
        // userIdx+1 への挿入は供給設備同士の順序を毎パス反転させて掃引を
        // 振動させるため）。
        void InsertDispensers(List<string> ids)
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

        // パス方向の隣接位置の加重平均をバリセンターキーとして計算する。
        // その方向に隣接ノードを持たないノードは現在位置を維持する
        // （従来の退縮規則を両方向に適用）。weighted=false のとき全隣接を
        // 重み 1 とする（従来方式の再現用）。
        Dictionary<string, double> BarycenterKey(bool downstream, bool weighted) =>
            nodes.Keys.ToDictionary(
                id => id,
                id =>
                {
                    List<(string Id, double Weight)> neighbors =
                        (downstream ? downNeighbors : upNeighbors)[id];
                    if (neighbors.Count == 0)
                    {
                        return Position(id);
                    }

                    double sum = 0;
                    double weightSum = 0;
                    foreach ((string n, double w) in neighbors)
                    {
                        double ww = weighted ? w : 1.0;
                        sum += ww * Position(n);
                        weightSum += ww;
                    }

                    return sum / weightSum;
                },
                StringComparer.Ordinal);

        // 1 パスの掃引: 隣接ノード位置の加重平均をキーに各ランクをソートし、ソート後に
        // BM の隣接挿入（環境供給設備を最小層利用設備の直後へ移す規則）を適用して
        // order を更新する。バリセンターはソート前の order に対して一括で計算する。
        // weighted=false のとき全隣接を重み 1 とする（従来方式の再現用）。
        // reverseTiebreak=true のとき方向キーの同率を、Id 昇順の前に逆方向の
        // バリセンターキー（逆方向に隣接を持たないノードは現位置）で再比較する
        // （仕様決定 CS）。従来方式の評価パスは false のまま比較規則を維持する。
        void RunPass(bool downstream, bool weighted, bool reverseTiebreak)
        {
            var key = BarycenterKey(downstream, weighted);
            Dictionary<string, double>? reverseKey =
                reverseTiebreak ? BarycenterKey(!downstream, weighted) : null;
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
                InsertDispensers(ids);
            }
        }

        // 順序位置: 端点ノードがランク r にあればその order、なければ両端点の
        // (rank, order) の線形補間（仕様決定 CR）。後退エッジ（rank が小さい側へ
        // 戻る辺）も両端点の大小で補間が決まるため同じ式で扱う。
        double OrderAtRank(FlowGraphEdge edge, int r)
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

        // 交差数: ランク境界 k|k+1 を跨ぐエッジをその境界における区間として扱い、
        // 両端の順序位置の差の符号が厳密に逆転する（積が負）区間対を全境界で合計する。
        // 端点を共有する区間対は共有位置で差が 0 になるため積は負にならず、
        // 交差として数えられない。
        int CountCrossings()
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
                        spans.Add((OrderAtRank(edge, k), OrderAtRank(edge, k + 1)));
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

        // 第 2 キー: 加重対象の目標出力エッジについて、両端ノードの順序番号差の
        // 絶対値の合計（仕様決定 CR）。加重で目標隣接側へ寄った候補が交差数最小に
        // 並んだとき、順序番号差が最小の候補として採用される。ランクは候補間で
        // 変わらないため、ランク成分を含む大域位置差ではなく order の差で計る
        // （大域位置差はカラムの遠い端点ペアを有利にしてしまう）。
        double TargetOutputGap()
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

        var bestByRank = new Dictionary<int, List<string>>();
        int bestCrossings = -1;
        double bestGap = -1;

        Dictionary<int, List<string>> SnapshotOrder() => nodesByRank.ToDictionary(
            kv => kv.Key, kv => new List<string>(kv.Value));

        // 現在の順序を候補として評価する。採用は交差数最小・同数は第 2 キー最小・
        // 同量は先に評価した候補を維持する。
        void EvaluateCandidate()
        {
            int crossings = CountCrossings();
            double gap = TargetOutputGap();
            if (bestCrossings < 0 || crossings < bestCrossings
                || (crossings == bestCrossings && gap < bestGap))
            {
                bestCrossings = crossings;
                bestGap = gap;
                bestByRank = SnapshotOrder();
            }
        }

        // 初期順序はランク内の Id 昇順（仕様決定 CR）。BM の隣接挿入は各パスの
        // ソート後に適用するため、初期順序そのものには含めない。
        Dictionary<int, List<string>> initialByRank = SnapshotOrder();

        // 従来方式（上流側のみ・重みなしのバリセンター掃引と BM 隣接挿入を固定
        // 4 パスで行った順序）を最初の候補として評価する。交差数と順序番号差が
        // 完全に並んだときは従来の配置が維持される（仕様決定 CR）。逆方向
        // タイブレークは双方向掃引だけに適用し、従来方式側は改訂前の比較規則
        // （方向キーのみ、同率は Id 昇順）のままにする（仕様決定 CS）。
        for (int pass = 0; pass < 4; pass++)
        {
            RunPass(downstream: false, weighted: false, reverseTiebreak: false);
        }

        EvaluateCandidate();

        // 初期順序に BM の隣接挿入を適用した配置も候補に加える（暫定解釈。
        // 挿入前の Id 昇順そのものは環境供給設備の隣接規則を満たさないため、
        // 採用候補は隣接を保証した側とする。従来方式の直後に評価するため、
        // 完全同点では従来の配置が維持される）。
        nodesByRank = initialByRank.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value));
        RefreshOrder();
        foreach (List<string> ids in nodesByRank.Values)
        {
            InsertDispensers(ids);
        }

        EvaluateCandidate();

        // 双方向掃引は従来方式の評価とは別に、初期順序（ランク内 Id 昇順）から
        // やり直す。
        nodesByRank = initialByRank.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value));
        RefreshOrder();

        // 上流側から始めて一往復（上流・下流の 2 パス）ずつ掃引し、各パス後に
        // 順序を候補として評価する。直近の一往復で全ランクの順序が不変になった
        // 時点、または上限パス数に達した時点で打ち切る（上限は暫定解釈の 12）。
        const int MaxSweepPasses = 12;
        for (int pass = 0; pass < MaxSweepPasses; pass += 2)
        {
            Dictionary<int, List<string>> roundStart = SnapshotOrder();
            RunPass(downstream: false, weighted: true, reverseTiebreak: true);
            EvaluateCandidate();
            RunPass(downstream: true, weighted: true, reverseTiebreak: true);
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

        // 最良候補を採用して order を確定する。
        nodesByRank = bestByRank;
        RefreshOrder();

        // 隣接ペア入替の後処理（仕様決定 CS）: 全ランクを昇順に 1 巡し、ランク内の
        // 隣接ペア（位置 i, i+1）の入替を左から順に試す。いずれかのノードが環境
        // 供給設備（envFixed）であるペアは試行せず、入替ごとに BM の隣接挿入を
        // 再適用して order を更新し、全体の交差数が採用交差数より厳密に減るとき
        // だけ受理する。受理・非受理に関わらず i+1 へ進み、全ランクの走査が
        // 終わった時点で打ち切る（反復はしない）。受理判定は交差数のみで行い、
        // CR の第 2 キー（順序番号差）は見ない（暫定解釈）。
        int adoptedCrossings = bestCrossings;
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
                InsertDispensers(ids);
                int crossings = CountCrossings();
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

        return (rank, order);
    }
}
