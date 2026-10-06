using EndfieldAicWeb.Application;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

public partial class FlowGraphModelBuilderTests
{
    // FG-50〜54: 処理ランのグラフ表現（docs/phases/test-specification-phase32.md §4、仕様決定 CD）。

    /// <summary>D-01 相当: i-sew 余剰 30/分を処理レシピ r-disp（f-trt 4 秒）が処理する最小構成。</summary>
    private static MasterDataSnapshot D01() => ApplicationFixtures.Snapshot(
        [
            ApplicationFixtures.Item("i-ore", "鉄鉱石", gatherable: true),
            ApplicationFixtures.Item("i-p", "製品"), ApplicationFixtures.Item("i-sew", "汚水"),
        ],
        [
            ApplicationFixtures.Facility("f-asm", "加工機", 10),
            ApplicationFixtures.Facility("f-trt", "水処理設備", 10),
        ],
        [], [],
        [
            ApplicationFixtures.Recipe("r-m", "製造", [("i-ore", 1)], [("i-p", 1), ("i-sew", 1)],
                [ApplicationFixtures.Pair("f-asm", 4)]),
            ApplicationFixtures.Recipe("r-disp", "汚水処理", [("i-sew", 1)], [],
                [ApplicationFixtures.Pair("f-trt", 4)]),
        ]);

    // FG-50: 処理ランのみを持つ設備はノードに出さず、アイテムノードへ処理情報を持たせる。
    [Fact]
    public void DisposalOnlyFacilityHasNoNode()
    {
        (_, FlowGraphModel model) = Build(D01(), targets: new ProductionTarget("i-p", 30));

        Assert.DoesNotContain(model.Nodes, n => n.Id == "fac:f-trt");
        FlowGraphNode sew = Node(model, "item:i-sew");
        FlowGraphDisposal disposal = Assert.Single(sew.Disposals);
        Assert.Equal("f-trt", disposal.FacilityId);
    }

    // FG-51: 処理情報は設備名×台数と処理量。生産上の消費と併存できる。
    [Fact]
    public void DisposalInfoOnItemNode()
    {
        (_, FlowGraphModel model) = Build(D01(), targets: new ProductionTarget("i-p", 30));

        // 処理 30 サイクル × 4 秒 = 実数 2.0 台 → 台数 2。
        FlowGraphDisposal disposal = Assert.Single(Node(model, "item:i-sew").Disposals);
        Assert.Equal("水処理設備", disposal.FacilityName);
        Assert.Equal(2, disposal.Count);
        Assert.Equal(30, disposal.PerMinute, 6);
    }

    // DSP-27: 処理ランと再利用消費は同じアイテムノードに同居する。
    [Fact]
    public void DisposalCoexistsWithProductionConsumption()
    {
        // 再利用 20/分 + 処理 10/分が同じアイテムノードに同居する。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [
                ApplicationFixtures.Item("i-ore", "鉄鉱石", gatherable: true),
                ApplicationFixtures.Item("i-p", "製品"), ApplicationFixtures.Item("i-sew", "汚水"),
                ApplicationFixtures.Item("i-use", "再生品"),
            ],
            [
                ApplicationFixtures.Facility("f-m", "加工機", 10),
                ApplicationFixtures.Facility("f-u", "再生機", 10),
                ApplicationFixtures.Facility("f-trt", "水処理設備", 10),
            ],
            [], [],
            [
                ApplicationFixtures.Recipe("r-m", "製造", [("i-ore", 1)], [("i-p", 1), ("i-sew", 1)],
                    [ApplicationFixtures.Pair("f-m", 4)]),
                ApplicationFixtures.Recipe("r-use", "再生", [("i-sew", 1)], [("i-use", 1)],
                    [ApplicationFixtures.Pair("f-u", 6)]),
                ApplicationFixtures.Recipe("r-disp", "汚水処理", [("i-sew", 1)], [],
                    [ApplicationFixtures.Pair("f-trt", 4)]),
            ]);

        (_, FlowGraphModel model) = Build(
            snapshot, targets: [new ProductionTarget("i-p", 30), new ProductionTarget("i-use", 20)]);

        FlowGraphNode sew = Node(model, "item:i-sew");
        Assert.Equal(30, sew.RequiredPerMinute, 6);
        Assert.Equal(10, Assert.Single(sew.Disposals).PerMinute, 6);
        // 再利用側のエッジは従来どおり出る。
        Edge(model, "item:i-sew", "fac:f-u", FlowGraphEdgeKind.RecipeInput);
    }

    // FG-52: 処理ランへのエッジ（入力・固定消費）は生成されない。
    [Fact]
    public void NoEdgesToDisposalRun()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [
                ApplicationFixtures.Item("i-ore", "鉄鉱石", gatherable: true),
                ApplicationFixtures.Item("i-fuel", "燃料", gatherable: true),
                ApplicationFixtures.Item("i-p", "製品"), ApplicationFixtures.Item("i-sew", "汚水"),
            ],
            [
                ApplicationFixtures.Facility("f-asm", "加工機", 10),
                ApplicationFixtures.Facility("f-trt", "水処理設備", 10),
            ],
            [], [],
            [
                ApplicationFixtures.Recipe("r-m", "製造", [("i-ore", 1)], [("i-p", 1), ("i-sew", 1)],
                    [ApplicationFixtures.Pair("f-asm", 4)]),
                ApplicationFixtures.Recipe("r-disp", "汚水処理", [("i-sew", 1)], [],
                    [ApplicationFixtures.Pair("f-trt", 4, fc: new FixedConsumption
                    {
                        ItemId = "i-fuel", RatePerMinute = 6,
                    })]),
            ]);

        (_, FlowGraphModel model) = Build(snapshot, targets: new ProductionTarget("i-p", 30));

        Assert.DoesNotContain(model.Edges,
            e => e.ToId == "fac:f-trt" || e.FromId == "fac:f-trt");
    }

    // FG-53: 処理ランと生産ランを兼ねる設備はノードを持ち、台数は設備の合計切上げ。
    [Fact]
    public void SharedFacilityNodeKeepsDisposalMeta()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [
                ApplicationFixtures.Item("i-ore", "鉄鉱石", gatherable: true),
                ApplicationFixtures.Item("i-p", "製品"), ApplicationFixtures.Item("i-sew", "汚水"),
            ],
            [ApplicationFixtures.Facility("f-asm", "加工機", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-m", "製造", [("i-ore", 1)], [("i-p", 1), ("i-sew", 1)],
                    [ApplicationFixtures.Pair("f-asm", 4)]),
                ApplicationFixtures.Recipe("r-disp", "汚水処理", [("i-sew", 1)], [],
                    [ApplicationFixtures.Pair("f-asm", 4)]),
            ]);

        (_, FlowGraphModel model) = Build(snapshot, targets: new ProductionTarget("i-p", 30));

        Assert.Single(model.Nodes, n => n.Id == "fac:f-asm");
        // 生産 2.0 台 + 処理 2.0 台 = 実数 4.0 台 → 台数 4。
        Assert.Equal(4, Assert.Single(Node(model, "item:i-sew").Disposals).Count);
    }

    // FG-54: 台数分表示でも処理ランの占有機械はノードに出ない。
    [Fact]
    public void ExpandedUnitsExcludeDisposalRuns()
    {
        (_, FlowGraphModel model) = Build(
            D01(), expandFacilities: true, targets: new ProductionTarget("i-p", 30));

        Assert.DoesNotContain(model.Nodes, n => n.Id.StartsWith("facunit:f-trt", StringComparison.Ordinal));
    }

    // DSP-28: 兼用設備の台数分表示は占有スロットのユニットのみを出し、処理ランはユニット割当対象外。
    [Fact]
    public void ExpandedUnitsShowOnlyOccupiedSlotsOnSharedFacility()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [
                ApplicationFixtures.Item("i-ore", "鉄鉱石", gatherable: true),
                ApplicationFixtures.Item("i-p", "製品"), ApplicationFixtures.Item("i-sew", "汚水"),
            ],
            [ApplicationFixtures.Facility("f-asm", "加工機", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-m", "製造", [("i-ore", 1)], [("i-p", 1), ("i-sew", 1)],
                    [ApplicationFixtures.Pair("f-asm", 4)]),
                ApplicationFixtures.Recipe("r-disp", "汚水処理", [("i-sew", 1)], [],
                    [ApplicationFixtures.Pair("f-asm", 4)]),
            ]);

        (_, FlowGraphModel model) = Build(
            snapshot, expandFacilities: true, targets: new ProductionTarget("i-p", 30));

        // f-asm は実数 4.0 台（生産 2.0 + 処理 2.0）→ スロット 4 機だが、
        // 処理ランはユニット割当対象外のため占有 2 機分だけが出る。
        Assert.Single(model.Nodes, n => n.Id == "facunit:f-asm#0");
        Assert.Single(model.Nodes, n => n.Id == "facunit:f-asm#1");
        Assert.DoesNotContain(model.Nodes,
            n => n.Id.StartsWith("facunit:f-asm#", StringComparison.Ordinal)
                && n.Id != "facunit:f-asm#0" && n.Id != "facunit:f-asm#1");
    }

    // FG-55: 未調整ビューで表示量が 0 の処理はアイテムを紫化しない（Devin Review 対応の回帰）。
    [Fact]
    public void ZeroRateDisposalDoesNotColorItem()
    {
        // 補助入力 i-aux は生産不能（レシピ不在・採取不可）のため未調整でも利用可能量 0。
        // 表示量 0 の処理エントリはノードに残さない。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [
                ApplicationFixtures.Item("i-ore", "鉄鉱石", gatherable: true),
                ApplicationFixtures.Item("i-p", "製品"), ApplicationFixtures.Item("i-sew", "汚水"),
                ApplicationFixtures.Item("i-aux", "触媒"),
            ],
            [
                ApplicationFixtures.Facility("f-asm", "加工機", 10),
                ApplicationFixtures.Facility("f-trt", "水処理設備", 10),
            ],
            [], [],
            [
                ApplicationFixtures.Recipe("r-m", "製造", [("i-ore", 1)], [("i-p", 1), ("i-sew", 1)],
                    [ApplicationFixtures.Pair("f-asm", 4)]),
                ApplicationFixtures.Recipe("r-disp", "汚水処理", [("i-sew", 1), ("i-aux", 1)], [],
                    [ApplicationFixtures.Pair("f-trt", 4)]),
            ]);

        (_, FlowGraphModel model) = Build(
            snapshot, unadjusted: true, targets: new ProductionTarget("i-p", 30));

        Assert.Empty(Node(model, "item:i-aux").Disposals);
        Assert.Single(Node(model, "item:i-sew").Disposals);
    }
}
