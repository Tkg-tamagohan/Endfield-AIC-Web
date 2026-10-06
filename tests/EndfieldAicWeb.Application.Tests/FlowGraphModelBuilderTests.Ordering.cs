using EndfieldAicWeb.Application;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

public partial class FlowGraphModelBuilderTests
{
    // FG-56〜62: ランク内順序の交差最小化（test-specification-phase39.md、仕様決定 CR）。
    //
    // 交差数の検査は、モデルの公開情報（Rank・Order とエッジ一覧）から仕様どおりの
    // 計測を行うテスト側ヘルパーで行う（暫定解釈。計測は本体の内部実装で公開面は
    // 増やさないため）。

    // ランク境界ごとの交差数: 境界 k|k+1 を跨ぐエッジをその境界における区間とみなし、
    // 端点がランク k にあればその order、なければ両端点の (rank, order) の線形補間を
    // 順序位置として、両端の差の符号が厳密に逆転する（積が負）区間対を数える。
    private static int[] CrossingsPerBoundary(
        FlowGraphModel model, IReadOnlyDictionary<string, int> order)
    {
        var rank = model.Nodes.ToDictionary(n => n.Id, n => n.Rank, StringComparer.Ordinal);
        int maxRank = model.Nodes.Count > 0 ? model.Nodes.Max(n => n.Rank) : 0;
        var counts = new int[maxRank];

        double PosAt(FlowGraphEdge e, int r)
        {
            int rF = rank[e.FromId];
            int rT = rank[e.ToId];
            if (rF == rT)
            {
                return order[e.FromId];
            }

            double t = (r - rF) / (double)(rT - rF);
            return order[e.FromId] + (order[e.ToId] - order[e.FromId]) * t;
        }

        for (int k = 0; k < maxRank; k++)
        {
            var spans = new List<(double AtK, double AtK1)>();
            foreach (FlowGraphEdge e in model.Edges)
            {
                int lo = Math.Min(rank[e.FromId], rank[e.ToId]);
                int hi = Math.Max(rank[e.FromId], rank[e.ToId]);
                if (lo <= k && hi >= k + 1)
                {
                    spans.Add((PosAt(e, k), PosAt(e, k + 1)));
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
                        counts[k]++;
                    }
                }
            }
        }

        return counts;
    }

    private static int CountCrossings(
        FlowGraphModel model, IReadOnlyDictionary<string, int> order) =>
        CrossingsPerBoundary(model, order).Sum();

    private static Dictionary<string, int> AdoptedOrder(FlowGraphModel model) =>
        model.Nodes.ToDictionary(n => n.Id, n => n.Order, StringComparer.Ordinal);

    // 初期順序の再現: ランク内の Id 昇順（仕様決定 CR の初期順序）。
    private static Dictionary<string, int> IdAscendingOrder(FlowGraphModel model)
    {
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var g in model.Nodes.GroupBy(n => n.Rank))
        {
            int i = 0;
            foreach (FlowGraphNode n in g.OrderBy(n => n.Id, StringComparer.Ordinal))
            {
                order[n.Id] = i++;
            }
        }

        return order;
    }

    // 従来方式の再現: 上流側のみのバリセンター掃引（重みなし・先行ノード位置の平均）
    // を固定 4 パス行う。環境設備を含まないフィクスチャに限る（BM の仮想先行は
    // 計画内部情報のためモデルから再構成できない。暫定解釈として環境なしに限定）。
    private static Dictionary<string, int> LegacyUpstreamOrder(FlowGraphModel model)
    {
        var rank = model.Nodes.ToDictionary(n => n.Id, n => n.Rank, StringComparer.Ordinal);
        var nodesByRank = model.Nodes.GroupBy(n => n.Rank).ToDictionary(
            g => g.Key,
            g => g.OrderBy(n => n.Id, StringComparer.Ordinal).Select(n => n.Id).ToList());
        var order = IdAscendingOrder(model);
        var preds = model.Nodes.ToDictionary(
            n => n.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (FlowGraphEdge e in model.Edges)
        {
            preds[e.ToId].Add(e.FromId);
        }

        for (int pass = 0; pass < 4; pass++)
        {
            var key = model.Nodes.ToDictionary(
                n => n.Id,
                n => preds[n.Id].Count > 0
                    ? preds[n.Id].Average(p => rank[p] * 1_000_000.0 + order[p])
                    : rank[n.Id] * 1_000_000.0 + order[n.Id],
                StringComparer.Ordinal);
            foreach (List<string> ids in nodesByRank.Values)
            {
                ids.Sort((a, b) =>
                {
                    int cmp = key[a].CompareTo(key[b]);
                    return cmp != 0 ? cmp : StringComparer.Ordinal.Compare(a, b);
                });
                for (int i = 0; i < ids.Count; i++)
                {
                    order[ids[i]] = i;
                }
            }
        }

        return order;
    }

    // 第 2 キーの再現: 目標アイテムへの出力エッジについて両端ノードの
    // 順序番号差の絶対値の合計。
    private static double TargetOutputGap(
        FlowGraphModel model, IReadOnlyDictionary<string, int> order)
    {
        var isTarget = model.Nodes.ToDictionary(
            n => n.Id, n => n.IsTarget, StringComparer.Ordinal);
        double sum = 0;
        foreach (FlowGraphEdge e in model.Edges)
        {
            if (e.Kind == FlowGraphEdgeKind.RecipeOutput && isTarget[e.ToId])
            {
                sum += Math.Abs(order[e.FromId] - order[e.ToId]);
            }
        }

        return sum;
    }

    // FG-56: 採取のみのアイテム（先行ノードなし）は、下流側のバリセンターで
    // 消費設備の直上流の位置へ寄る（従来は Id 昇順から動かず交差を生んだ）。
    [Fact]
    public void GatheredItemsMoveTowardConsumers()
    {
        // 採取アイテム 2 個が消費設備 2 台へ逆向きに供給する: i-ga は散布機 f-db の
        // 環境消費、i-ob は利用機 f-au のレシピ入力。Id 昇順では i-ga→f-db と
        // i-ob→f-au が交差する。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-ga", "ガスA", TransportKind.Pipe, gatherable: true),
             ApplicationFixtures.Item("i-ob", "素材B", gatherable: true),
             ApplicationFixtures.Item("i-pt", "製品T")],
            [ApplicationFixtures.Facility("f-au", "利用機", 30),
             ApplicationFixtures.Facility("f-db", "散布機", 5)],
            [ApplicationFixtures.Env("env-b", "環境B", "f-db", "i-ga", 360)],
            [],
            [
                ApplicationFixtures.Recipe("r-pt", "製品T", [("i-ob", 1)], [("i-pt", 1)],
                    [ApplicationFixtures.Pair("f-au", 4), ApplicationFixtures.Pair("f-au", 3, "env-b")]),
            ]);

        (_, FlowGraphModel model) = Build(snapshot, targets: new ProductionTarget("i-pt", 30));

        // 各採取アイテムが消費設備と同じカラム（直上流の位置）に来る。
        Assert.Equal(Node(model, "fac:f-db").Order, Node(model, "item:i-ga").Order);
        Assert.Equal(Node(model, "fac:f-au").Order, Node(model, "item:i-ob").Order);
        Assert.Equal(0, CountCrossings(model, AdoptedOrder(model)));
    }

    // FG-57: 報告画面相当の構造（採取アイテム 2 個が消費設備 2 台へ逆向きに供給して
    // Id 昇順では交差する）で、採用順序の交差数が 0 になる。
    [Fact]
    public void CrossedSuppliesAreResolvedToZeroCrossings()
    {
        // 利用機 f-sh が環境ペア 2 件で動き、ガス i-ga/i-gb が散布機 f-db/f-da と
        // 逆向きに供給される（不活性ガス・息壌ガスの報告と同型）。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-ga", "ガスA", TransportKind.Pipe, gatherable: true),
             ApplicationFixtures.Item("i-gb", "ガスB", TransportKind.Pipe, gatherable: true),
             ApplicationFixtures.Item("i-oa", "素材A", gatherable: true),
             ApplicationFixtures.Item("i-ob", "素材B", gatherable: true),
             ApplicationFixtures.Item("i-pa", "製品A"), ApplicationFixtures.Item("i-pb", "製品B")],
            [ApplicationFixtures.Facility("f-sh", "共用機", 30),
             ApplicationFixtures.Facility("f-da", "散布機A", 5),
             ApplicationFixtures.Facility("f-db", "散布機B", 5)],
            [ApplicationFixtures.Env("env-a", "環境A", "f-da", "i-gb", 360),
             ApplicationFixtures.Env("env-b", "環境B", "f-db", "i-ga", 360)],
            [],
            [
                ApplicationFixtures.Recipe("r-pa", "製品A", [("i-oa", 1)], [("i-pa", 1)],
                    [ApplicationFixtures.Pair("f-sh", 4), ApplicationFixtures.Pair("f-sh", 3, "env-a")]),
                ApplicationFixtures.Recipe("r-pb", "製品B", [("i-ob", 1)], [("i-pb", 1)],
                    [ApplicationFixtures.Pair("f-sh", 4), ApplicationFixtures.Pair("f-sh", 3, "env-b")]),
            ]);

        (_, FlowGraphModel model) = Build(
            snapshot, targets: [new ProductionTarget("i-pa", 30), new ProductionTarget("i-pb", 30)]);

        // 構成確認: Id 昇順の初期順序では交差する。
        Assert.True(CountCrossings(model, IdAscendingOrder(model)) > 0);
        Assert.Equal(0, CountCrossings(model, AdoptedOrder(model)));
    }

    // FG-58: 複数ランクを跨ぐエッジと、中間ランクで順序が入れ替わる他エッジとの
    // 交差が、線形補間した順序位置の逆転として計測され、最良候補で解消される。
    [Fact]
    public void SpanningEdgeCrossingIsCountedAndResolved()
    {
        // i-sh→f-z が rank 2→5 を跨ぎ、その中間のランク境界で f-dp→i-m などと
        // 交差する構造。跨ぐエッジは境界のいずれ側にも端点を持たないため、
        // この交差は線形補間がないと計測できない。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "素材U", gatherable: true),
             ApplicationFixtures.Item("i-a4", "素材A4", gatherable: true),
             ApplicationFixtures.Item("i-z4", "素材Z4", gatherable: true),
             ApplicationFixtures.Item("i-sh", "共有中間品"),
             ApplicationFixtures.Item("i-m", "中間品M"),
             ApplicationFixtures.Item("i-tt", "製品T"), ApplicationFixtures.Item("i-zz", "製品Z")],
            [ApplicationFixtures.Facility("f-p", "機P", 10),
             ApplicationFixtures.Facility("f-dp", "機DP", 10),
             ApplicationFixtures.Facility("f-t", "機T", 10),
             ApplicationFixtures.Facility("f-z", "機Z", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-p", "共有中間品", [("i-u", 1)], [("i-sh", 1)],
                    [ApplicationFixtures.Pair("f-p", 6)]),
                ApplicationFixtures.Recipe("r-dp", "中間品M", [("i-sh", 1)], [("i-m", 1)],
                    [ApplicationFixtures.Pair("f-dp", 4)]),
                ApplicationFixtures.Recipe("r-t", "製品T", [("i-a4", 1), ("i-m", 1), ("i-z4", 1)],
                    [("i-tt", 1)], [ApplicationFixtures.Pair("f-t", 4)]),
                ApplicationFixtures.Recipe("r-z", "製品Z", [("i-sh", 1)], [("i-zz", 1)],
                    [ApplicationFixtures.Pair("f-z", 4)]),
            ]);

        (_, FlowGraphModel model) = Build(
            snapshot, targets: [new ProductionTarget("i-tt", 30), new ProductionTarget("i-zz", 30)]);

        // 初期の Id 昇順順序では、境界 3|4（i-sh→f-z が両端とも端点を持たない
        // 中間境界）で交差が検出される。
        int[] initialCounts = CrossingsPerBoundary(model, IdAscendingOrder(model));
        Assert.Equal(1, initialCounts[3]);
        Assert.True(initialCounts.Sum() > 1);

        Assert.Equal(0, CountCrossings(model, AdoptedOrder(model)));
    }

    // FG-59: 交差数が最小の候補が複数あるとき、目標出力エッジ両端の順序番号差が
    // 最小の候補が採用され、設備は目標アイテムの同一または隣接カラムに配置される。
    [Fact]
    public void TargetOutputWeightPrefersFacilityAdjacentToTarget()
    {
        // 未充足の目標（i-need・i-need2）が最終ランクの順序に余地を作る。
        // Id 昇順の初期順序は f-t と目標 i-t が 2 カラム離れた交差なし候補であり、
        // 第 2 キーがなければ交差数で並んで負けうる。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-a", "素材A", gatherable: true),
             ApplicationFixtures.Item("i-b", "素材B", gatherable: true),
             ApplicationFixtures.Item("i-t", "製品T"), ApplicationFixtures.Item("i-t2", "副産T2"),
             ApplicationFixtures.Item("i-y", "製品Y"),
             ApplicationFixtures.Item("i-need", "未充足"), ApplicationFixtures.Item("i-need2", "未充足2")],
            [ApplicationFixtures.Facility("f-t", "機T", 10),
             ApplicationFixtures.Facility("f-y", "機Y", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-t", "製品T", [("i-a", 1)], [("i-t", 1), ("i-t2", 1)],
                    [ApplicationFixtures.Pair("f-t", 4)]),
                ApplicationFixtures.Recipe("r-y", "製品Y", [("i-b", 1)], [("i-y", 1)],
                    [ApplicationFixtures.Pair("f-y", 4)]),
            ]);

        (_, FlowGraphModel model) = Build(
            snapshot,
            targets:
            [
                new ProductionTarget("i-t", 30), new ProductionTarget("i-y", 30),
                new ProductionTarget("i-need", 30), new ProductionTarget("i-need2", 30),
            ]);

        Dictionary<string, int> initial = IdAscendingOrder(model);
        Dictionary<string, int> adopted = AdoptedOrder(model);

        // 初期順序は交差なしだが f-t と i-t が離れた非隣接候補（第 2 キーの効果を
        // 示す構成になっている）。
        Assert.Equal(0, CountCrossings(model, initial));
        Assert.True(Math.Abs(initial["fac:f-t"] - initial["item:i-t"]) >= 2);

        // 採用順序も交差最小（0）で、設備は目標の同一または隣接カラム。
        Assert.Equal(0, CountCrossings(model, adopted));
        Assert.True(Math.Abs(adopted["fac:f-t"] - adopted["item:i-t"]) <= 1);
        Assert.True(Math.Abs(adopted["fac:f-y"] - adopted["item:i-y"]) <= 1);

        // 交差数が並んだ候補の中で順序番号差の小さい方が採用されている。
        Assert.True(TargetOutputGap(model, adopted) < TargetOutputGap(model, initial));
    }

    // FG-60: 環境供給設備（散布機）を持つ計画で、下流掃引を含む掃引後も
    // 「散布機が最小層の利用設備の直後に置かれる」規則が全表示で維持される。
    [Fact]
    public void DispenserAdjacencyKeptInAllViews()
    {
        const int Machines = 3;

        (_, FlowGraphModel collapsed) = Build(
            ApplicationFixtures.A01(), targets: new ProductionTarget("i-part", 60));
        Assert.Equal(
            Node(collapsed, "fac:f-asm").Order + 1,
            Node(collapsed, "fac:f-disp").Order);

        (_, FlowGraphModel unadjusted) = Build(
            ApplicationFixtures.A01(), unadjusted: true,
            targets: new ProductionTarget("i-part", 60));
        Assert.Equal(
            Node(unadjusted, "fac:f-asm").Order + 1,
            Node(unadjusted, "fac:f-disp").Order);

        (_, FlowGraphModel expanded) = Build(
            ApplicationFixtures.A01(), expandFacilities: true,
            targets: new ProductionTarget("i-part", 60));
        int leftmostUnit = Enumerable.Range(0, Machines)
            .Select(i => Node(expanded, $"facunit:f-asm#{i}").Order)
            .Min();
        Assert.Equal(leftmostUnit + 1, Node(expanded, "fac:f-disp").Order);
    }

    // FG-61: 同一入力の 2 回の構築で Rank・Order が完全一致する（確定的動作）。
    [Fact]
    public void OrderingIsDeterministic()
    {
        MasterDataSnapshot Snapshot() => ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "素材U", gatherable: true),
             ApplicationFixtures.Item("i-a4", "素材A4", gatherable: true),
             ApplicationFixtures.Item("i-z4", "素材Z4", gatherable: true),
             ApplicationFixtures.Item("i-sh", "共有中間品"),
             ApplicationFixtures.Item("i-m", "中間品M"),
             ApplicationFixtures.Item("i-tt", "製品T"), ApplicationFixtures.Item("i-zz", "製品Z")],
            [ApplicationFixtures.Facility("f-p", "機P", 10),
             ApplicationFixtures.Facility("f-dp", "機DP", 10),
             ApplicationFixtures.Facility("f-t", "機T", 10),
             ApplicationFixtures.Facility("f-z", "機Z", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-p", "共有中間品", [("i-u", 1)], [("i-sh", 1)],
                    [ApplicationFixtures.Pair("f-p", 6)]),
                ApplicationFixtures.Recipe("r-dp", "中間品M", [("i-sh", 1)], [("i-m", 1)],
                    [ApplicationFixtures.Pair("f-dp", 4)]),
                ApplicationFixtures.Recipe("r-t", "製品T", [("i-a4", 1), ("i-m", 1), ("i-z4", 1)],
                    [("i-tt", 1)], [ApplicationFixtures.Pair("f-t", 4)]),
                ApplicationFixtures.Recipe("r-z", "製品Z", [("i-sh", 1)], [("i-zz", 1)],
                    [ApplicationFixtures.Pair("f-z", 4)]),
            ]);
        var targets = new ProductionTarget[] { new("i-tt", 30), new("i-zz", 30) };

        Dictionary<string, (int Rank, int Order)> Positions(FlowGraphModel m) =>
            m.Nodes.ToDictionary(n => n.Id, n => (n.Rank, n.Order), StringComparer.Ordinal);

        (_, FlowGraphModel first) = Build(Snapshot(), targets: targets);
        (_, FlowGraphModel second) = Build(Snapshot(), targets: targets);

        Assert.Equal(Positions(first), Positions(second));
    }

    // FG-62: 掃引が交差を増やさないことを、交差が残るフィクスチャで確認する。
    // 採用順序の交差数は初期の Id 昇順順序と従来方式の再現順序のいずれも上回らない。
    [Fact]
    public void AdoptedOrderingDoesNotRegress()
    {
        // i-1 は f-b・f-c の、i-2 は f-a・f-b・f-c の入力で、どの順序でも境界
        // 0|1 に交差が残る構造（i-2→f-a が必ず i-1 のどちらかのエッジと交差する）。
        // 設備 Id 昇順は消費側と逆張りで、初期順序の交差は多い。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-1", "素材1", gatherable: true),
             ApplicationFixtures.Item("i-2", "素材2", gatherable: true),
             ApplicationFixtures.Item("i-o1", "製品1"), ApplicationFixtures.Item("i-o2", "製品2"),
             ApplicationFixtures.Item("i-o3", "製品3")],
            [ApplicationFixtures.Facility("f-a", "機A", 10),
             ApplicationFixtures.Facility("f-b", "機B", 10),
             ApplicationFixtures.Facility("f-c", "機C", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-a", "製品3", [("i-2", 1)],
                    [("i-o3", 1)], [ApplicationFixtures.Pair("f-a", 4)]),
                ApplicationFixtures.Recipe("r-b", "製品1", [("i-1", 1), ("i-2", 1)],
                    [("i-o1", 1)], [ApplicationFixtures.Pair("f-b", 4)]),
                ApplicationFixtures.Recipe("r-c", "製品2", [("i-1", 1), ("i-2", 1)],
                    [("i-o2", 1)], [ApplicationFixtures.Pair("f-c", 4)]),
            ]);

        (_, FlowGraphModel model) = Build(
            snapshot,
            targets:
            [
                new ProductionTarget("i-o1", 30), new ProductionTarget("i-o2", 30),
                new ProductionTarget("i-o3", 30),
            ]);

        int adopted = CountCrossings(model, AdoptedOrder(model));
        int initial = CountCrossings(model, IdAscendingOrder(model));
        int legacy = CountCrossings(model, LegacyUpstreamOrder(model));

        Assert.True(adopted > 0);
        Assert.True(adopted <= initial);
        Assert.True(adopted <= legacy);
    }
}
