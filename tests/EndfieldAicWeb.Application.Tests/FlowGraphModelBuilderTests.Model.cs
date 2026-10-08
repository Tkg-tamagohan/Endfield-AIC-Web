using EndfieldAicWeb.Application.Graph;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

public partial class FlowGraphModelBuilderTests
{
    // FG-01〜07・11・14・24・45〜47: ノードとエッジの帳簿と基本モデル（test-specification-phase15.md §2・phase17.md・phase25.md）。

    // FG-01: 目標アイテムはフラグ付きノードになる。
    [Fact]
    public void TargetItemHasFlag()
    {
        (_, FlowGraphModel model) = Build(ApplicationFixtures.A02(), targets: new ProductionTarget("i-t", 72));

        Assert.True(Node(model, "item:i-t").IsTarget);
        Assert.False(Node(model, "item:i-u").IsTarget);
    }

    // FG-45: 採取供給はアイテムノードの GatheredPerMinute に保持される（従来 FG-02 の改訂、仕様決定 BO）。
    [Fact]
    public void GatheredSupplyStaysOnItemNode()
    {
        (_, FlowGraphModel model) = Build(ApplicationFixtures.A02(), targets: new ProductionTarget("i-t", 72));

        // i-u は i-t 72/分 × 入力 4 = 288/分を採取で賄う。共通の採取ノードは存在しない。
        Assert.Equal(288, Node(model, "item:i-u").GatheredPerMinute, 6);
        Assert.DoesNotContain(model.Nodes, n => n.Id == "gather");
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

    // FG-47: 採取のない計画では全ノードが Item または Facility になる（従来 FG-10 の改訂、仕様決定 BO）。
    [Fact]
    public void GatherlessPlanHasOnlyItemAndFacilityNodes()
    {
        // i-need は生産不能（採取不可・レシピなし）で、計画に採取供給は登場しない。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-need", "生産不能品")],
            [ApplicationFixtures.Facility("f-a", "機A", 10)],
            [], [], []);

        (_, FlowGraphModel model) = Build(snapshot, targets: new ProductionTarget("i-need", 5));

        Assert.All(model.Nodes, n =>
            Assert.True(n.Kind is FlowGraphNodeKind.Item or FlowGraphNodeKind.Facility));
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

    // FG-24: 同一アイテムの複数ランの入力は集約表示で 1 本のエッジに合算される。
    [Fact]
    public void SharedRunInputsAreSummedOnCollapsedEdge()
    {
        // 2 レシピが同一設備へ同じアイテムを各 20/分入力 → 集約表示の入力エッジは合計 40/分の 1 本。
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
    }

    // FG-46: 採取と生産が併存するアイテムも GatheredPerMinute > 0 になる。
    // IsGatherable でも生産で全量賄う場合は 0（仕様決定 BO）。
    [Fact]
    public void GatheredAndProducedItemKeepsGatheredAmount()
    {
        // 採取可能だが生産もできる i-g。マップ上限 30/分に対し需要 50/分 → 採取 30 + 生産 20。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
             ApplicationFixtures.Item("i-g", "採取原料", gatherable: true)],
            [ApplicationFixtures.Facility("f-a", "加工機", 10)],
            [], [],
            [ApplicationFixtures.Recipe("r-g", "採取原料", [("i-u", 1)], [("i-g", 1)],
                [ApplicationFixtures.Pair("f-a", 6)])],
            maps:
            [
                new GameMap
                {
                    Id = "m-cap", Name = "上限マップ", Description = "", VersionAdded = "1.0.0",
                    GatherRates = [new GatherRate { ItemId = "i-g", RatePerMinute = 30 }],
                },
                new GameMap
                {
                    Id = "m-none", Name = "採取なしマップ", Description = "", VersionAdded = "1.0.0",
                    GatherRates = [],
                },
            ]);

        (_, FlowGraphModel coexisting) = Build(
            snapshot, context: new ContextFilter { MapId = "m-cap" },
            targets: new ProductionTarget("i-g", 50));

        Assert.Equal(30, Node(coexisting, "item:i-g").GatheredPerMinute, 6);

        // i-g の行がないマップでは採取上限 0 → 全量をレシピで生産 → 緑化しない。
        (_, FlowGraphModel produced) = Build(
            snapshot, context: new ContextFilter { MapId = "m-none" },
            targets: new ProductionTarget("i-g", 50));

        Assert.Equal(0, Node(produced, "item:i-g").GatheredPerMinute, 6);
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
