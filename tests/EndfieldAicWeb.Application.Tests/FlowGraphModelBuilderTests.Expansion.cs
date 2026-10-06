using EndfieldAicWeb.Application;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

public partial class FlowGraphModelBuilderTests
{
    // FG-12・17〜23・25・30: 台数分展開と未調整ビュー（test-specification-phase15.md §2・phase17.md・phase23.md・phase30.md、仕様決定 AO）。

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

        // 環境を要する機械数 6.0（3.0 機 × 2 ラン）→ 散布機 2 台 → 消費合計 720/分 を 360 ずつ分ける。
        // （仕様決定 BQ: 台数は稼働レシピ数でなく機械数÷CoverableMachines）
        (_, FlowGraphModel model) = Build(
            snapshot, expandFacilities: true,
            targets: [new ProductionTarget("i-x", 30), new ProductionTarget("i-y", 30)]);

        Assert.Equal("散布機", Node(model, "facunit:f-disp#0").Note);
        Assert.Equal("散布機", Node(model, "facunit:f-disp#1").Note);
        Assert.Equal(360, Edge(model, "item:i-gas", "facunit:f-disp#0", FlowGraphEdgeKind.EnvironmentConsume).RatePerMinute, 6);
        Assert.Equal(360, Edge(model, "item:i-gas", "facunit:f-disp#1", FlowGraphEdgeKind.EnvironmentConsume).RatePerMinute, 6);
        Assert.DoesNotContain(model.Nodes, n => n.Id == "fac:f-disp");
    }

    // FG-20: 台数分割で集約エッジの入力がユニットへ分かれる（仕様決定 AO）。
    [Fact]
    public void LaneSplitSharesUnitInputs()
    {
        // ユニットあたり 20/分（3 台で合計 60/分）の入力。
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

        foreach (int i in new[] { 0, 1, 2 })
        {
            FlowGraphEdge unitEdge = Edge(
                expanded, "item:i-src", $"facunit:f-x#{i}", FlowGraphEdgeKind.RecipeInput);
            Assert.Equal(20, unitEdge.RatePerMinute, 6);
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

    // FG-23: 未調整ビューでは実機械の全速稼働流量を占有ユニットへ等量に分ける（AO）。
    [Fact]
    public void UnadjustedViewSplitsFullMachineRateEvenly()
    {
        // i-out 27.5/分 → 実数 1.1 台。
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

        // 切上げ 2 台が全速稼働するのでユニットあたり 25/分（合計 50/分）。
        Assert.Equal(25, Edge(model, "item:i-in", "facunit:f-x#0", FlowGraphEdgeKind.RecipeInput).RatePerMinute, 6);
        Assert.Equal(25, Edge(model, "item:i-in", "facunit:f-x#1", FlowGraphEdgeKind.RecipeInput).RatePerMinute, 6);
    }

    // FG-25: 未調整ビューではスケーリング後の機械数でユニットを再割当する（AO）。
    [Fact]
    public void UnadjustedViewReallocatesScaledMachines()
    {
        // 0.9 台ずつ共有する 2 レシピ（実機械あたり i-u 24/分）。スケーリング後は各 1.0 台で
        // ユニットごと 24/分。
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
    }
}
