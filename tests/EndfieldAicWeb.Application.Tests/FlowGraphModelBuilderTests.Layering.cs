using EndfieldAicWeb.Application;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

public partial class FlowGraphModelBuilderTests
{
    // FG-08〜10・27〜29・32〜37: 層割り（test-specification-phase15.md §2・phase23.md、仕様決定 BF・BG・BH）。

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

        // 採取素材のアイテムノードが最左列（最深の上流）になる。
        Assert.Equal(0, Node(model, "item:i-u").Rank);
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
        // 外部投入の採取素材は最も上流側のアイテム層（i-seed と同じ層）に来る。
        Assert.Equal(Node(model, "item:i-seed").Rank, Node(model, "item:i-u").Rank);
        Assert.Equal(
            model.Nodes.Where(n => n.Kind == FlowGraphNodeKind.Item).Min(n => n.Rank),
            Node(model, "item:i-u").Rank);
    }

    // FG-36: 循環の外から循環へ供給する目標は Layer0 に置かず、消費設備の直上流に置く（BG・BH）。
    [Fact]
    public void TargetFeedingCycleSitsBeforeConsumer()
    {
        // i-t は目標だが i-a↔i-b 循環の構成要素ではなく、f-1 経由で循環へ外部供給するだけ。
        // Layer0 に固定すると i-t→f-1 の供給エッジが後退してしまう。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-t", "外部目標", gatherable: true),
             ApplicationFixtures.Item("i-a", "循環品A"), ApplicationFixtures.Item("i-b", "循環品B"),
             ApplicationFixtures.Item("i-z", "最終目標")],
            [ApplicationFixtures.Facility("f-1", "機1", 10), ApplicationFixtures.Facility("f-2", "機2", 10),
             ApplicationFixtures.Facility("f-3", "機3", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-1", "循環品A", [("i-t", 1), ("i-b", 1)], [("i-a", 1)],
                    [ApplicationFixtures.Pair("f-1", 6)]),
                ApplicationFixtures.Recipe("r-2", "循環品B", [("i-a", 1)], [("i-b", 1)],
                    [ApplicationFixtures.Pair("f-2", 6)]),
                ApplicationFixtures.Recipe("r-3", "最終目標", [("i-a", 1)], [("i-z", 1)],
                    [ApplicationFixtures.Pair("f-3", 6)]),
            ]);

        (_, FlowGraphModel model) = Build(
            snapshot, targets: [new ProductionTarget("i-t", 10), new ProductionTarget("i-z", 10)]);

        // i-t は消費設備 f-1 の直上流に置かれ、右端列は i-z 側の出口だけが占める。
        Assert.Equal(Node(model, "fac:f-1").Rank - 1, Node(model, "item:i-t").Rank);
        Assert.True(Node(model, "item:i-t").Rank < Node(model, "item:i-z").Rank);
    }

    // FG-37: 生産される目標が別の循環へ供給するとき、層の引き直し後も生産設備が直上流に来る（BH）。
    [Fact]
    public void ProducedTargetKeepsProducerUpstream()
    {
        // i-t は f-t で生産される目標で、f-1 経由で i-a↔i-b 循環へ供給する。循環は浅い出口
        //（f-3→i-z）と深い出口（f-4→i-m→f-5→i-z2）を持つ。深い出口で i-t の層が仮確定より
        // 深く引き直されても、生産設備 f-t は i-t の直上流に追従する。
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot(
            [ApplicationFixtures.Item("i-u", "原料", gatherable: true),
             ApplicationFixtures.Item("i-t", "中間目標"), ApplicationFixtures.Item("i-a", "循環品A"),
             ApplicationFixtures.Item("i-b", "循環品B"), ApplicationFixtures.Item("i-m", "中間品"),
             ApplicationFixtures.Item("i-z", "製品Z"), ApplicationFixtures.Item("i-z2", "製品Z2")],
            [ApplicationFixtures.Facility("f-t", "機T", 10), ApplicationFixtures.Facility("f-1", "機1", 10),
             ApplicationFixtures.Facility("f-2", "機2", 10), ApplicationFixtures.Facility("f-3", "機3", 10),
             ApplicationFixtures.Facility("f-4", "機4", 10), ApplicationFixtures.Facility("f-5", "機5", 10)],
            [], [],
            [
                ApplicationFixtures.Recipe("r-t", "中間目標", [("i-u", 1)], [("i-t", 1)],
                    [ApplicationFixtures.Pair("f-t", 6)]),
                ApplicationFixtures.Recipe("r-1", "循環品A", [("i-t", 1), ("i-b", 1)], [("i-a", 1)],
                    [ApplicationFixtures.Pair("f-1", 6)]),
                ApplicationFixtures.Recipe("r-2", "循環品B", [("i-a", 1)], [("i-b", 1)],
                    [ApplicationFixtures.Pair("f-2", 6)]),
                ApplicationFixtures.Recipe("r-3", "製品Z", [("i-a", 1)], [("i-z", 1)],
                    [ApplicationFixtures.Pair("f-3", 6)]),
                ApplicationFixtures.Recipe("r-4", "中間品", [("i-b", 1)], [("i-m", 1)],
                    [ApplicationFixtures.Pair("f-4", 6)]),
                ApplicationFixtures.Recipe("r-5", "製品Z2", [("i-m", 1)], [("i-z2", 1)],
                    [ApplicationFixtures.Pair("f-5", 6)]),
            ]);

        (_, FlowGraphModel model) = Build(
            snapshot,
            targets: [new ProductionTarget("i-t", 10), new ProductionTarget("i-z", 10), new ProductionTarget("i-z2", 10)]);

        // 産出エッジ f-t→i-t は順方向のまま。i-t は最長出口経路の深さ（i-z2 から 8 段）に置かれる。
        Assert.Equal(Node(model, "item:i-t").Rank - 1, Node(model, "fac:f-t").Rank);
        Assert.Equal(Node(model, "item:i-z").Rank - 8, Node(model, "item:i-t").Rank);
    }
}
