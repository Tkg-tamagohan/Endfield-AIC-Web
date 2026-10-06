using EndfieldAicWeb.Application;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

public partial class FlowGraphModelBuilderTests
{
    // FG-63〜67: 交差ペア入替の後処理と逆方向タイブレーク（test-specification-phase40.md、仕様決定 CS）。

    // Phase 40（仕様決定 CS）で使う、同梱マスタの重息壌ガス系計画に近い構造の
    // フィクスチャ。種循環 i-y→f-u→i-sd→f-p→i-y は実計画の芽針循環と同型で、
    // i-y→f-r が循環からの脱出エッジ（実計画の 芽針→精錬（炭塊）と同型）になる。
    // 採取アイテム i-w（水相当）は浅い消費先 f-p（rank 2）と深い消費先 f-f（rank 6）に
    // 跨って供給するため、下流バリセンターキーが消費先のランク項に支配され、掃引の
    // 採用順序では i-sd の右に留まって供給エッジ（i-w→f-f）が中間ランクの
    // i-o→f-r・f-r→i-c1 と交差する。
    private static MasterDataSnapshot MultiRankConsumerFixture() =>
        ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-sd", "種相当"),
             ApplicationFixtures.Item("i-w", "水相当", gatherable: true),
             ApplicationFixtures.Item("i-y", "芽相当"),
             ApplicationFixtures.Item("i-o", "鉱物相当", gatherable: true),
             ApplicationFixtures.Item("i-c0", "炭相当"), ApplicationFixtures.Item("i-c1", "銅相当"),
             ApplicationFixtures.Item("i-x", "製品X"), ApplicationFixtures.Item("i-y2", "製品Y2")],
            [ApplicationFixtures.Facility("f-u", "採種機", 10),
             ApplicationFixtures.Facility("f-p", "栽培機", 10),
             ApplicationFixtures.Facility("f-r", "精錬炉", 10),
             ApplicationFixtures.Facility("f-f", "洪炉", 10),
             ApplicationFixtures.Facility("f-m", "成形機", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-u", "種", [("i-y", 1)], [("i-sd", 1)],
                    [ApplicationFixtures.Pair("f-u", 4)]),
                ApplicationFixtures.Recipe("r-p", "芽", [("i-sd", 1), ("i-w", 1)], [("i-y", 1)],
                    [ApplicationFixtures.Pair("f-p", 4)]),
                ApplicationFixtures.Recipe("r-r", "精錬", [("i-y", 1), ("i-o", 1)],
                    [("i-c0", 1), ("i-c1", 1)], [ApplicationFixtures.Pair("f-r", 4)]),
                ApplicationFixtures.Recipe("r-f", "製品X", [("i-w", 1), ("i-c0", 1)],
                    [("i-x", 1)], [ApplicationFixtures.Pair("f-f", 4)]),
                ApplicationFixtures.Recipe("r-m", "製品Y2", [("i-c1", 1)], [("i-y2", 1)],
                    [ApplicationFixtures.Pair("f-m", 4)]),
            ]);

    // FG-63: 交差ペア入替の後処理。掃引の採用順序が残す交差（i-w の供給エッジが
    // i-o→f-r・f-r→i-c1 と交差する 2 件）を、rank 1 の隣接ペア (i-sd, i-w) の
    // 入替で解消し、採用順序の交差数が 0 になる。
    // （実測: 後処理なしの掃引採用順序は [i-sd, i-w] で交差 2、入替後は [i-w, i-sd] で 0。）
    [Fact]
    public void AdjacentSwapPostProcessResolvesResidualCrossings()
    {
        (_, FlowGraphModel model) = Build(
            MultiRankConsumerFixture(),
            targets: [new ProductionTarget("i-x", 30), new ProductionTarget("i-y2", 30)]);

        // 構造確認: 初期の Id 昇順でも従来方式の上流掃引でも交差が残る。
        Assert.True(CountCrossings(model, IdAscendingOrder(model)) > 0);
        Assert.True(CountCrossings(model, LegacyUpstreamOrder(model)) > 0);

        // 後処理が受理した入替により i-w が消費設備寄りの最前列へ移る。
        Assert.Equal(1, Node(model, "item:i-w").Rank);
        Assert.Equal(1, Node(model, "item:i-sd").Rank);
        Assert.Equal(0, Node(model, "item:i-w").Order);
        Assert.Equal(1, Node(model, "item:i-sd").Order);
        Assert.Equal(0, CountCrossings(model, AdoptedOrder(model)));
    }

    // FG-64: どの隣接入替も交差を減らさない局所最適の配置（i-1・i-2 が f-a・f-b の
    // 双方に供給する完全二部構造）では、後処理で受理される入替がなく、採用順序と
    // 交差数が維持される。
    [Fact]
    public void AdjacentSwapPostProcessLeavesLocallyOptimalOrder()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-1", "素材1", gatherable: true),
             ApplicationFixtures.Item("i-2", "素材2", gatherable: true),
             ApplicationFixtures.Item("i-o1", "製品1"), ApplicationFixtures.Item("i-o2", "製品2")],
            [ApplicationFixtures.Facility("f-a", "機A", 10),
             ApplicationFixtures.Facility("f-b", "機B", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-a", "製品1", [("i-1", 1), ("i-2", 1)],
                    [("i-o1", 1)], [ApplicationFixtures.Pair("f-a", 4)]),
                ApplicationFixtures.Recipe("r-b", "製品2", [("i-1", 1), ("i-2", 1)],
                    [("i-o2", 1)], [ApplicationFixtures.Pair("f-b", 4)]),
            ]);

        (_, FlowGraphModel model) = Build(
            snapshot,
            targets: [new ProductionTarget("i-o1", 30), new ProductionTarget("i-o2", 30)]);

        Dictionary<string, int> adopted = AdoptedOrder(model);

        // どの順序でも境界 0|1 に交差 1 が残る構造（i-1→f-b と i-2→f-a が必ず交差）。
        Assert.Equal(1, CountCrossings(model, adopted));

        // ランク内の隣接ペアは各ランク 1 組だけで、いずれの入替も交差を減らさない
        // （入替後も交差 1）ことをテスト側の計測で確認する。
        var swapItems = new Dictionary<string, int>(adopted, StringComparer.Ordinal)
        {
            ["item:i-1"] = adopted["item:i-2"],
            ["item:i-2"] = adopted["item:i-1"],
        };
        Assert.True(CountCrossings(model, swapItems) >= 1);
        var swapFacs = new Dictionary<string, int>(adopted, StringComparer.Ordinal)
        {
            ["fac:f-a"] = adopted["fac:f-b"],
            ["fac:f-b"] = adopted["fac:f-a"],
        };
        Assert.True(CountCrossings(model, swapFacs) >= 1);

        // 受理される入替がないため採用順序は変わらない。
        Assert.Equal(0, Node(model, "item:i-1").Order);
        Assert.Equal(1, Node(model, "item:i-2").Order);
        Assert.Equal(0, Node(model, "fac:f-a").Order);
        Assert.Equal(1, Node(model, "fac:f-b").Order);
    }

    // FG-65: 環境供給設備（散布機 f-d）を持つ計画。掃引採用順序では散布機への供給
    // エッジ i-g→f-d と利用設備への供給エッジ i2→f-u が交差し、後処理は供給側の
    // 隣接ペア (i-g, i2) の入替を受理して交差を解消する。散布機 f-d（envFixed）を
    // 含むペア（f-u/f-d・f-d/f-q）は入替対象外で、入替のたびに BM の隣接挿入が
    // 再適用されるため、散布機は最小層の利用設備 f-u の直後に留まる。
    [Fact]
    public void AdjacentSwapPostProcessKeepsDispenserAdjacency()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i", "素材I", gatherable: true),
             ApplicationFixtures.Item("i2", "素材I2", gatherable: true),
             ApplicationFixtures.Item("i4", "素材I4", gatherable: true),
             ApplicationFixtures.Item("i-g", "ガス", TransportKind.Pipe, gatherable: true),
             ApplicationFixtures.Item("i-o", "中間O"), ApplicationFixtures.Item("i-j", "中間J"),
             ApplicationFixtures.Item("i-b", "中間B"),
             ApplicationFixtures.Item("i-z", "製品Z"), ApplicationFixtures.Item("i-z2", "製品Z2")],
            [ApplicationFixtures.Facility("f-u", "利用機", 10),
             ApplicationFixtures.Facility("f-q", "別機", 10),
             ApplicationFixtures.Facility("f-d", "散布機", 5),
             ApplicationFixtures.Facility("f-v", "統合機", 10),
             ApplicationFixtures.Facility("f-deep", "深部機", 10)],
            [ApplicationFixtures.Env("env-g", "環境G", "f-d", "i-g", 360)],
            [],
            [
                ApplicationFixtures.Recipe("r-u", "利用機レシピ", [("i", 1), ("i2", 1)],
                    [("i-o", 1), ("i-b", 1)], [ApplicationFixtures.Pair("f-u", 4, "env-g")]),
                ApplicationFixtures.Recipe("r-q", "別機レシピ", [("i4", 1)], [("i-j", 1)],
                    [ApplicationFixtures.Pair("f-q", 4)]),
                ApplicationFixtures.Recipe("r-v", "統合", [("i-o", 1), ("i-j", 1)], [("i-z", 1)],
                    [ApplicationFixtures.Pair("f-v", 4)]),
                ApplicationFixtures.Recipe("r-deep", "深部", [("i-b", 1)], [("i-z2", 1)],
                    [ApplicationFixtures.Pair("f-deep", 4)]),
            ]);

        (_, FlowGraphModel model) = Build(
            snapshot,
            targets: [new ProductionTarget("i-z", 30), new ProductionTarget("i-z2", 30)]);

        // 散布機は最小層の利用設備 f-u と同じランクで、その直後に留まる。
        Assert.Equal(Node(model, "fac:f-u").Rank, Node(model, "fac:f-d").Rank);
        Assert.Equal(Node(model, "fac:f-u").Order + 1, Node(model, "fac:f-d").Order);

        // 供給側の入替が受理され、採用順序の交差数は 0 になる。
        Assert.Equal(0, CountCrossings(model, AdoptedOrder(model)));
    }

    // FG-66: 下流キー同率（共通消費先 f-p を持つ i-x・i-c）のノード対で、
    // Id 昇順は i-c < i-x となり上流の生産設備の並び（f-f@0・f-m@1）と逆転する。
    // 逆方向キーが同率を上流隣接の並びで解き、採用順序は [i-x, i-c] になる。
    [Fact]
    public void ReverseDirectionTiebreakAlignsWithUpstreamNeighbors()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-a", "原料A", gatherable: true),
             ApplicationFixtures.Item("i-b", "原料B", gatherable: true),
             ApplicationFixtures.Item("i-x", "息壌相当"),
             ApplicationFixtures.Item("i-c", "圧力タンク相当"),
             ApplicationFixtures.Item("i-t", "最終製品")],
            [ApplicationFixtures.Facility("f-f", "洪炉", 10),
             ApplicationFixtures.Facility("f-m", "成形機", 10),
             ApplicationFixtures.Facility("f-p", "包装機", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-x", "息壌", [("i-a", 1)], [("i-x", 1)],
                    [ApplicationFixtures.Pair("f-f", 4)]),
                ApplicationFixtures.Recipe("r-c", "タンク", [("i-b", 1)], [("i-c", 1)],
                    [ApplicationFixtures.Pair("f-m", 4)]),
                ApplicationFixtures.Recipe("r-p", "包装", [("i-x", 1), ("i-c", 1)], [("i-t", 1)],
                    [ApplicationFixtures.Pair("f-p", 4)]),
            ]);

        (_, FlowGraphModel model) = Build(snapshot, targets: new ProductionTarget("i-t", 30));

        // 上流の生産設備の並びと整合する順序（Id 昇順では逆転する対）。
        Assert.Equal(Node(model, "fac:f-f").Rank, Node(model, "item:i-x").Rank - 1);
        Assert.Equal(Node(model, "fac:f-m").Rank, Node(model, "item:i-c").Rank - 1);
        Assert.Equal(Node(model, "fac:f-f").Order, Node(model, "item:i-x").Order);
        Assert.Equal(Node(model, "fac:f-m").Order, Node(model, "item:i-c").Order);
        Assert.Equal(0, CountCrossings(model, AdoptedOrder(model)));
    }

    // FG-67: 後処理が入替を受理するフィクスチャ（FG-63 と同じ構造）で Build を
    // 2 回実行しても Rank・Order が完全一致する（確定的動作）。
    [Fact]
    public void PostProcessOrderingIsDeterministic()
    {
        var targets = new ProductionTarget[] { new("i-x", 30), new("i-y2", 30) };

        Dictionary<string, (int Rank, int Order)> Positions(FlowGraphModel m) =>
            m.Nodes.ToDictionary(n => n.Id, n => (n.Rank, n.Order), StringComparer.Ordinal);

        (_, FlowGraphModel first) = Build(MultiRankConsumerFixture(), targets: targets);
        (_, FlowGraphModel second) = Build(MultiRankConsumerFixture(), targets: targets);

        Assert.Equal(Positions(first), Positions(second));
    }
}
