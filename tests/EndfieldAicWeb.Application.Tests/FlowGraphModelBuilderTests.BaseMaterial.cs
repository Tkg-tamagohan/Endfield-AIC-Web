using EndfieldAicWeb.Application.Graph;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

public partial class FlowGraphModelBuilderTests
{
    // FG-68〜70: 基礎素材指定のグラフ表現（docs/phases/test-specification-phase42.md §2）。

    private static ContextFilter Specified(params string[] itemIds) =>
        new() { SpecifiedBaseItemIds = itemIds };

    /// <summary>FG-68・69 用の多段チェーン（i-top ← i-mid ← i-low ← i-ore）。</summary>
    private static MasterDataSnapshot SpecifiedChain() => ApplicationFixtures.Snapshot(
        [
            ApplicationFixtures.Item("i-ore", "鉄鉱石", gatherable: true),
            ApplicationFixtures.Item("i-low", "下位素材"),
            ApplicationFixtures.Item("i-mid", "中間素材"),
            ApplicationFixtures.Item("i-top", "製品"),
        ],
        [
            ApplicationFixtures.Facility("f-top", "組立機", 10),
            ApplicationFixtures.Facility("f-mid", "加工機", 10),
            ApplicationFixtures.Facility("f-low", "精錬機", 10),
        ],
        [], [],
        [
            ApplicationFixtures.Recipe("r-top", "製品", [("i-mid", 1)], [("i-top", 1)],
                [ApplicationFixtures.Pair("f-top", 6)]),
            ApplicationFixtures.Recipe("r-mid", "中間素材", [("i-low", 1)], [("i-mid", 1)],
                [ApplicationFixtures.Pair("f-mid", 6)]),
            ApplicationFixtures.Recipe("r-low", "下位素材", [("i-ore", 1)], [("i-low", 1)],
                [ApplicationFixtures.Pair("f-low", 4)]),
        ]);

    // FG-68: 指定アイテムのノードは指定フラグと外部調達量を持つ。
    [Fact]
    public void SpecifiedItemNodeCarriesBaseFlags()
    {
        (_, FlowGraphModel model) = Build(
            SpecifiedChain(),
            context: Specified("i-mid"),
            specifiedBaseItemIds: ["i-mid"],
            targets: new ProductionTarget("i-top", 10));

        FlowGraphNode mid = Node(model, "item:i-mid");
        Assert.True(mid.IsSpecifiedBase);
        Assert.Equal(10, mid.ExternalProcuredPerMinute, 6);
        Assert.Equal(0, mid.GatheredPerMinute, 6);
        Assert.False(Node(model, "item:i-top").IsSpecifiedBase);
    }

    // FG-69: 指定アイテムの上流の設備ノード・エッジは出ない。
    [Fact]
    public void SpecifiedOmitsUpstreamNodesAndEdges()
    {
        (_, FlowGraphModel model) = Build(
            SpecifiedChain(),
            context: Specified("i-mid"),
            specifiedBaseItemIds: ["i-mid"],
            targets: new ProductionTarget("i-top", 10));

        Assert.DoesNotContain(model.Nodes, n => n.Id == "fac:f-mid");
        Assert.DoesNotContain(model.Nodes, n => n.Id == "fac:f-low");
        Assert.DoesNotContain(model.Nodes, n => n.Id == "item:i-low");
        Assert.DoesNotContain(model.Nodes, n => n.Id == "item:i-ore");
        // 指定アイテムを産出する経路のエッジはない。
        Assert.DoesNotContain(model.Edges, e => e.ToId == "item:i-mid");
        // 目標側の経路は残る。
        Edge(model, "fac:f-top", "item:i-top", FlowGraphEdgeKind.RecipeOutput);
        Edge(model, "item:i-mid", "fac:f-top", FlowGraphEdgeKind.RecipeInput);
    }

    /// <summary>FG-70 用。r-main が i-mid を副産し、r-use が i-mid を消費する。</summary>
    private static MasterDataSnapshot SpecifiedByproduct()
    {
        Recipe main = ApplicationFixtures.Recipe(
            "r-main", "主産物", [("i-ore", 1)], [("i-main", 1), ("i-mid", 1)],
            [ApplicationFixtures.Pair("f-a", 4)]);
        // 2 番目の出力は副産物（SortOrder > 0）。
        main.Outputs[1].SortOrder = 1;
        return ApplicationFixtures.Snapshot(
            [
                ApplicationFixtures.Item("i-ore", "鉄鉱石", gatherable: true),
                ApplicationFixtures.Item("i-mid", "中間素材"),
                ApplicationFixtures.Item("i-use", "加工品"),
                ApplicationFixtures.Item("i-main", "主産物"),
            ],
            [
                ApplicationFixtures.Facility("f-a", "製造機", 10),
                ApplicationFixtures.Facility("f-c", "加工機", 10),
            ],
            [], [],
            [
                main,
                ApplicationFixtures.Recipe("r-use", "加工品", [("i-mid", 4)], [("i-use", 1)],
                    [ApplicationFixtures.Pair("f-c", 6)]),
            ]);
    }

    // FG-70: 副産物を産出する設備からのエッジは残り、ノードは指定情報を持つ。
    // i-mid 需要 40/分のうち副産物 10/分・外部調達 30/分。
    [Fact]
    public void SpecifiedByproductNodeKeepsProducerEdge()
    {
        (_, FlowGraphModel model) = Build(
            SpecifiedByproduct(),
            context: Specified("i-mid"),
            specifiedBaseItemIds: ["i-mid"],
            targets: [new ProductionTarget("i-use", 10), new ProductionTarget("i-main", 10)]);

        FlowGraphNode mid = Node(model, "item:i-mid");
        Assert.True(mid.IsSpecifiedBase);
        Assert.Equal(30, mid.ExternalProcuredPerMinute, 6);
        FlowGraphEdge edge = Edge(model, "fac:f-a", "item:i-mid", FlowGraphEdgeKind.RecipeOutput);
        Assert.Equal(10, edge.RatePerMinute, 6);
        Assert.True(edge.IsByproduct);
    }
}
