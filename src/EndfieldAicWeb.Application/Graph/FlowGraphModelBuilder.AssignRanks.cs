namespace EndfieldAicWeb.Application.Graph;

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
        (Dictionary<string, int> rank, Dictionary<string, int> envFixed,
            Dictionary<string, List<string>> virtualPreds) = AssignLayers(nodes, edges, envConsumers);
        Dictionary<string, int> order = OrderNodesWithinRanks(nodes, edges, rank, envFixed, virtualPreds);
        return (rank, order);
    }

    /// <summary>
    /// 層割りの前提となる隣接表を構築する。preds・succs は描画エッジの先行・後続表。
    /// 出力を持たない設備などの終端ノードへのエッジは層割りに使わないため、terminals と
    /// 終端を除いた有効後続 effSuccs を返す。
    /// </summary>
    private static (Dictionary<string, List<string>> Preds, Dictionary<string, List<string>> Succs,
        Dictionary<string, List<string>> EffSuccs, HashSet<string> Terminals) BuildLayerGraph(
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
        return (preds, succs, effSuccs, terminals);
    }
}
