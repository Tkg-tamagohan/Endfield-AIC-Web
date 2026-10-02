using EndfieldAicWeb.Application;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

/// <summary>FG: 生産フローグラフのモデル構築（test-specification-phase15.md §2）。</summary>
public class FlowGraphModelBuilderTests
{
    private static (ProductionPlan Plan, FlowGraphModel Model) Build(
        MasterDataSnapshot snapshot,
        bool unadjusted = false,
        bool expandFacilities = false,
        params ProductionTarget[] targets)
    {
        ProductionPlan plan = ProductionCalculator.Calculate(
            snapshot, targets, new ContextFilter(), [], [], []);
        return (plan, FlowGraphModelBuilder.Build(
            plan, snapshot, new ContextFilter(), targets, unadjusted, expandFacilities));
    }

    private static FlowGraphNode Node(FlowGraphModel model, string id) =>
        Assert.Single(model.Nodes, n => n.Id == id);

    private static FlowGraphEdge Edge(
        FlowGraphModel model, string fromId, string toId, FlowGraphEdgeKind kind) =>
        Assert.Single(model.Edges, e => e.FromId == fromId && e.ToId == toId && e.Kind == kind);

    // FG-01: 目標アイテムはフラグ付きノードになる。
    [Fact]
    public void TargetItemHasFlag()
    {
        (_, FlowGraphModel model) = Build(ApplicationFixtures.A02(), targets: new ProductionTarget("i-t", 72));

        Assert.True(Node(model, "item:i-t").IsTarget);
        Assert.False(Node(model, "item:i-u").IsTarget);
    }

    // FG-02: 採取供給は共通の採取ノードへ集約される。
    [Fact]
    public void GatheredSupplyUsesSharedNode()
    {
        (_, FlowGraphModel model) = Build(ApplicationFixtures.A02(), targets: new ProductionTarget("i-t", 72));

        FlowGraphNode gather = Assert.Single(model.Nodes, n => n.Kind == FlowGraphNodeKind.Gather);
        Assert.Equal(FlowGraphModelBuilder.GatherNodeId, gather.Id);
        // i-u は i-t 72/分 × 入力 4 = 288/分を採取で賄う。
        Assert.Equal(288, Edge(model, "gather", "item:i-u", FlowGraphEdgeKind.Gathered).RatePerMinute, 6);
    }

    // FG-03: 設備ノードは FacilityId で集約される。
    [Fact]
    public void FacilityNodeIsSharedAcrossRecipes()
    {
        (_, FlowGraphModel model) = Build(
            ApplicationFixtures.A03(),
            targets: [new ProductionTarget("i-x", 30), new ProductionTarget("i-y", 20)]);

        Assert.Single(model.Nodes, n => n.Kind == FlowGraphNodeKind.Facility);
        Edge(model, "fac:f-sh", "item:i-x", FlowGraphEdgeKind.RecipeOutput);
        Edge(model, "fac:f-sh", "item:i-y", FlowGraphEdgeKind.RecipeOutput);
    }

    // FG-04: 同一ノード対・同種別のエッジは流量を合算する。
    [Fact]
    public void SamePairEdgesAreAggregated()
    {
        (_, FlowGraphModel model) = Build(
            ApplicationFixtures.A03(),
            targets: [new ProductionTarget("i-x", 30), new ProductionTarget("i-y", 20)]);

        // r-x 30/分 + r-y 20/分 の入力が 1 本に合算される。
        Assert.Equal(50, Edge(model, "item:i-u", "fac:f-sh", FlowGraphEdgeKind.RecipeInput).RatePerMinute, 6);
    }

    // FG-05: SortOrder > 0 の出力は副産物フラグが立つ。
    [Fact]
    public void ByproductOutputIsFlagged()
    {
        Recipe recipe = ApplicationFixtures.Recipe(
            "r-bp", "副産物レシピ", [("i-u", 1)], [("i-p", 1), ("i-s", 1)],
            [ApplicationFixtures.Pair("f-a", 6)]);
        recipe.Outputs[1].SortOrder = 1;
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
             ApplicationFixtures.Item("i-p", "主産物"),
             ApplicationFixtures.Item("i-s", "副産物")],
            [ApplicationFixtures.Facility("f-a", "加工機", 10)],
            [], [], [recipe]);

        (_, FlowGraphModel model) = Build(snapshot, targets: new ProductionTarget("i-p", 10));

        Assert.False(Edge(model, "fac:f-a", "item:i-p", FlowGraphEdgeKind.RecipeOutput).IsByproduct);
        Assert.True(Edge(model, "fac:f-a", "item:i-s", FlowGraphEdgeKind.RecipeOutput).IsByproduct);
    }

    // FG-06: 環境消費は専用エッジで供給設備ノードへ接続する。
    [Fact]
    public void EnvironmentConsumeHasDedicatedEdge()
    {
        (_, FlowGraphModel model) = Build(ApplicationFixtures.A01(), targets: new ProductionTarget("i-part", 60));

        Assert.Equal(360, Edge(model, "item:i-gas", "fac:f-disp", FlowGraphEdgeKind.EnvironmentConsume).RatePerMinute, 6);
        Assert.Equal("×1（散布機 1）", Node(model, "fac:f-disp").Note);
    }

    // FG-07: 固定消費は切上げ台数を掛ける。
    [Fact]
    public void FixedConsumptionScalesByCeilCount()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-in", "原料", gatherable: true),
             ApplicationFixtures.Item("i-pow", "動力素材", gatherable: true),
             ApplicationFixtures.Item("i-out", "製品")],
            [ApplicationFixtures.Facility("f-fc", "化学機", 10)],
            [], [],
            [ApplicationFixtures.Recipe("r-fc", "製品", [("i-in", 1)], [("i-out", 1)],
                [ApplicationFixtures.Pair("f-fc", 30,
                    fc: new FixedConsumption { ItemId = "i-pow", RatePerMinute = 12 })])]);

        // i-out 2.5/分 → 実数 1.25 台 → 切上げ 2 台 → 12 × 2 = 24。
        (_, FlowGraphModel model) = Build(snapshot, targets: new ProductionTarget("i-out", 2.5));

        Assert.Equal(24, Edge(model, "item:i-pow", "fac:f-fc", FlowGraphEdgeKind.FixedConsumption).RatePerMinute, 6);
    }

    // FG-08: 層割りは出口側起点の最長距離で行う（仕様決定 BF）。
    [Fact]
    public void RanksFollowLongestPath()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
             ApplicationFixtures.Item("i-mid", "中間品"),
             ApplicationFixtures.Item("i-t", "製品")],
            [ApplicationFixtures.Facility("f-a", "機A", 10), ApplicationFixtures.Facility("f-b", "機B", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-mid", "中間品", [("i-u", 1)], [("i-mid", 1)],
                    [ApplicationFixtures.Pair("f-a", 6)]),
                ApplicationFixtures.Recipe("r-fin", "製品", [("i-mid", 1)], [("i-t", 1)],
                    [ApplicationFixtures.Pair("f-b", 6)]),
            ]);

        (_, FlowGraphModel model) = Build(snapshot, targets: new ProductionTarget("i-t", 10));

        Assert.Equal(0, Node(model, "gather").Rank);
        Assert.True(Node(model, "fac:f-b").Rank > Node(model, "fac:f-a").Rank);
        Assert.True(Node(model, "item:i-t").Rank > Node(model, "fac:f-b").Rank);
        Assert.Equal(model.Nodes.Max(n => n.Rank), Node(model, "item:i-t").Rank);
    }

    // FG-09: 循環経路を含む計画でも停止する（後退エッジを無視）。ループ内の目標は Layer0（仕様決定 BH）。
    [Fact]
    public void CyclicPlanStillRanks()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-a", "素材A"), ApplicationFixtures.Item("i-b", "素材B")],
            [ApplicationFixtures.Facility("f-a", "機A", 10), ApplicationFixtures.Facility("f-b", "機B", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-a", "素材B", [("i-a", 1)], [("i-b", 1)],
                    [ApplicationFixtures.Pair("f-a", 6)]),
                ApplicationFixtures.Recipe("r-b", "素材A", [("i-b", 1)], [("i-a", 1)],
                    [ApplicationFixtures.Pair("f-b", 6)]),
            ]);
        var plan = new ProductionPlan
        {
            ItemRequirements =
            [
                new ItemRequirement("i-a", 10, [new SupplyPortion(SupplyKind.Recipe, "r-b", 10)], 0),
                new ItemRequirement("i-b", 10, [new SupplyPortion(SupplyKind.Recipe, "r-a", 10)], 0),
            ],
            FacilityRequirements =
            [
                new FacilityRequirement("f-a", 1, 1),
                new FacilityRequirement("f-b", 1, 1),
            ],
            RecipeRuns =
            [
                new RecipeRun("r-a", "f-a", 10),
                new RecipeRun("r-b", "f-b", 10),
            ],
            PairSelections =
            [
                new PairSelection("i-a", "r-b", ApplicationFixtures.Pair("f-b", 6, recipeId: "r-b")),
                new PairSelection("i-b", "r-a", ApplicationFixtures.Pair("f-a", 6, recipeId: "r-a")),
            ],
            EnvironmentRequirements = [],
            TotalPowerConsumption = 0,
            Surpluses = [],
            FlowAdjustments = [],
            Warnings = [new CalculationWarning(WarningCode.CycleDetected, "cycle")],
        };

        FlowGraphModel model = FlowGraphModelBuilder.Build(
            plan, snapshot, new ContextFilter(), [new ProductionTarget("i-a", 10)], false);

        Assert.Equal(4, model.Nodes.Count);
        Assert.Equal(4, model.Edges.Count);
        Assert.True(model.Nodes.Max(n => n.Rank) >= 1);
        Assert.Equal(model.Nodes.Max(n => n.Rank), Node(model, "item:i-a").Rank);
    }

    // FG-10: 未充足のみのアイテムも Layer0（仕様決定 BF。単独ノードのため rank 0）。
    [Fact]
    public void UnmetOnlyItemIsRankZero()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-need", "生産不能品")],
            [ApplicationFixtures.Facility("f-a", "機A", 10)],
            [], [], []);

        (_, FlowGraphModel model) = Build(snapshot, targets: new ProductionTarget("i-need", 5));

        FlowGraphNode node = Node(model, "item:i-need");
        Assert.Equal(5, node.UnmetPerMinute, 6);
        Assert.Equal(model.Nodes.Max(n => n.Rank), node.Rank);
        Assert.DoesNotContain(model.Nodes, n => n.Kind == FlowGraphNodeKind.Gather);
    }

    // FG-27: 消費される目標は消費設備の直上流に置かれる（仕様決定 BG）。
    [Fact]
    public void ConsumedTargetSitsBeforeConsumer()
    {
        // 目標 i-t が別目標 i-z の素材でもある計画。i-t は Layer0 に置かれない。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
             ApplicationFixtures.Item("i-t", "中間目標"),
             ApplicationFixtures.Item("i-z", "最終目標")],
            [ApplicationFixtures.Facility("f-t", "機T", 10), ApplicationFixtures.Facility("f-z", "機Z", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-t", "中間目標", [("i-u", 1)], [("i-t", 1)],
                    [ApplicationFixtures.Pair("f-t", 6)]),
                ApplicationFixtures.Recipe("r-z", "最終目標", [("i-t", 1)], [("i-z", 1)],
                    [ApplicationFixtures.Pair("f-z", 6)]),
            ]);

        (_, FlowGraphModel model) = Build(
            snapshot, targets: [new ProductionTarget("i-t", 10), new ProductionTarget("i-z", 20)]);

        Assert.Equal(Node(model, "fac:f-z").Rank - 1, Node(model, "item:i-t").Rank);
        Assert.True(Node(model, "item:i-t").Rank < Node(model, "item:i-z").Rank);
        Assert.Equal(model.Nodes.Max(n => n.Rank), Node(model, "item:i-z").Rank);
    }

    // FG-28: 未消費の副産物は Layer0（右端列）にまとまる（仕様決定 BF）。
    [Fact]
    public void UnconsumedByproductSitsAtRightEdge()
    {
        Recipe recipe = ApplicationFixtures.Recipe(
            "r-bp", "副産物レシピ", [("i-u", 1)], [("i-p", 1), ("i-s", 1)],
            [ApplicationFixtures.Pair("f-a", 6)]);
        recipe.Outputs[1].SortOrder = 1;
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
             ApplicationFixtures.Item("i-p", "主産物"),
             ApplicationFixtures.Item("i-s", "副産物")],
            [ApplicationFixtures.Facility("f-a", "加工機", 10)],
            [], [], [recipe]);

        (_, FlowGraphModel model) = Build(snapshot, targets: new ProductionTarget("i-p", 10));

        Assert.Equal(Node(model, "item:i-p").Rank, Node(model, "item:i-s").Rank);
        Assert.Equal(model.Nodes.Max(n => n.Rank), Node(model, "item:i-s").Rank);
    }

    // FG-29: 鎖の短い目標も Layer0（右端列）に固定される（仕様決定 BF）。
    [Fact]
    public void ShortChainTargetIsAnchoredRight()
    {
        // i-x は 1 段、i-t は 3 段の深さを持つ 2 目標。旧来の起点側最長パスでは i-x が中間に浮いた。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
             ApplicationFixtures.Item("i-m", "中間品"),
             ApplicationFixtures.Item("i-t", "製品"),
             ApplicationFixtures.Item("i-x", "短鎖製品")],
            [ApplicationFixtures.Facility("f-a", "機A", 10),
             ApplicationFixtures.Facility("f-b", "機B", 10),
             ApplicationFixtures.Facility("f-c", "機C", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-m", "中間品", [("i-u", 1)], [("i-m", 1)],
                    [ApplicationFixtures.Pair("f-a", 6)]),
                ApplicationFixtures.Recipe("r-t", "製品", [("i-m", 1)], [("i-t", 1)],
                    [ApplicationFixtures.Pair("f-b", 6)]),
                ApplicationFixtures.Recipe("r-x", "短鎖製品", [("i-u", 1)], [("i-x", 1)],
                    [ApplicationFixtures.Pair("f-c", 6)]),
            ]);

        (_, FlowGraphModel model) = Build(
            snapshot, targets: [new ProductionTarget("i-t", 10), new ProductionTarget("i-x", 10)]);

        int rightmost = model.Nodes.Max(n => n.Rank);
        Assert.Equal(rightmost, Node(model, "item:i-t").Rank);
        Assert.Equal(rightmost, Node(model, "item:i-x").Rank);
        Assert.True(Node(model, "item:i-x").Rank > Node(model, "fac:f-c").Rank);
    }

    // FG-30: 台数分表示のユニットは設備と同じ層規則で割り当たる（仕様決定 BF・AO）。
    [Fact]
    public void UnitNodesShareFacilityLayer()
    {
        // i-t 72/分 → 切上げ 3 台。ユニットはすべて i-t の 1 つ左の列に揃う。
        (_, FlowGraphModel model) = Build(
            ApplicationFixtures.A02(), expandFacilities: true, targets: new ProductionTarget("i-t", 72));

        int targetRank = Node(model, "item:i-t").Rank;
        foreach (int i in new[] { 0, 1, 2 })
        {
            Assert.Equal(targetRank - 1, Node(model, $"facunit:f-t#{i}").Rank);
        }
    }

    // FG-31: 出力を持たない設備（散布機）は Layer0 に置かず、消費アイテムの直下流に置く（仕様決定 BF）。
    [Fact]
    public void DispenserSitsDownstreamOfConsumedItem()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
             ApplicationFixtures.Item("i-gas", "活性ガス", TransportKind.Pipe, gatherable: true),
             ApplicationFixtures.Item("i-x", "製品X")],
            [ApplicationFixtures.Facility("f-a", "機A", 10),
             ApplicationFixtures.Facility("f-disp", "散布機", 5)],
            [ApplicationFixtures.Env("env-g", "ガス環境", "f-disp", "i-gas", 360)],
            [],
            [
                ApplicationFixtures.Recipe("r-x", "製品X", [("i-u", 1)], [("i-x", 1)],
                    [ApplicationFixtures.Pair("f-a", 6, "env-g")]),
            ]);

        (_, FlowGraphModel model) = Build(snapshot, targets: new ProductionTarget("i-x", 10));

        // 散布機はガスの 1 列右。目標アイテムと同列の Layer0 には置かれない。
        // ガスはアイテムでは最右列に残り、散布機はそれよりさらに 1 列右（契約: Layer0 の右端はアイテムのみ）。
        FlowGraphNode gas = Node(model, "item:i-gas");
        Assert.Equal(model.Nodes.Where(n => n.Kind == FlowGraphNodeKind.Item).Max(n => n.Rank), gas.Rank);
        Assert.Equal(gas.Rank + 1, Node(model, "fac:f-disp").Rank);
    }

    // FG-32: 複数の出口を持つ循環では、浅い出口で仮確定したノードを深い出口側へ緩和する（仕様決定 BH）。
    [Fact]
    public void CyclicNodeRelaxesTowardDeepestExit()
    {
        // i-a と i-b が相互生産の循環。i-a は浅い出口（→i-z 目標）、i-b は深い出口（→i-m→i-w 目標）。
        // i-a は i-b 側の深い出口まで緩和され、i-a→f-b の供給エッジが後退エッジにならない。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-a", "素材A"), ApplicationFixtures.Item("i-b", "素材B"),
             ApplicationFixtures.Item("i-m", "中間品"), ApplicationFixtures.Item("i-z", "製品Z"),
             ApplicationFixtures.Item("i-w", "製品W")],
            [ApplicationFixtures.Facility("f-a", "機A", 10), ApplicationFixtures.Facility("f-b", "機B", 10),
             ApplicationFixtures.Facility("f-z", "機Z", 10), ApplicationFixtures.Facility("f-w1", "機W1", 10),
             ApplicationFixtures.Facility("f-w2", "機W2", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-ab", "素材B", [("i-a", 1)], [("i-b", 1)],
                    [ApplicationFixtures.Pair("f-b", 6)]),
                ApplicationFixtures.Recipe("r-ba", "素材A", [("i-b", 1)], [("i-a", 1)],
                    [ApplicationFixtures.Pair("f-a", 6)]),
                ApplicationFixtures.Recipe("r-z", "製品Z", [("i-a", 1)], [("i-z", 1)],
                    [ApplicationFixtures.Pair("f-z", 6)]),
                ApplicationFixtures.Recipe("r-w1", "中間品", [("i-b", 1)], [("i-m", 1)],
                    [ApplicationFixtures.Pair("f-w1", 6)]),
                ApplicationFixtures.Recipe("r-w2", "製品W", [("i-m", 1)], [("i-w", 1)],
                    [ApplicationFixtures.Pair("f-w2", 6)]),
            ]);
        var plan = new ProductionPlan
        {
            ItemRequirements =
            [
                new ItemRequirement("i-a", 20, [new SupplyPortion(SupplyKind.Recipe, "r-ba", 20)], 0),
                new ItemRequirement("i-b", 20, [new SupplyPortion(SupplyKind.Recipe, "r-ab", 20)], 0),
                new ItemRequirement("i-m", 10, [new SupplyPortion(SupplyKind.Recipe, "r-w1", 10)], 0),
                new ItemRequirement("i-z", 10, [new SupplyPortion(SupplyKind.Recipe, "r-z", 10)], 0),
                new ItemRequirement("i-w", 10, [new SupplyPortion(SupplyKind.Recipe, "r-w2", 10)], 0),
            ],
            FacilityRequirements =
            [
                new FacilityRequirement("f-a", 1, 1),
                new FacilityRequirement("f-b", 1, 1),
                new FacilityRequirement("f-z", 1, 1),
                new FacilityRequirement("f-w1", 1, 1),
                new FacilityRequirement("f-w2", 1, 1),
            ],
            RecipeRuns =
            [
                new RecipeRun("r-ab", "f-b", 10),
                new RecipeRun("r-ba", "f-a", 10),
                new RecipeRun("r-z", "f-z", 10),
                new RecipeRun("r-w1", "f-w1", 10),
                new RecipeRun("r-w2", "f-w2", 10),
            ],
            PairSelections =
            [
                new PairSelection("i-a", "r-ba", ApplicationFixtures.Pair("f-a", 6, recipeId: "r-ba")),
                new PairSelection("i-b", "r-ab", ApplicationFixtures.Pair("f-b", 6, recipeId: "r-ab")),
                new PairSelection("i-m", "r-w1", ApplicationFixtures.Pair("f-w1", 6, recipeId: "r-w1")),
                new PairSelection("i-z", "r-z", ApplicationFixtures.Pair("f-z", 6, recipeId: "r-z")),
                new PairSelection("i-w", "r-w2", ApplicationFixtures.Pair("f-w2", 6, recipeId: "r-w2")),
            ],
            EnvironmentRequirements = [],
            TotalPowerConsumption = 0,
            Surpluses = [],
            FlowAdjustments = [],
            Warnings = [new CalculationWarning(WarningCode.CycleDetected, "cycle")],
        };

        FlowGraphModel model = FlowGraphModelBuilder.Build(
            plan, snapshot, new ContextFilter(), [new ProductionTarget("i-z", 10), new ProductionTarget("i-w", 10)], false);

        // i-a は i-b 側の深い出口まで緩和され、i-a→f-b の供給エッジは左→右のまま後退しない。
        Assert.True(Node(model, "item:i-a").Rank < Node(model, "fac:f-b").Rank);
        // 後退エッジになるのは i-b→f-a 側だけ。
        Assert.True(Node(model, "item:i-b").Rank > Node(model, "fac:f-a").Rank);
    }

    // FG-33: 重なった循環でも、自分を経由しない出口への最長単純経路で層を引き直す（仕様決定 BH）。
    [Fact]
    public void OverlappingCyclesKeepLongestExitPath()
    {
        // C1: i-v↔i-u（f-v/f-u）、C2: i-u→i-x→i-u（f-x/f-u2）。i-v は浅い出口（f-w→i-w→i-e）、
        // i-x は目標 i-z への出口を持つ。i-u が i-v 経由で先に確定しても、i-v・i-x の層は
        // 出口への最長単純経路（i-v→f-v→i-u→f-x→i-x→f-x2→i-z、i-x→f-u2→i-u→f-u→i-v→f-w→i-w→f-e→i-e）になる。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-v", "素材V"), ApplicationFixtures.Item("i-u", "素材U"),
             ApplicationFixtures.Item("i-x", "素材X"), ApplicationFixtures.Item("i-w", "中間品W"),
             ApplicationFixtures.Item("i-z", "製品Z"), ApplicationFixtures.Item("i-e", "製品E")],
            [ApplicationFixtures.Facility("f-v", "機V", 10), ApplicationFixtures.Facility("f-u", "機U", 10),
             ApplicationFixtures.Facility("f-x", "機X", 10), ApplicationFixtures.Facility("f-u2", "機U2", 10),
             ApplicationFixtures.Facility("f-x2", "機X2", 10), ApplicationFixtures.Facility("f-w", "機W", 10),
             ApplicationFixtures.Facility("f-e", "機E", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-vu", "素材U", [("i-v", 1)], [("i-u", 1)],
                    [ApplicationFixtures.Pair("f-v", 6)]),
                ApplicationFixtures.Recipe("r-uv", "素材V", [("i-u", 1)], [("i-v", 1)],
                    [ApplicationFixtures.Pair("f-u", 6)]),
                ApplicationFixtures.Recipe("r-ux", "素材X", [("i-u", 1)], [("i-x", 1)],
                    [ApplicationFixtures.Pair("f-x", 6)]),
                ApplicationFixtures.Recipe("r-xu", "素材U", [("i-x", 1)], [("i-u", 1)],
                    [ApplicationFixtures.Pair("f-u2", 6)]),
                ApplicationFixtures.Recipe("r-xz", "製品Z", [("i-x", 1)], [("i-z", 1)],
                    [ApplicationFixtures.Pair("f-x2", 6)]),
                ApplicationFixtures.Recipe("r-vw", "中間品W", [("i-v", 1)], [("i-w", 1)],
                    [ApplicationFixtures.Pair("f-w", 6)]),
                ApplicationFixtures.Recipe("r-we", "製品E", [("i-w", 1)], [("i-e", 1)],
                    [ApplicationFixtures.Pair("f-e", 6)]),
            ]);
        var plan = new ProductionPlan
        {
            ItemRequirements =
            [
                new ItemRequirement("i-v", 20, [new SupplyPortion(SupplyKind.Recipe, "r-uv", 20)], 0),
                new ItemRequirement("i-u", 20, [new SupplyPortion(SupplyKind.Recipe, "r-vu", 20)], 0),
                new ItemRequirement("i-x", 20, [new SupplyPortion(SupplyKind.Recipe, "r-ux", 20)], 0),
                new ItemRequirement("i-w", 10, [new SupplyPortion(SupplyKind.Recipe, "r-vw", 10)], 0),
                new ItemRequirement("i-z", 10, [new SupplyPortion(SupplyKind.Recipe, "r-xz", 10)], 0),
                new ItemRequirement("i-e", 10, [new SupplyPortion(SupplyKind.Recipe, "r-we", 10)], 0),
            ],
            FacilityRequirements =
            [
                new FacilityRequirement("f-v", 1, 1), new FacilityRequirement("f-u", 1, 1),
                new FacilityRequirement("f-x", 1, 1), new FacilityRequirement("f-u2", 1, 1),
                new FacilityRequirement("f-x2", 1, 1), new FacilityRequirement("f-w", 1, 1),
                new FacilityRequirement("f-e", 1, 1),
            ],
            RecipeRuns =
            [
                new RecipeRun("r-vu", "f-v", 10), new RecipeRun("r-uv", "f-u", 10),
                new RecipeRun("r-ux", "f-x", 10), new RecipeRun("r-xu", "f-u2", 10),
                new RecipeRun("r-xz", "f-x2", 10), new RecipeRun("r-vw", "f-w", 10),
                new RecipeRun("r-we", "f-e", 10),
            ],
            PairSelections =
            [
                new PairSelection("i-v", "r-uv", ApplicationFixtures.Pair("f-u", 6, recipeId: "r-uv")),
                new PairSelection("i-u", "r-vu", ApplicationFixtures.Pair("f-v", 6, recipeId: "r-vu")),
                new PairSelection("i-x", "r-ux", ApplicationFixtures.Pair("f-x", 6, recipeId: "r-ux")),
                new PairSelection("i-w", "r-vw", ApplicationFixtures.Pair("f-w", 6, recipeId: "r-vw")),
                new PairSelection("i-z", "r-xz", ApplicationFixtures.Pair("f-x2", 6, recipeId: "r-xz")),
                new PairSelection("i-e", "r-we", ApplicationFixtures.Pair("f-e", 6, recipeId: "r-we")),
            ],
            EnvironmentRequirements = [],
            TotalPowerConsumption = 0,
            Surpluses = [],
            FlowAdjustments = [],
            Warnings = [new CalculationWarning(WarningCode.CycleDetected, "cycle")],
        };

        FlowGraphModel model = FlowGraphModelBuilder.Build(
            plan, snapshot, new ContextFilter(), [new ProductionTarget("i-z", 10), new ProductionTarget("i-e", 10)], false);

        // 最長単純経路どおり: i-x は出口 i-z から 8 段、i-v は 6 段の深さに置かれる。
        Assert.Equal(Node(model, "item:i-z").Rank - 8, Node(model, "item:i-x").Rank);
        Assert.Equal(Node(model, "item:i-z").Rank - 6, Node(model, "item:i-v").Rank);
    }

    // FG-34: 循環内の目標が別の出口へも分岐するとき、層はその目標を端点として数える（仕様決定 BH）。
    [Fact]
    public void CycleTargetStopsExitPath()
    {
        // i-a は目標かつ循環（i-a→f-1→i-b→f-2→i-a）の構成要素で、別目標 i-z への分岐（f-3）も持つ。
        // i-b の層は「i-a までの 2 段」。i-a を通り越して i-z まで数えると 4 段に膨らむ。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-a", "循環目標"), ApplicationFixtures.Item("i-b", "循環素材"),
             ApplicationFixtures.Item("i-z", "最終目標")],
            [ApplicationFixtures.Facility("f-1", "機1", 10), ApplicationFixtures.Facility("f-2", "機2", 10),
             ApplicationFixtures.Facility("f-3", "機3", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-1", "循環素材", [("i-a", 1)], [("i-b", 1)],
                    [ApplicationFixtures.Pair("f-1", 6)]),
                ApplicationFixtures.Recipe("r-2", "循環目標", [("i-b", 1)], [("i-a", 1)],
                    [ApplicationFixtures.Pair("f-2", 6)]),
                ApplicationFixtures.Recipe("r-3", "最終目標", [("i-a", 1)], [("i-z", 1)],
                    [ApplicationFixtures.Pair("f-3", 6)]),
            ]);
        var plan = new ProductionPlan
        {
            ItemRequirements =
            [
                new ItemRequirement("i-a", 20, [new SupplyPortion(SupplyKind.Recipe, "r-2", 20)], 0),
                new ItemRequirement("i-b", 10, [new SupplyPortion(SupplyKind.Recipe, "r-1", 10)], 0),
                new ItemRequirement("i-z", 10, [new SupplyPortion(SupplyKind.Recipe, "r-3", 10)], 0),
            ],
            FacilityRequirements =
            [
                new FacilityRequirement("f-1", 1, 1), new FacilityRequirement("f-2", 1, 1),
                new FacilityRequirement("f-3", 1, 1),
            ],
            RecipeRuns =
            [
                new RecipeRun("r-1", "f-1", 10), new RecipeRun("r-2", "f-2", 20),
                new RecipeRun("r-3", "f-3", 10),
            ],
            PairSelections =
            [
                new PairSelection("i-a", "r-2", ApplicationFixtures.Pair("f-2", 6, recipeId: "r-2")),
                new PairSelection("i-b", "r-1", ApplicationFixtures.Pair("f-1", 6, recipeId: "r-1")),
                new PairSelection("i-z", "r-3", ApplicationFixtures.Pair("f-3", 6, recipeId: "r-3")),
            ],
            EnvironmentRequirements = [],
            TotalPowerConsumption = 0,
            Surpluses = [],
            FlowAdjustments = [],
            Warnings = [new CalculationWarning(WarningCode.CycleDetected, "cycle")],
        };

        FlowGraphModel model = FlowGraphModelBuilder.Build(
            plan, snapshot, new ContextFilter(), [new ProductionTarget("i-a", 10), new ProductionTarget("i-z", 10)], false);

        // 循環内の目標 i-a は Layer0（右端）に留まり、i-b はその 2 段上流。
        Assert.Equal(model.Nodes.Max(n => n.Rank), Node(model, "item:i-a").Rank);
        Assert.Equal(Node(model, "item:i-a").Rank - 2, Node(model, "item:i-b").Rank);
    }

    // FG-35: 計算機が生成した循環プランでも、目標は右端に留まり循環閉鎖だけが後退する（仕様決定 BH）。
    [Fact]
    public void CalculatedCyclePlanKeepsTargetRightmost()
    {
        // 種↔製品の相互生産（実データの芽針循環と同じ形）。r-p は外部投入 i-u を必要とする。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-seed", "種"), ApplicationFixtures.Item("i-u", "水", gatherable: true),
             ApplicationFixtures.Item("i-y", "製品")],
            [ApplicationFixtures.Facility("f-p", "栽培機", 10), ApplicationFixtures.Facility("f-s", "採種機", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-p", "製品", [("i-seed", 1), ("i-u", 1)], [("i-y", 1)],
                    [ApplicationFixtures.Pair("f-p", 6)]),
                ApplicationFixtures.Recipe("r-s", "種", [("i-y", 1)], [("i-seed", 1)],
                    [ApplicationFixtures.Pair("f-s", 6)]),
            ]);

        (_, FlowGraphModel model) = Build(snapshot, targets: new ProductionTarget("i-y", 10));

        // 消費される目標 i-y は Layer0（最右列）。後退するのは循環を閉じる i-y→f-s だけ。
        Assert.Equal(model.Nodes.Max(n => n.Rank), Node(model, "item:i-y").Rank);
        Assert.True(Node(model, "item:i-y").Rank > Node(model, "fac:f-s").Rank);
        Assert.True(Node(model, "item:i-seed").Rank < Node(model, "fac:f-p").Rank);
        Assert.Equal(0, Node(model, "gather").Rank);
    }

    // FG-11: 余剰のみに登場するアイテムもノード化する。
    [Fact]
    public void SurplusOnlyItemBecomesNode()
    {
        (_, FlowGraphModel model) = Build(ApplicationFixtures.A05(), targets: new ProductionTarget("i-side", 8));

        // ev-ltd 無効で i-ltd の産出 8/分は全量余剰。ItemRequirements には載らない。
        FlowGraphNode node = Node(model, "item:i-ltd");
        Assert.Equal(0, node.RequiredPerMinute, 6);
        Assert.Equal(8, node.SurplusPerMinute, 6);
    }

    // FG-12: 未調整ビューでは ResultViewBuilder と同じ設備倍率を掛ける。
    [Fact]
    public void UnadjustedScalesRecipeEdges()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.A02();
        var targets = new[] { new ProductionTarget("i-t", 72) };

        (_, FlowGraphModel adjusted) = Build(snapshot, targets: targets);
        (_, FlowGraphModel unadjusted) = Build(snapshot, unadjusted: true, targets: targets);

        // 実数 2.4 台 → 切上げ 3 台 → 倍率 1.25（出力 72 → 90、入力 288 → 360）。
        Assert.Equal(72, Edge(adjusted, "fac:f-t", "item:i-t", FlowGraphEdgeKind.RecipeOutput).RatePerMinute, 6);
        Assert.Equal(90, Edge(unadjusted, "fac:f-t", "item:i-t", FlowGraphEdgeKind.RecipeOutput).RatePerMinute, 6);
        Assert.Equal(360, Edge(unadjusted, "item:i-u", "fac:f-t", FlowGraphEdgeKind.RecipeInput).RatePerMinute, 6);
    }

    // FG-13: 設備ユニットへの入力流量が輸送容量を超えるエッジと両端ノードを赤化する（仕様決定 AN）。
    [Fact]
    public void OverCapacityIsFlagged()
    {
        // i-t 60/分 → f-t は 2 台（実数 2.0）でユニット入力は各 120/分（ベルト 30 個/分超過）。
        (_, FlowGraphModel model) = Build(ApplicationFixtures.A02(), targets: new ProductionTarget("i-t", 60));

        Assert.True(Edge(model, "item:i-u", "fac:f-t", FlowGraphEdgeKind.RecipeInput).OverCapacity);
        Assert.True(Node(model, "item:i-u").OverCapacity);
        Assert.True(Node(model, "fac:f-t").OverCapacity);
    }

    // FG-15: 産出・需要だけの超過は対象外（仕様決定 AN）。
    [Fact]
    public void OutputOnlyOverflowIsNotFlagged()
    {
        // i-t の産出・需要は 60/分で容量超過だが、設備への入力ではないため対象外。
        (_, FlowGraphModel model) = Build(ApplicationFixtures.A02(), targets: new ProductionTarget("i-t", 60));

        Assert.False(Node(model, "item:i-t").OverCapacity);
        Assert.False(Edge(model, "fac:f-t", "item:i-t", FlowGraphEdgeKind.RecipeOutput).OverCapacity);
        Assert.False(Edge(model, "gather", "item:i-u", FlowGraphEdgeKind.Gathered).OverCapacity);
    }

    // FG-16: 環境消費の入力も判定対象（仕様決定 AN）。
    [Fact]
    public void EnvironmentConsumeIsChecked()
    {
        // A-01: 散布機 1 台あたり i-gas 360/分（パイプ 60 個/分超過）。
        (_, FlowGraphModel model) = Build(ApplicationFixtures.A01(), targets: new ProductionTarget("i-part", 60));

        Assert.True(Edge(model, "item:i-gas", "fac:f-disp", FlowGraphEdgeKind.EnvironmentConsume).OverCapacity);
        Assert.True(Node(model, "item:i-gas").OverCapacity);
        Assert.True(Node(model, "fac:f-disp").OverCapacity);
    }

    // FG-17: 設備を切上台数ぶんのユニットへ展開する（仕様決定 AO）。
    [Fact]
    public void ExpandFacilitiesEmitsUnitNodes()
    {
        // i-t 72/分 → 実数 2.4 台 → 切上げ 3 台。ラン占有は 1.0・1.0・0.4。
        (_, FlowGraphModel model) = Build(
            ApplicationFixtures.A02(), expandFacilities: true, targets: new ProductionTarget("i-t", 72));

        Assert.DoesNotContain(model.Nodes, n => n.Id == "fac:f-t");
        Assert.Equal("稼働 100%", Node(model, "facunit:f-t#0").Note);
        Assert.Equal("稼働 100%", Node(model, "facunit:f-t#1").Note);
        Assert.Equal("稼働 40%", Node(model, "facunit:f-t#2").Note);

        // 入出力は占有比（1/2.4, 1/2.4, 0.4/2.4）で分割される。
        Assert.Equal(120, Edge(model, "item:i-u", "facunit:f-t#0", FlowGraphEdgeKind.RecipeInput).RatePerMinute, 6);
        Assert.Equal(120, Edge(model, "item:i-u", "facunit:f-t#1", FlowGraphEdgeKind.RecipeInput).RatePerMinute, 6);
        Assert.Equal(48, Edge(model, "item:i-u", "facunit:f-t#2", FlowGraphEdgeKind.RecipeInput).RatePerMinute, 6);
        Assert.Equal(30, Edge(model, "facunit:f-t#0", "item:i-t", FlowGraphEdgeKind.RecipeOutput).RatePerMinute, 6);
        Assert.Equal(12, Edge(model, "facunit:f-t#2", "item:i-t", FlowGraphEdgeKind.RecipeOutput).RatePerMinute, 6);

        // ユニット入力が容量超過ならユニットノードにもフラグが立つ。
        Assert.True(Node(model, "facunit:f-t#0").OverCapacity);
        Assert.True(Edge(model, "item:i-u", "facunit:f-t#0", FlowGraphEdgeKind.RecipeInput).OverCapacity);
    }

    // FG-18: 固定消費は台数分表示で全ユニットへ等量に分ける（仕様決定 AO）。
    [Fact]
    public void ExpandFacilitiesSplitsFixedConsumption()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-in", "原料", gatherable: true),
             ApplicationFixtures.Item("i-pow", "動力素材", gatherable: true),
             ApplicationFixtures.Item("i-out", "製品")],
            [ApplicationFixtures.Facility("f-fc", "化学機", 10)],
            [], [],
            [ApplicationFixtures.Recipe("r-fc", "製品", [("i-in", 1)], [("i-out", 1)],
                [ApplicationFixtures.Pair("f-fc", 30,
                    fc: new FixedConsumption { ItemId = "i-pow", RatePerMinute = 12 })])]);

        // i-out 2.5/分 → 切上げ 2 台。ユニットあたり 12/分（合計 24/分で不変）。
        (_, FlowGraphModel model) = Build(
            snapshot, expandFacilities: true, targets: new ProductionTarget("i-out", 2.5));

        Assert.Equal(12, Edge(model, "item:i-pow", "facunit:f-fc#0", FlowGraphEdgeKind.FixedConsumption).RatePerMinute, 6);
        Assert.Equal(12, Edge(model, "item:i-pow", "facunit:f-fc#1", FlowGraphEdgeKind.FixedConsumption).RatePerMinute, 6);
    }

    // FG-19: 散布機は環境ごとの専用ユニットになる（仕様決定 AO）。
    [Fact]
    public void ExpandFacilitiesEmitsDispenserUnits()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
             ApplicationFixtures.Item("i-gas", "活性ガス", TransportKind.Pipe, gatherable: true),
             ApplicationFixtures.Item("i-x", "製品X"), ApplicationFixtures.Item("i-y", "製品Y")],
            [ApplicationFixtures.Facility("f-a", "機A", 10),
             ApplicationFixtures.Facility("f-b", "機B", 10),
             ApplicationFixtures.Facility("f-disp", "散布機", 5)],
            [ApplicationFixtures.Env("env-g", "ガス環境", "f-disp", "i-gas", 360)],
            [],
            [
                ApplicationFixtures.Recipe("r-x", "製品X", [("i-u", 1)], [("i-x", 1)],
                    [ApplicationFixtures.Pair("f-a", 6, "env-g")]),
                ApplicationFixtures.Recipe("r-y", "製品Y", [("i-u", 1)], [("i-y", 1)],
                    [ApplicationFixtures.Pair("f-b", 6, "env-g")]),
            ]);

        // 環境を要するレシピ 2 つ → 散布機 2 台 → 消費合計 720/分 を 360 ずつ分ける。
        (_, FlowGraphModel model) = Build(
            snapshot, expandFacilities: true,
            targets: [new ProductionTarget("i-x", 10), new ProductionTarget("i-y", 10)]);

        Assert.Equal("散布機", Node(model, "facunit:f-disp#0").Note);
        Assert.Equal("散布機", Node(model, "facunit:f-disp#1").Note);
        Assert.Equal(360, Edge(model, "item:i-gas", "facunit:f-disp#0", FlowGraphEdgeKind.EnvironmentConsume).RatePerMinute, 6);
        Assert.Equal(360, Edge(model, "item:i-gas", "facunit:f-disp#1", FlowGraphEdgeKind.EnvironmentConsume).RatePerMinute, 6);
        Assert.DoesNotContain(model.Nodes, n => n.Id == "fac:f-disp");
    }

    // FG-20: レーン増設（台数分割）で解消できる集計超過は対象外。両表示で判定が一致する（仕様決定 AN・AO）。
    [Fact]
    public void LaneSolvableOverflowIsNotFlagged()
    {
        // ユニットあたり 20/分（3 台で合計 60/分）の入力。集計はベルト容量を超えるがユニットでは超えない。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-src", "素材", gatherable: true),
             ApplicationFixtures.Item("i-o", "製品")],
            [ApplicationFixtures.Facility("f-x", "加工機", 10)],
            [], [],
            [ApplicationFixtures.Recipe("r-x", "製品", [("i-src", 1)], [("i-o", 1)],
                [ApplicationFixtures.Pair("f-x", 3)])]);

        var targets = new[] { new ProductionTarget("i-o", 60) };
        (_, FlowGraphModel collapsed) = Build(snapshot, targets: targets);
        (_, FlowGraphModel expanded) = Build(snapshot, expandFacilities: true, targets: targets);

        Assert.Equal(60, Edge(collapsed, "item:i-src", "fac:f-x", FlowGraphEdgeKind.RecipeInput).RatePerMinute, 6);
        Assert.False(Edge(collapsed, "item:i-src", "fac:f-x", FlowGraphEdgeKind.RecipeInput).OverCapacity);
        Assert.False(Node(collapsed, "item:i-src").OverCapacity);
        Assert.False(Node(collapsed, "fac:f-x").OverCapacity);

        foreach (int i in new[] { 0, 1, 2 })
        {
            FlowGraphEdge unitEdge = Edge(
                expanded, "item:i-src", $"facunit:f-x#{i}", FlowGraphEdgeKind.RecipeInput);
            Assert.Equal(20, unitEdge.RatePerMinute, 6);
            Assert.False(unitEdge.OverCapacity);
            Assert.False(Node(expanded, $"facunit:f-x#{i}").OverCapacity);
        }
    }

    // FG-21: 切上げ 1 台の設備は展開しない（仕様決定 AO）。
    [Fact]
    public void SingleUnitFacilityIsNotExpanded()
    {
        // i-t 10/分 → 実数 1/3 台 → 切上げ 1 台。
        (_, FlowGraphModel model) = Build(
            ApplicationFixtures.A02(), expandFacilities: true, targets: new ProductionTarget("i-t", 10));

        Assert.Equal("×1", Node(model, "fac:f-t").Note);
        Assert.DoesNotContain(model.Nodes, n => n.Id.Contains('#'));
    }

    // FG-22: 設備 ID に '#' が含まれてもユニットノードと別設備ノードが衝突しない（仕様決定 AO）。
    [Fact]
    public void HashInFacilityIdDoesNotCollide()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
             ApplicationFixtures.Item("i-x", "製品X"), ApplicationFixtures.Item("i-y", "製品Y")],
            [ApplicationFixtures.Facility("m", "機M", 10), ApplicationFixtures.Facility("m#1", "機Mサブ", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-x", "製品X", [("i-u", 1)], [("i-x", 1)],
                    [ApplicationFixtures.Pair("m", 6)]),
                ApplicationFixtures.Recipe("r-y", "製品Y", [("i-u", 1)], [("i-y", 1)],
                    [ApplicationFixtures.Pair("m#1", 6)]),
            ]);

        // m は 2 台（i-x 20/分）、m#1 は 1 台（i-y 10/分）。旧実装では fac:m#1 が衝突した。
        (_, FlowGraphModel expanded) = Build(
            snapshot, expandFacilities: true,
            targets: [new ProductionTarget("i-x", 20), new ProductionTarget("i-y", 10)]);

        Assert.Equal("機Mサブ", Node(expanded, "fac:m#1").Label);
        Node(expanded, "facunit:m#0");
        Node(expanded, "facunit:m#1");
        Edge(expanded, "item:i-u", "facunit:m#1", FlowGraphEdgeKind.RecipeInput);
        Edge(expanded, "item:i-u", "fac:m#1", FlowGraphEdgeKind.RecipeInput);

        (_, FlowGraphModel collapsed) = Build(
            snapshot, targets: [new ProductionTarget("i-x", 20), new ProductionTarget("i-y", 10)]);

        Assert.Equal("機M", Node(collapsed, "fac:m").Label);
        Assert.Equal("機Mサブ", Node(collapsed, "fac:m#1").Label);
        Assert.Equal(20, Edge(collapsed, "item:i-u", "fac:m", FlowGraphEdgeKind.RecipeInput).RatePerMinute, 6);
        Assert.Equal(10, Edge(collapsed, "item:i-u", "fac:m#1", FlowGraphEdgeKind.RecipeInput).RatePerMinute, 6);
    }

    // FG-23: 未調整ビューでは実機械の全速稼働流量を占有ユニットへ等量に分ける（AN・AO）。
    [Fact]
    public void UnadjustedViewSplitsFullMachineRateEvenly()
    {
        // i-out 27.5/分 → 実数 1.1 台（ユニット入力は実機械で 25/分、ベルト容量以下）。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-in", "原料", gatherable: true),
             ApplicationFixtures.Item("i-out", "製品")],
            [ApplicationFixtures.Facility("f-x", "加工機", 10)],
            [], [],
            [ApplicationFixtures.Recipe("r-x", "製品", [("i-in", 1)], [("i-out", 1)],
                [ApplicationFixtures.Pair("f-x", 2.4)])]);

        (_, FlowGraphModel model) = Build(
            snapshot, unadjusted: true, expandFacilities: true,
            targets: new ProductionTarget("i-out", 27.5));

        // 切上げ 2 台が全速稼働するのでユニットあたり 25/分（合計 50/分）。比率保持だと 45.45/分で誤って赤化する。
        Assert.Equal(25, Edge(model, "item:i-in", "facunit:f-x#0", FlowGraphEdgeKind.RecipeInput).RatePerMinute, 6);
        Assert.Equal(25, Edge(model, "item:i-in", "facunit:f-x#1", FlowGraphEdgeKind.RecipeInput).RatePerMinute, 6);
        Assert.False(Node(model, "facunit:f-x#0").OverCapacity);
        Assert.False(Node(model, "facunit:f-x#1").OverCapacity);
    }

    // FG-25: 未調整ビューではスケーリング後の機械数でユニットを再割当する（AN・AO）。
    [Fact]
    public void UnadjustedViewReallocatesScaledMachines()
    {
        // 0.9 台ずつ共有する 2 レシピ（実機械あたり i-u 24/分）。スケーリング後は各 1.0 台で
        // ユニットごと 24/分。占有ユニットへの等量分配だとユニット 0 が 36/分で誤って赤化する。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
             ApplicationFixtures.Item("i-x", "製品X"), ApplicationFixtures.Item("i-y", "製品Y")],
            [ApplicationFixtures.Facility("f-s", "共用機", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-x", "製品X", [("i-u", 1)], [("i-x", 1)],
                    [ApplicationFixtures.Pair("f-s", 2.5)]),
                ApplicationFixtures.Recipe("r-y", "製品Y", [("i-u", 1)], [("i-y", 1)],
                    [ApplicationFixtures.Pair("f-s", 2.5)]),
            ]);

        (_, FlowGraphModel model) = Build(
            snapshot, unadjusted: true, expandFacilities: true,
            targets: [new ProductionTarget("i-x", 21.6), new ProductionTarget("i-y", 21.6)]);

        Assert.Equal(24, Edge(model, "item:i-u", "facunit:f-s#0", FlowGraphEdgeKind.RecipeInput).RatePerMinute, 6);
        Assert.Equal(24, Edge(model, "item:i-u", "facunit:f-s#1", FlowGraphEdgeKind.RecipeInput).RatePerMinute, 6);
        Assert.False(Edge(model, "item:i-u", "facunit:f-s#0", FlowGraphEdgeKind.RecipeInput).OverCapacity);
        Assert.False(Node(model, "facunit:f-s#0").OverCapacity);
        Assert.False(Node(model, "facunit:f-s#1").OverCapacity);
    }

    // FG-26: TransportKind=None の入力は容量判定対象外（AN）。
    [Fact]
    public void VirtualItemInputIsNotFlagged()
    {
        // 固定消費が仮想アイテム（電力相当）でも容量超過にならない。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-in", "原料", gatherable: true),
             ApplicationFixtures.Item("i-pow", "電力", TransportKind.None, gatherable: true),
             ApplicationFixtures.Item("i-out", "製品")],
            [ApplicationFixtures.Facility("f-v", "加工機", 10)],
            [], [],
            [ApplicationFixtures.Recipe("r-v", "製品", [("i-in", 1)], [("i-out", 1)],
                [ApplicationFixtures.Pair("f-v", 2,
                    fc: new FixedConsumption { ItemId = "i-pow", RatePerMinute = 100 })])]);

        (_, FlowGraphModel model) = Build(
            snapshot, targets: new ProductionTarget("i-out", 10));

        Assert.False(Edge(model, "item:i-pow", "fac:f-v", FlowGraphEdgeKind.FixedConsumption).OverCapacity);
        Assert.False(Node(model, "item:i-pow").OverCapacity);
        Assert.False(Node(model, "fac:f-v").OverCapacity);
    }

    // FG-24: 同一アイテムの複数ランの入力はユニットで合算して容量判定する（AN）。
    [Fact]
    public void SharedUnitInputsAreSummedForCapacity()
    {
        // 2 レシピが同一ユニットへ同じアイテムを各 20/分入力 → 合計 40/分がベルト容量超過。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
             ApplicationFixtures.Item("i-x", "製品X"), ApplicationFixtures.Item("i-y", "製品Y")],
            [ApplicationFixtures.Facility("f-s", "共用機", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-x", "製品X", [("i-u", 1)], [("i-x", 1)],
                    [ApplicationFixtures.Pair("f-s", 1.5)]),
                ApplicationFixtures.Recipe("r-y", "製品Y", [("i-u", 1)], [("i-y", 1)],
                    [ApplicationFixtures.Pair("f-s", 1.5)]),
            ]);

        (_, FlowGraphModel model) = Build(
            snapshot, targets: [new ProductionTarget("i-x", 20), new ProductionTarget("i-y", 20)]);

        FlowGraphEdge edge = Edge(model, "item:i-u", "fac:f-s", FlowGraphEdgeKind.RecipeInput);
        Assert.Equal(40, edge.RatePerMinute, 6);
        Assert.True(edge.OverCapacity);
        Assert.True(Node(model, "item:i-u").OverCapacity);
        Assert.True(Node(model, "fac:f-s").OverCapacity);
    }

    // FG-14: 空の計画は例外なく空モデルを返す。
    [Fact]
    public void EmptyPlanReturnsEmptyModel()
    {
        var plan = new ProductionPlan
        {
            ItemRequirements = [],
            FacilityRequirements = [],
            RecipeRuns = [],
            PairSelections = [],
            EnvironmentRequirements = [],
            TotalPowerConsumption = 0,
            Surpluses = [],
            FlowAdjustments = [],
            Warnings = [],
        };
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot([], [], [], [], []);

        FlowGraphModel model = FlowGraphModelBuilder.Build(
            plan, snapshot, new ContextFilter(), [], false);

        Assert.Empty(model.Nodes);
        Assert.Empty(model.Edges);
        Assert.Equal(0, model.MaxRatePerMinute);
    }
}
