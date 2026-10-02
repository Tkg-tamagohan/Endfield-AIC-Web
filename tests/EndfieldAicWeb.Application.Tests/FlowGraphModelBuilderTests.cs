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
        params ProductionTarget[] targets)
    {
        ProductionPlan plan = ProductionCalculator.Calculate(
            snapshot, targets, new ContextFilter(), [], [], []);
        return (plan, FlowGraphModelBuilder.Build(
            plan, snapshot, new ContextFilter(), targets, unadjusted));
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

    // FG-08: 層割りは最長パスで行う。
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
    }

    // FG-09: 循環経路を含む計画でも停止する（後退エッジを無視）。
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
    }

    // FG-10: 未充足のみのアイテムは起点側（rank 0）に置かれる。
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
        Assert.Equal(0, node.Rank);
        Assert.DoesNotContain(model.Nodes, n => n.Kind == FlowGraphNodeKind.Gather);
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

    // FG-13: 要求流量が輸送容量を超えるアイテムにフラグと流入エッジの赤化を付ける。
    [Fact]
    public void OverCapacityIsFlagged()
    {
        // i-t 2000/分 = 33.3/s > ベルト 30/s。
        (_, FlowGraphModel model) = Build(ApplicationFixtures.A02(), targets: new ProductionTarget("i-t", 2000));

        Assert.True(Node(model, "item:i-t").OverCapacity);
        Assert.True(Edge(model, "fac:f-t", "item:i-t", FlowGraphEdgeKind.RecipeOutput).OverCapacity);
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
