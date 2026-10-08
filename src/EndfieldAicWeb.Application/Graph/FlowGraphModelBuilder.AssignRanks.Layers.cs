namespace EndfieldAicWeb.Application.Graph;

public static partial class FlowGraphModelBuilder
{
    /// <summary>
    /// 層割り段（仕様決定 BF・BG・BH・BM）。各ノードの層を出口側起点の最長距離で決め、
    /// 表示用ランク（最大層からの反転値）を返す。順序段が使う環境供給設備の固定層
    /// （envFixed）と仮想先行ノード（virtualPreds）も併せて返す。
    /// </summary>
    private static (Dictionary<string, int> Rank, Dictionary<string, int> EnvFixed,
        Dictionary<string, List<string>> VirtualPreds) AssignLayers(
        IReadOnlyDictionary<string, FlowGraphNode> nodes,
        IReadOnlyList<FlowGraphEdge> edges,
        IReadOnlyDictionary<string, List<string>> envConsumers)
    {
        (Dictionary<string, List<string>> preds, Dictionary<string, List<string>> succs,
            Dictionary<string, List<string>> effSuccs, HashSet<string> terminals) =
            BuildLayerGraph(nodes, edges);
        Dictionary<string, int> layer = PropagateInitialLayers(nodes, effSuccs, preds, terminals);
        HashSet<string> deferredSet = AssignCycleLayers(nodes, effSuccs, terminals, layer);
        (Dictionary<string, int> envFixed, Dictionary<string, List<string>> virtualPreds) =
            FixEnvironmentLayers(nodes, edges, succs, effSuccs, preds, terminals, deferredSet,
                envConsumers, layer);
        int maxLayer = layer.Count > 0 ? layer.Values.Max() : 0;
        var rank = nodes.Keys.ToDictionary(
            id => id, id => maxLayer - layer[id], StringComparer.Ordinal);
        return (rank, envFixed, virtualPreds);
    }

    /// <summary>
    /// 第 1 段の層割り（仕様決定 BF・BG）。下流側が確定した順に層を確定する。
    /// 戻り値の layer は後段（循環の引き直し・環境設備の再伝播）で更新される。
    /// </summary>
    private static Dictionary<string, int> PropagateInitialLayers(
        IReadOnlyDictionary<string, FlowGraphNode> nodes,
        IReadOnlyDictionary<string, List<string>> effSuccs,
        IReadOnlyDictionary<string, List<string>> preds,
        IReadOnlySet<string> terminals)
    {
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

        while (queue.Count > 0)
        {
            string id = queue.Min!;
            queue.Remove(id);
            layer[id] = 1 + MaxAssignedSucc(id, effSuccs, layer);
            foreach (string pred in preds[id])
            {
                if (--pending[pred] == 0)
                {
                    queue.Add(pred);
                }
            }
        }

        return layer;
    }

    /// <summary>確定済みの後続ノードが持つ最大の層。確定済みがなければ -1。</summary>
    private static int MaxAssignedSucc(
        string id,
        IReadOnlyDictionary<string, List<string>> effSuccs,
        IReadOnlyDictionary<string, int> layer)
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

    /// <summary>
    /// 循環に残ったノードの層を確定する（仕様決定 BH）。後退エッジは層割りに使わず、
    /// 自分自身へ戻れる起点は Layer0 に固定し、仮確定より深い経路が残るときは
    /// 自分自身を経由しない出口への最長単純経路で層を引き直す。
    /// 戻り値は循環部分のノード集合で、環境設備の再伝播が循環内エッジを除外する
    /// 判定に使う。
    /// </summary>
    private static HashSet<string> AssignCycleLayers(
        IReadOnlyDictionary<string, FlowGraphNode> nodes,
        IReadOnlyDictionary<string, List<string>> effSuccs,
        IReadOnlySet<string> terminals,
        Dictionary<string, int> layer)
    {
        // Layer0 の起点: 目標アイテム、およびどの設備にも消費されない（下流エッジのない）
        // アイテム（未消費の副産物や余剰を含む）。
        bool IsAnchor(string id) =>
            nodes[id].Kind == FlowGraphNodeKind.Item
            && (nodes[id].IsTarget || effSuccs[id].Count == 0);

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
                layer[id] = cycleAnchors.Contains(id) ? 0 : 1 + MaxAssignedSucc(id, effSuccs, layer);
            }

            if (next.Count == deferred.Count)
            {
                // 起点に届かない閉じた循環（通常は発生しない）。確定済み後続だけで打ち切る。
                foreach (string id in next)
                {
                    layer[id] = 1 + MaxAssignedSucc(id, effSuccs, layer);
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

        return deferredSet;
    }

    /// <summary>
    /// 環境供給設備（散布機など）の層固定と第 2 段の再伝播（仕様決定 BM）。
    /// 終端ノードのうち環境供給設備は利用設備の最小層へ固定し、その設備を消費者として
    /// 消費アイテムを層の深い側へ再伝播させる。残りの終端ノードは最も深い消費
    /// アイテムの直下流に置く。戻り値の envFixed・virtualPreds は順序段の隣接挿入
    /// （BM の行内順規則）でも使う。
    /// </summary>
    private static (Dictionary<string, int> EnvFixed, Dictionary<string, List<string>> VirtualPreds)
        FixEnvironmentLayers(
        IReadOnlyDictionary<string, FlowGraphNode> nodes,
        IReadOnlyList<FlowGraphEdge> edges,
        IReadOnlyDictionary<string, List<string>> succs,
        IReadOnlyDictionary<string, List<string>> effSuccs,
        IReadOnlyDictionary<string, List<string>> preds,
        IReadOnlySet<string> terminals,
        IReadOnlySet<string> deferredSet,
        IReadOnlyDictionary<string, List<string>> envConsumers,
        Dictionary<string, int> layer)
    {
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

        return (envFixed, virtualPreds);
    }
}
