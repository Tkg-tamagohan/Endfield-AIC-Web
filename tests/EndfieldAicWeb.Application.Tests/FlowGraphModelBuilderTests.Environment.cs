using EndfieldAicWeb.Application.Graph;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

public partial class FlowGraphModelBuilderTests
{
    // FG-38〜44・48・49: 環境供給設備の層割りと隣接（test-specification-phase25.md、仕様決定 BM）。

    // FG-38: 環境供給設備は利用設備の最小層に置かれ、消費アイテムはその直上流に来る
    // （従来 FG-31 の改訂、仕様決定 BM）。
    [Fact]
    public void DispenserSitsAtUsingFacilityLayer()
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

        // 散布機は利用設備 f-a と同じ層。ガスはその直上流（rank−1）で、散布機より上流側に来る。
        FlowGraphNode dispenser = Node(model, "fac:f-disp");
        Assert.Equal(Node(model, "fac:f-a").Rank, dispenser.Rank);
        Assert.Equal(dispenser.Rank - 1, Node(model, "item:i-gas").Rank);
    }

    // FG-39: 複数の利用設備では最小層に置く（仕様決定 BM）。
    [Fact]
    public void DispenserUsesShallowestUsingFacilityLayer()
    {
        // env-g は浅い経路の f-y（製品を直接産出）と深い経路の f-m（中間品を産出）の
        // 両方に要求される。散布機は浅い側（層番号が小さい側）の利用設備と同じ層になる。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
             ApplicationFixtures.Item("i-gas", "活性ガス", TransportKind.Pipe, gatherable: true),
             ApplicationFixtures.Item("i-m", "中間品"),
             ApplicationFixtures.Item("i-x", "製品X"),
             ApplicationFixtures.Item("i-y", "製品Y")],
            [ApplicationFixtures.Facility("f-m", "機M", 10),
             ApplicationFixtures.Facility("f-x", "機X", 10),
             ApplicationFixtures.Facility("f-y", "機Y", 10),
             ApplicationFixtures.Facility("f-disp", "散布機", 5)],
            [ApplicationFixtures.Env("env-g", "ガス環境", "f-disp", "i-gas", 360)],
            [],
            [
                ApplicationFixtures.Recipe("r-m", "中間品", [("i-u", 1)], [("i-m", 1)],
                    [ApplicationFixtures.Pair("f-m", 6, "env-g")]),
                ApplicationFixtures.Recipe("r-x", "製品X", [("i-m", 1)], [("i-x", 1)],
                    [ApplicationFixtures.Pair("f-x", 6)]),
                ApplicationFixtures.Recipe("r-y", "製品Y", [("i-u", 1)], [("i-y", 1)],
                    [ApplicationFixtures.Pair("f-y", 6, "env-g")]),
            ]);

        (_, FlowGraphModel model) = Build(
            snapshot, targets: [new ProductionTarget("i-x", 10), new ProductionTarget("i-y", 10)]);

        // f-y（製品を直接産出、層 1）側に寄り、f-m（層 3）側ではない。
        FlowGraphNode dispenser = Node(model, "fac:f-disp");
        Assert.Equal(Node(model, "fac:f-y").Rank, dispenser.Rank);
        Assert.True(dispenser.Rank > Node(model, "fac:f-m").Rank);
        Assert.Equal(dispenser.Rank - 1, Node(model, "item:i-gas").Rank);
    }

    // FG-40: 1 台の供給設備が複数環境を担うとき、全環境の利用設備を合わせた最小層に置く（BM）。
    [Fact]
    public void MultiEnvironmentProviderUsesMinLayerAcrossEnvironments()
    {
        // f-disp は env-g（浅い経路の f-a が要求）と env-h（深い経路の f-mid が要求）を
        // 両方供給する。利用設備を合わせた最小層は f-a 側の層。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
             ApplicationFixtures.Item("i-gas", "活性ガス", TransportKind.Pipe, gatherable: true),
             ApplicationFixtures.Item("i-heat", "熱媒", TransportKind.Pipe, gatherable: true),
             ApplicationFixtures.Item("i-m", "中間品"),
             ApplicationFixtures.Item("i-x", "製品X"),
             ApplicationFixtures.Item("i-y", "製品Y")],
            [ApplicationFixtures.Facility("f-a", "機A", 10),
             ApplicationFixtures.Facility("f-mid", "機MID", 10),
             ApplicationFixtures.Facility("f-b", "機B", 10),
             ApplicationFixtures.Facility("f-disp", "散布機", 5)],
            [
                ApplicationFixtures.Env("env-g", "ガス環境", "f-disp", "i-gas", 360),
                ApplicationFixtures.Env("env-h", "熱環境", "f-disp", "i-heat", 120),
            ],
            [],
            [
                ApplicationFixtures.Recipe("r-x", "製品X", [("i-u", 1)], [("i-x", 1)],
                    [ApplicationFixtures.Pair("f-a", 6, "env-g")]),
                ApplicationFixtures.Recipe("r-m", "中間品", [("i-u", 1)], [("i-m", 1)],
                    [ApplicationFixtures.Pair("f-mid", 6, "env-h")]),
                ApplicationFixtures.Recipe("r-y", "製品Y", [("i-m", 1)], [("i-y", 1)],
                    [ApplicationFixtures.Pair("f-b", 6)]),
            ]);

        (_, FlowGraphModel model) = Build(
            snapshot, targets: [new ProductionTarget("i-x", 10), new ProductionTarget("i-y", 10)]);

        FlowGraphNode dispenser = Node(model, "fac:f-disp");
        Assert.Equal(Node(model, "fac:f-a").Rank, dispenser.Rank);
        Assert.True(dispenser.Rank > Node(model, "fac:f-mid").Rank);
        Edge(model, "item:i-gas", "fac:f-disp", FlowGraphEdgeKind.EnvironmentConsume);
        Edge(model, "item:i-heat", "fac:f-disp", FlowGraphEdgeKind.EnvironmentConsume);
    }

    // FG-41: 台数分表示では散布機ユニットは自分の環境を要求するランを占有するユニットの
    // 最小層に置く（仕様決定 BM・AO）。
    [Fact]
    public void ExpandedDispenserUnitsUseTheirOwnEnvironmentLayer()
    {
        // f-disp の 2 ユニットは env-g（f-a のランが要求、浅い層）と env-h（f-mid のランが要求、
        // 深い層）を 1 台ずつ担当する。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
             ApplicationFixtures.Item("i-gas", "活性ガス", TransportKind.Pipe, gatherable: true),
             ApplicationFixtures.Item("i-heat", "熱媒", TransportKind.Pipe, gatherable: true),
             ApplicationFixtures.Item("i-m", "中間品"),
             ApplicationFixtures.Item("i-x", "製品X"),
             ApplicationFixtures.Item("i-y", "製品Y")],
            [ApplicationFixtures.Facility("f-a", "機A", 10),
             ApplicationFixtures.Facility("f-mid", "機MID", 10),
             ApplicationFixtures.Facility("f-b", "機B", 10),
             ApplicationFixtures.Facility("f-disp", "散布機", 5)],
            [
                ApplicationFixtures.Env("env-g", "ガス環境", "f-disp", "i-gas", 360),
                ApplicationFixtures.Env("env-h", "熱環境", "f-disp", "i-heat", 120),
            ],
            [],
            [
                ApplicationFixtures.Recipe("r-x", "製品X", [("i-u", 1)], [("i-x", 1)],
                    [ApplicationFixtures.Pair("f-a", 6, "env-g")]),
                ApplicationFixtures.Recipe("r-m", "中間品", [("i-u", 1)], [("i-m", 1)],
                    [ApplicationFixtures.Pair("f-mid", 6, "env-h")]),
                ApplicationFixtures.Recipe("r-y", "製品Y", [("i-m", 1)], [("i-y", 1)],
                    [ApplicationFixtures.Pair("f-b", 6)]),
            ]);

        // i-x 20/分 → f-a は 2 ユニットを占有する。env-h は f-mid（1 ユニット=集約ノード）。
        (ProductionPlan plan, FlowGraphModel model) = Build(
            snapshot, expandFacilities: true,
            targets: [new ProductionTarget("i-x", 20), new ProductionTarget("i-y", 10)]);

        // ユニット→担当環境の対応は FacilityUnitLayout と同じ割当で引く。
        Dictionary<string, List<FacilityUnitSlot>> units = FacilityUnitLayout.Allocate(
            plan.RecipeRuns, plan.FacilityRequirements, plan.EnvironmentRequirements,
            snapshot, plan.PairSelections);
        var dispenserEnvByIndex = units["f-disp"]
            .Where(u => u.IsDispenser)
            .ToDictionary(u => u.Index, u => u.DispenserEnvironmentId);
        Assert.Equal(2, dispenserEnvByIndex.Count);

        int usingRankFor(string envId) => envId switch
        {
            "env-g" => Node(model, "facunit:f-a#0").Rank,
            "env-h" => Node(model, "fac:f-mid").Rank,
            _ => throw new InvalidOperationException(envId),
        };

        foreach ((int index, string? envId) in dispenserEnvByIndex)
        {
            Assert.Equal(usingRankFor(envId!), Node(model, $"facunit:f-disp#{index}").Rank);
        }
    }

    // FG-42: 利用設備が 0 件の環境は従来規則へ退避し、最深消費アイテムの直下流に置く
    // （防御的経路。計算機では散布機台数が稼働中ランの環境にのみ計上されるため到達しない）。
    [Fact]
    public void EnvFacilityWithoutUsersFallsBackToConsumedItemDownstream()
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
                    [ApplicationFixtures.Pair("f-a", 6)]),
            ]);
        // env-g を要求するランがない手組みの計画（r-x のペアは環境なし）。
        var plan = new ProductionPlan
        {
            ItemRequirements =
            [
                new ItemRequirement("i-x", 10, [new SupplyPortion(SupplyKind.Recipe, "r-x", 10)], 0),
                new ItemRequirement("i-u", 10, [new SupplyPortion(SupplyKind.Gathered, null, 10)], 0),
            ],
            FacilityRequirements =
            [
                new FacilityRequirement("f-a", 1, 1),
                new FacilityRequirement("f-disp", 1, 1),
            ],
            RecipeRuns = [new RecipeRun("r-x", "f-a", 10)],
            PairSelections =
            [
                new PairSelection("i-x", "r-x", ApplicationFixtures.Pair("f-a", 6, recipeId: "r-x")),
            ],
            EnvironmentRequirements =
            [
                new EnvironmentRequirement("env-g", "f-disp", 1, "i-gas", 360, 1, 1.0),
            ],
            TotalPowerConsumption = 0,
            Surpluses = [],
            FlowAdjustments = [],
            Warnings = [],
        };

        FlowGraphModel model = FlowGraphModelBuilder.Build(
            plan, snapshot, new ContextFilter(), [new ProductionTarget("i-x", 10)], false);

        // i-gas の唯一の消費先は散布機で、ガスは消費されない出口側（目標と同じ Layer0）に留まる。
        // 散布機はガスの直下流（最右列のさらに右）に置かれる。
        Assert.Equal(Node(model, "item:i-x").Rank, Node(model, "item:i-gas").Rank);
        Assert.Equal(Node(model, "item:i-gas").Rank + 1, Node(model, "fac:f-disp").Rank);
    }

    // FG-43: ランも回す兼用設備は出力を持つため本規則の対象外で、従来どおりの層割り（BM）。
    [Fact]
    public void DualUseFacilityFollowsNormalLayering()
    {
        // f-mix は製品を産出するランを回しつつ env-g の供給設備でもある。
        // 出力を持つため終端ノードではなく、環境消費エッジも通常の下流エッジとして層割りに効く。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
             ApplicationFixtures.Item("i-gas", "活性ガス", TransportKind.Pipe, gatherable: true),
             ApplicationFixtures.Item("i-x", "製品X")],
            [ApplicationFixtures.Facility("f-mix", "兼用機", 10)],
            [ApplicationFixtures.Env("env-g", "ガス環境", "f-mix", "i-gas", 360)],
            [],
            [
                ApplicationFixtures.Recipe("r-x", "製品X", [("i-u", 1)], [("i-x", 1)],
                    [ApplicationFixtures.Pair("f-mix", 6, "env-g")]),
            ]);

        (_, FlowGraphModel model) = Build(snapshot, targets: new ProductionTarget("i-x", 10));

        // i-x（Layer0）→ f-mix（層 1）→ i-u・i-gas（層 2）の通常鎖。
        FlowGraphNode facility = Node(model, "fac:f-mix");
        Assert.Equal(Node(model, "item:i-x").Rank - 1, facility.Rank);
        Assert.Equal(facility.Rank - 1, Node(model, "item:i-gas").Rank);
    }

    // FG-44: 環境設備の消費アイテムは設備層+1 以降になり、その生産設備はさらに上流へ来る（BM）。
    [Fact]
    public void EnvConsumedItemSinksWithItsProducer()
    {
        // i-gas は採取ではなく f-gen のレシピで生産される。散布機の固定層+1 へ沈み、
        // 生産設備 f-gen はさらにその上流へ来る。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
             ApplicationFixtures.Item("i-w", "水", TransportKind.Pipe, gatherable: true),
             ApplicationFixtures.Item("i-gas", "活性ガス", TransportKind.Pipe),
             ApplicationFixtures.Item("i-x", "製品X")],
            [ApplicationFixtures.Facility("f-a", "機A", 10),
             ApplicationFixtures.Facility("f-gen", "生成機", 10),
             ApplicationFixtures.Facility("f-disp", "散布機", 5)],
            [ApplicationFixtures.Env("env-g", "ガス環境", "f-disp", "i-gas", 360)],
            [],
            [
                ApplicationFixtures.Recipe("r-x", "製品X", [("i-u", 1)], [("i-x", 1)],
                    [ApplicationFixtures.Pair("f-a", 6, "env-g")]),
                ApplicationFixtures.Recipe("r-g", "活性ガス", [("i-w", 1)], [("i-gas", 1)],
                    [ApplicationFixtures.Pair("f-gen", 6)]),
            ]);

        (_, FlowGraphModel model) = Build(snapshot, targets: new ProductionTarget("i-x", 10));

        FlowGraphNode dispenser = Node(model, "fac:f-disp");
        FlowGraphNode gas = Node(model, "item:i-gas");
        Assert.Equal(Node(model, "fac:f-a").Rank, dispenser.Rank);
        // ガスは散布機の直上流（rank−1）で、生産設備はさらにその上流。
        Assert.Equal(dispenser.Rank - 1, gas.Rank);
        Assert.Equal(gas.Rank - 1, Node(model, "fac:f-gen").Rank);
        // ガス→散布機のエッジは上流→下流の順方向を保つ。
        Assert.True(gas.Rank < dispenser.Rank);
        Edge(model, "item:i-gas", "fac:f-disp", FlowGraphEdgeKind.EnvironmentConsume);
    }

    // FG-48: 同じ層に無関係な設備が並ぶときも、散布機は利用設備の隣に置かれる
    // （仕様決定 BM の行内順規則。ガス先行と利用設備の混成平均では行内の端へ
    // 流れて無関係な設備の隣になりうるため、利用設備の順序を優先する）。
    [Fact]
    public void DispenserStaysAdjacentToUsingFacility()
    {
        // 層構造: i-x・i-z・i-w が Layer0、f-a・f-z・f-z2・散布機が同じ層（rank 1）、
        // i-b・i-gas・i-u・i-v がその直上流（rank 0、順序はアイテム Id の辞書順）。
        // 修正前は散布機のバリセンターが層をまたぐ平均値で全機械より行末へ流れ、
        // 行末の f-z2 の隣に並んで利用設備 f-a から離れた。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-b", "素材B", gatherable: true),
             ApplicationFixtures.Item("i-gas", "活性ガス", TransportKind.Pipe, gatherable: true),
             ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
             ApplicationFixtures.Item("i-v", "素材V", gatherable: true),
             ApplicationFixtures.Item("i-w", "製品W"),
             ApplicationFixtures.Item("i-x", "製品X"), ApplicationFixtures.Item("i-z", "製品Z")],
            [ApplicationFixtures.Facility("f-a", "機A", 10),
             ApplicationFixtures.Facility("f-disp", "散布機", 5),
             ApplicationFixtures.Facility("f-z", "機Z", 10),
             ApplicationFixtures.Facility("f-z2", "機Z2", 10)],
            [ApplicationFixtures.Env("env-g", "ガス環境", "f-disp", "i-gas", 360)],
            [],
            [
                ApplicationFixtures.Recipe("r-x", "製品X", [("i-u", 1)], [("i-x", 1)],
                    [ApplicationFixtures.Pair("f-a", 6, "env-g")]),
                ApplicationFixtures.Recipe("r-z", "製品Z", [("i-b", 1)], [("i-z", 1)],
                    [ApplicationFixtures.Pair("f-z", 6)]),
                ApplicationFixtures.Recipe("r-w", "製品W", [("i-v", 1)], [("i-w", 1)],
                    [ApplicationFixtures.Pair("f-z2", 6)]),
            ]);

        (_, FlowGraphModel model) = Build(
            snapshot, targets: [new ProductionTarget("i-x", 10), new ProductionTarget("i-z", 10),
                new ProductionTarget("i-w", 10)]);

        FlowGraphNode dispenser = Node(model, "fac:f-disp");
        FlowGraphNode user = Node(model, "fac:f-a");
        Assert.Equal(user.Rank, dispenser.Rank);
        Assert.Equal(1, Math.Abs(user.Order - dispenser.Order));
    }

    // FG-49: 環境利用レシピがその環境の消費アイテムを副産物として産出するときも、
    // 散布機は利用設備と同じ層を保つ（消費アイテムの沈下に利用設備が追従すると
    // 固定した供給設備と層が離れるため、自己供給の出力エッジは再伝播の対象外
    // として後退エッジに残す）。
    [Fact]
    public void SelfProducedEnvConsumeItemKeepsDispenserLayer()
    {
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-gas", "活性ガス", TransportKind.Pipe),
             ApplicationFixtures.Item("i-u", "上流素材", gatherable: true),
             ApplicationFixtures.Item("i-x", "製品X")],
            [ApplicationFixtures.Facility("f-a", "機A", 10),
             ApplicationFixtures.Facility("f-disp", "散布機", 5)],
            [ApplicationFixtures.Env("env-g", "ガス環境", "f-disp", "i-gas", 360)],
            [],
            [
                // f-a は環境ペアで動きつつ、散布機の消費ガスを副産物として産出する。
                ApplicationFixtures.Recipe("r-x", "製品X", [("i-u", 1)], [("i-x", 1), ("i-gas", 40)],
                    [ApplicationFixtures.Pair("f-a", 6, "env-g")]),
            ]);

        (_, FlowGraphModel model) = Build(snapshot, targets: new ProductionTarget("i-x", 10));

        FlowGraphNode dispenser = Node(model, "fac:f-disp");
        Assert.Equal(Node(model, "fac:f-a").Rank, dispenser.Rank);
        Assert.Equal(dispenser.Rank - 1, Node(model, "item:i-gas").Rank);
    }
}
