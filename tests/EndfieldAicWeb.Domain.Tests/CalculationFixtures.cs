using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>
/// docs/phases/test-specification-phase2.md §2 のゴールデンフィクスチャ（F-01〜F-13）を構築するビルダー。
/// 各フィクスチャは独立した MasterDataSnapshot で、他フィクスチャとデータを共有しない。
/// 記載のない共通属性は Description=""・IconKey=null・VersionAdded="1.0.0"・VersionRemoved=null・GameEventId=null。
/// </summary>
internal static class CalculationFixtures
{
    public static Item Item(
        string id,
        string category = "部品",
        TransportKind kind = TransportKind.Belt,
        string? gameEventId = null,
        bool isGatherable = false) => new()
    {
        Id = id,
        Name = id,
        Category = category,
        TransportKind = kind,
        VersionAdded = "1.0.0",
        GameEventId = gameEventId,
        IsGatherable = isGatherable,
    };

    public static Facility Facility(string id, double powerConsumption = 0.0) => new()
    {
        Id = id,
        Name = id,
        Width = 1,
        Height = 1,
        PowerConsumption = powerConsumption,
        VersionAdded = "1.0.0",
    };

    public static Environment Env(
        string id,
        string providerFacilityId,
        string consumeItemId,
        double ratePerMinute,
        string? gameEventId = null,
        int coverableMachines = 4) => new()
    {
        Id = id,
        Name = id,
        ProviderFacilityId = providerFacilityId,
        ConsumeItemId = consumeItemId,
        ConsumeRatePerMinute = ratePerMinute,
        CoverableMachines = coverableMachines,
        VersionAdded = "1.0.0",
        GameEventId = gameEventId,
    };

    /// <summary>ペア行。recipeId は所属レシピの Id（JSON のネスト構造に対応）。</summary>
    public static RecipeFacility Pair(
        string recipeId,
        string facilityId,
        double cycleTime,
        string? environmentId = null,
        (string ItemId, double Rate)? fixedConsumption = null) => new()
    {
        RecipeId = recipeId,
        FacilityId = facilityId,
        CycleTime = cycleTime,
        EnvironmentId = environmentId,
        FixedConsumption = fixedConsumption is { } fc
            ? new FixedConsumption { ItemId = fc.ItemId, RatePerMinute = fc.Rate }
            : null,
    };

    public static Recipe Recipe(
        string id,
        RecipeFacility[] pairs,
        (string ItemId, double Quantity)[] inputs,
        (string ItemId, double Quantity)[] outputs,
        string versionAdded = "1.0.0",
        string? versionRemoved = null,
        string? gameEventId = null) => new()
    {
        Id = id,
        Name = id,
        VersionAdded = versionAdded,
        VersionRemoved = versionRemoved,
        GameEventId = gameEventId,
        Inputs = inputs
            .Select(i => new RecipeInput { ItemId = i.ItemId, Quantity = i.Quantity })
            .ToList(),
        Outputs = outputs
            .Select((o, index) => new RecipeOutput { ItemId = o.ItemId, Quantity = o.Quantity, SortOrder = index })
            .ToList(),
        Facilities = pairs.ToList(),
    };

    /// <summary>単一ペアのレシピを組み立てる簡易形。</summary>
    public static Recipe Recipe(
        string id,
        string facilityId,
        double cycleTime,
        (string ItemId, double Quantity)[] inputs,
        (string ItemId, double Quantity)[] outputs,
        string versionAdded = "1.0.0",
        string? versionRemoved = null,
        string? gameEventId = null,
        string? environmentId = null,
        (string ItemId, double Rate)? fixedConsumption = null) =>
        Recipe(
            id,
            [Pair(id, facilityId, cycleTime, environmentId, fixedConsumption)],
            inputs,
            outputs,
            versionAdded,
            versionRemoved,
            gameEventId);

    public static GameEvent GameEvent(string id) => new()
    {
        Id = id,
        Name = id,
        VersionAdded = "1.0.0",
    };

    /// <summary>マップ。rows は (ItemId, 無限なら true, 上限個/分) の行。</summary>
    public static GameMap Map(
        string id,
        (string ItemId, bool IsUnlimited, double? Rate)[]? rows = null,
        string? gameEventId = null) => new()
    {
        Id = id,
        Name = id,
        VersionAdded = "1.0.0",
        GameEventId = gameEventId,
        GatherRates = (rows ?? [])
            .Select(r => new GatherRate
            {
                ItemId = r.ItemId,
                IsUnlimited = r.IsUnlimited,
                RatePerMinute = r.Rate,
            })
            .ToList(),
    };

    public static MasterDataSnapshot Snapshot(
        IReadOnlyList<Item> items,
        IReadOnlyList<Facility> facilities,
        IReadOnlyList<Recipe> recipes,
        IReadOnlyList<Environment>? environments = null,
        IReadOnlyList<GameEvent>? gameEvents = null,
        IReadOnlyList<GameMap>? maps = null) => new()
    {
        Items = items,
        Facilities = facilities,
        Recipes = recipes,
        Environments = environments ?? [],
        GameEvents = gameEvents ?? [],
        Maps = maps ?? [],
    };

    public static ContextFilter Context(params string[] activeEventIds) =>
        new() { ActiveGameEventIds = activeEventIds };

    /// <summary>マップ選択つきのコンテキスト。mapId は選択マップ、残りは有効イベント。</summary>
    public static ContextFilter MapContext(string mapId, params string[] activeEventIds) =>
        new() { ActiveGameEventIds = activeEventIds, MapId = mapId };

    public static PairOverride Override(
        string itemId,
        string recipeId,
        string facilityId,
        double cycleTime,
        string? environmentId = null,
        (string ItemId, double Rate)? fixedConsumption = null) =>
        new(
            itemId,
            recipeId,
            facilityId,
            cycleTime,
            environmentId,
            fixedConsumption is { } fc
                ? new FixedConsumption { ItemId = fc.ItemId, RatePerMinute = fc.Rate }
                : null);

    public static ProductionPlan Run(
        MasterDataSnapshot master,
        IReadOnlyList<(string ItemId, double Rate)> targets,
        ContextFilter? context = null,
        IReadOnlyList<PairOverride>? overrides = null,
        IReadOnlyList<EnvironmentCountOverride>? environmentOverrides = null,
        IReadOnlyList<GatherRateOverride>? gatherOverrides = null)
    {
        return ProductionCalculator.Calculate(
            master,
            targets.Select(t => new ProductionTarget(t.ItemId, t.Rate)).ToList(),
            context ?? new ContextFilter(),
            overrides ?? [],
            environmentOverrides ?? [],
            gatherOverrides ?? []);
    }

    /// <summary>F-01: 直線チェーン（i-ore×2 → i-part、4秒）。</summary>
    public static MasterDataSnapshot F01() => Snapshot(
        [Item("i-ore", "採取素材", TransportKind.Belt, null, true), Item("i-part")],
        [Facility("f-asm")],
        [Recipe("r-part", "f-asm", 4.0, [("i-ore", 2.0)], [("i-part", 1.0)])]);

    /// <summary>F-02: 3段依存（i-a ← i-b ← i-c ← i-d）。</summary>
    public static MasterDataSnapshot F02() => Snapshot(
        [Item("i-a"), Item("i-b"), Item("i-c"), Item("i-d", "採取素材", TransportKind.Belt, null, true)],
        [Facility("f-a"), Facility("f-b"), Facility("f-c")],
        [
            Recipe("r-a", "f-a", 5.0, [("i-b", 2.0)], [("i-a", 1.0)]),
            Recipe("r-b", "f-b", 10.0, [("i-c", 3.0)], [("i-b", 1.0)]),
            Recipe("r-c", "f-c", 12.0, [("i-d", 4.0)], [("i-c", 2.0)]),
        ]);

    /// <summary>F-03: 代替レシピとペア選択（バージョン・イベント・ペア既定・上書き）。</summary>
    public static MasterDataSnapshot F03() => Snapshot(
        [
            Item("i-x"), Item("i-y"), Item("i-z"), Item("i-w"), Item("i-v"),
            Item("i-w2"), Item("i-w3"),
            Item("i-ore-x", "採取素材", TransportKind.Belt, null, true),
            Item("i-gas-w", "採取素材", TransportKind.Pipe, null, true),
            Item("i-fuel-w", "採取素材", TransportKind.Belt, null, true),
        ],
        [Facility("f-a"), Facility("f-b"), Facility("f-c"), Facility("f-disp")],
        [
            Recipe("r-x-old", [Pair("r-x-old", "f-a", 6.0)],
                [("i-ore-x", 1.0)], [("i-x", 1.0)], "1.0.0"),
            Recipe("r-x-new", [Pair("r-x-new", "f-a", 6.0), Pair("r-x-new", "f-b", 3.0)],
                [("i-ore-x", 1.0)], [("i-x", 2.0)], "1.2.0", "2.0.0"),
            Recipe("r-x-ltd", "f-a", 6.0,
                [("i-ore-x", 1.0)], [("i-x", 4.0)], "1.5.0", null, "ev-limited"),
            Recipe("r-y-a", "f-a", 6.0, [("i-ore-x", 1.0)], [("i-y", 1.0)], "1.0.0"),
            Recipe("r-y-b", "f-a", 6.0, [("i-ore-x", 1.0)], [("i-y", 1.0)], "1.0.0"),
            Recipe("r-z-badver", "f-a", 6.0, [("i-ore-x", 1.0)], [("i-z", 1.0)], "latest"),
            Recipe("r-z", "f-a", 6.0, [("i-ore-x", 1.0)], [("i-z", 1.0)], "0.9.0"),
            Recipe("r-w", [
                    Pair("r-w", "f-a", 6.0, "env-w"),
                    Pair("r-w", "f-b", 6.0, null, ("i-fuel-w", 30.0)),
                    Pair("r-w", "f-c", 6.0),
                ],
                [("i-ore-x", 1.0)], [("i-w", 1.0)]),
            // BT 改定で既定が変わる組合せ。同 CycleTime で「環境あり・FC なし」が「環境なし・FC あり」に勝つ。
            Recipe("r-w2", [
                    Pair("r-w2", "f-a", 6.0, "env-w"),
                    Pair("r-w2", "f-b", 6.0, null, ("i-fuel-w", 30.0)),
                    Pair("r-w2", "f-c", 6.0, null, ("i-fuel-w", 60.0)),
                ],
                [("i-ore-x", 1.0)], [("i-w2", 1.0)]),
            // FC 同士の同率では RatePerMinute 小さい方が EnvironmentId より先に評価される。
            Recipe("r-w3", [
                    Pair("r-w3", "f-a", 6.0, "env-w", ("i-fuel-w", 10.0)),
                    Pair("r-w3", "f-b", 6.0, null, ("i-fuel-w", 30.0)),
                ],
                [("i-ore-x", 1.0)], [("i-w3", 1.0)]),
            Recipe("r-v-new", [Pair("r-v-new", "f-a", 3.0, "env-ltd")],
                [("i-ore-x", 1.0)], [("i-v", 1.0)], "1.5.0"),
            Recipe("r-v-old", "f-a", 6.0, [("i-ore-x", 1.0)], [("i-v", 1.0)], "1.0.0"),
        ],
        [
            Env("env-w", "f-disp", "i-gas-w", 60.0),
            Env("env-ltd", "f-disp", "i-gas-w", 60.0, "ev-off"),
        ],
        [GameEvent("ev-limited"), GameEvent("ev-off")]);

    /// <summary>F-04: 循環依存（i-a ⇄ i-b、i-s 自己ループ）。</summary>
    public static MasterDataSnapshot F04() => Snapshot(
        [Item("i-a"), Item("i-b"), Item("i-s")],
        [Facility("f-cyc")],
        [
            Recipe("r-cyc-a", "f-cyc", 6.0, [("i-b", 1.0)], [("i-a", 1.0)]),
            Recipe("r-cyc-b", "f-cyc", 6.0, [("i-a", 1.0)], [("i-b", 1.0)]),
            Recipe("r-self", "f-cyc", 6.0, [("i-s", 1.0)], [("i-s", 1.0)]),
        ]);

    /// <summary>F-04 に直線チェーンを併記した派生（CYC-03 用）。</summary>
    public static MasterDataSnapshot F04WithLinearChain()
    {
        MasterDataSnapshot base_ = F04();
        return Snapshot(
            [.. base_.Items, Item("i-ore", "採取素材", TransportKind.Belt, null, true), Item("i-part")],
            [.. base_.Facilities, Facility("f-asm")],
            [.. base_.Recipes, Recipe("r-part", "f-asm", 4.0, [("i-ore", 2.0)], [("i-part", 1.0)])]);
    }

    /// <summary>
    /// F-04 派生: 循環に加えて r-x が i-a を副産する（BYP-07・NCP-07 用）。
    /// r-x の CycleTime 8秒は i-a の実効レート（7.5/分）を r-cyc-a（10/分）より低く保ち、
    /// 仕様決定 BA の既定選択でも i-a が循環レシピ側に残るようにするための値。
    /// </summary>
    public static MasterDataSnapshot F04WithByproduct()
    {
        MasterDataSnapshot base_ = F04();
        return Snapshot(
            [.. base_.Items, Item("i-x"), Item("i-ore", "採取素材", TransportKind.Belt, null, true)],
            [.. base_.Facilities, Facility("f-x")],
            [.. base_.Recipes, Recipe("r-x", "f-x", 8.0, [("i-ore", 1.0)], [("i-x", 1.0), ("i-a", 1.0)])]);
    }

    /// <summary>F-05: 副産物（r-m が i-p + i-q×2 を生産）。</summary>
    public static MasterDataSnapshot F05() => Snapshot(
        [
            Item("i-p"), Item("i-q"),
            Item("i-orem", "採取素材", TransportKind.Belt, null, true), Item("i-oreq", "採取素材", TransportKind.Belt, null, true),
        ],
        [Facility("f-m"), Facility("f-q")],
        [
            Recipe("r-m", "f-m", 4.0, [("i-orem", 1.0)], [("i-p", 1.0), ("i-q", 2.0)], "0.9.0"),
            Recipe("r-q", "f-q", 6.0, [("i-oreq", 3.0)], [("i-q", 1.0)], "1.0.0"),
        ]);

    /// <summary>F-05 派生: 副産物が需要の一部のみを賄う（r-m の i-q 副産が 1）。</summary>
    public static MasterDataSnapshot F05WithPartialByproduct() => Snapshot(
        [
            Item("i-p"), Item("i-q"),
            Item("i-orem", "採取素材", TransportKind.Belt, null, true), Item("i-oreq", "採取素材", TransportKind.Belt, null, true),
        ],
        [Facility("f-m"), Facility("f-q")],
        [
            Recipe("r-m", "f-m", 4.0, [("i-orem", 1.0)], [("i-p", 1.0), ("i-q", 1.0)], "0.9.0"),
            Recipe("r-q", "f-q", 6.0, [("i-oreq", 3.0)], [("i-q", 1.0)], "1.0.0"),
        ]);

    /// <summary>F-06: 切上げ過剰生産と流量調整（i-u×4 → i-t、2秒）。</summary>
    public static MasterDataSnapshot F06() => Snapshot(
        [Item("i-t"), Item("i-u", "採取素材", TransportKind.Belt, null, true)],
        [Facility("f-t")],
        [Recipe("r-t", "f-t", 2.0, [("i-u", 4.0)], [("i-t", 1.0)])]);

    /// <summary>F-08: 輸送容量（Belt/Pipe/None の3系統）。</summary>
    public static MasterDataSnapshot F08() => Snapshot(
        [
            Item("i-belt-item", "部品", TransportKind.Belt),
            Item("i-pipe-item", "部品", TransportKind.Pipe),
            Item("i-none-item", "部品", TransportKind.None),
            Item("i-belt-src", "採取素材", TransportKind.Belt, null, true),
            Item("i-pipe-src", "採取素材", TransportKind.Pipe, null, true),
        ],
        [Facility("f-tr")],
        [
            Recipe("r-belt", "f-tr", 6.0, [("i-belt-src", 1.0)], [("i-belt-item", 1.0)]),
            Recipe("r-pipe", "f-tr", 6.0, [("i-pipe-src", 1.0)], [("i-pipe-item", 1.0)]),
            // 仮想アイテムを入力には使えないため、微量の通常素材を入力とする（出力側の容量判定だけを見る）。
            Recipe("r-none", "f-tr", 6.0, [("i-belt-src", 0.01)], [("i-none-item", 1.0)]),
        ]);

    /// <summary>F-10: 環境（env-gas を要するペアと、不要ペアの両方を持つ r-hp）。</summary>
    public static MasterDataSnapshot F10() => Snapshot(
        [
            Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            Item("i-gas", "採取素材", TransportKind.Pipe, null, true),
            Item("i-hp"), Item("i-std"),
        ],
        [Facility("f-asm", 50.0), Facility("f-disp", 20.0)],
        [
            Recipe("r-hp", [Pair("r-hp", "f-asm", 8.0), Pair("r-hp", "f-asm", 4.0, "env-gas")],
                [("i-ore", 1.0)], [("i-hp", 1.0)]),
            Recipe("r-std", [Pair("r-std", "f-asm", 5.0, "env-gas")],
                [("i-ore", 1.0)], [("i-std", 1.0)]),
        ],
        [Env("env-gas", "f-disp", "i-gas", 360.0)]);

    /// <summary>F-10 変形: env-gas が非有効イベント ev-off 所属（ENV-05 用）。</summary>
    public static MasterDataSnapshot F10WithInactiveEnv()
    {
        MasterDataSnapshot base_ = F10();
        return Snapshot(
            base_.Items,
            base_.Facilities,
            base_.Recipes,
            [Env("env-gas", "f-disp", "i-gas", 360.0, "ev-off")],
            [GameEvent("ev-off")]);
    }

    /// <summary>
    /// F-10 変形: r-hp は環境ペアのみを持ち、副産物で i-hp を賄う r-side を追加（ENV-16 用）。
    /// i-hp 125/分は 4 秒ペアで 8.33 機分。r-side は i-side 1 サイクルにつき i-hp 5 を副産する。
    /// r-side の実効出力レート（5×60/30=10/分）を r-hp（15/分）より低くして、
    /// i-hp の選択ペアが r-side に流れないようにする。
    /// </summary>
    public static MasterDataSnapshot F10WithByproductRescue() => Snapshot(
        [
            Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            Item("i-gas", "採取素材", TransportKind.Pipe, null, true),
            Item("i-hp"), Item("i-side"),
        ],
        [Facility("f-asm", 50.0), Facility("f-disp", 20.0), Facility("f-sid")],
        [
            Recipe("r-hp", [Pair("r-hp", "f-asm", 4.0, "env-gas")],
                [("i-ore", 1.0)], [("i-hp", 1.0)]),
            Recipe("r-side", "f-sid", 30.0,
                [("i-ore", 1.0)], [("i-side", 1.0), ("i-hp", 5.0)]),
        ],
        [Env("env-gas", "f-disp", "i-gas", 360.0)]);

    /// <summary>F-11: 固定消費（r-fc のペアが i-fuel を 6 個/分 消費）。r-fuel は派生のみ。</summary>
    public static MasterDataSnapshot F11() => Snapshot(
        [Item("i-ore", "採取素材", TransportKind.Belt, null, true), Item("i-fuel", "採取素材", TransportKind.Belt, null, true), Item("i-fc")],
        [Facility("f-fc"), Facility("f-fuel")],
        [Recipe("r-fc", [Pair("r-fc", "f-fc", 30.0, null, ("i-fuel", 6.0))],
            [("i-ore", 1.0)], [("i-fc", 1.0)])]);

    /// <summary>F-11 派生: 燃料を自産できる r-fuel を追加（FIX-03 用）。</summary>
    public static MasterDataSnapshot F11WithFuelRecipe()
    {
        MasterDataSnapshot base_ = F11();
        return Snapshot(
            base_.Items,
            base_.Facilities,
            [
                .. base_.Recipes,
                Recipe("r-fuel", "f-fuel", 3.0, [("i-ore", 2.0)], [("i-fuel", 1.0)]),
            ]);
    }

    /// <summary>F-12 派生: イベント不可アイテムを副産する常設レシピ（EVT-05 用）。</summary>
    public static MasterDataSnapshot F12WithInactiveByproduct() => Snapshot(
        [
            Item("i-ltd", "部品", TransportKind.Belt, "ev-ltd"),
            Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            Item("i-side"),
        ],
        [Facility("f-asm")],
        [Recipe("r-side", "f-asm", 6.0, [("i-ore", 1.0)], [("i-side", 1.0), ("i-ltd", 1.0)])],
        gameEvents: [GameEvent("ev-ltd")]);

    /// <summary>F-11 派生: 提供設備とレシピ設備が兼用（FIX-04 用）。</summary>
    public static MasterDataSnapshot F11WithSharedProvider() => Snapshot(
        [Item("i-ore", "採取素材", TransportKind.Belt, null, true), Item("i-fuel", "採取素材", TransportKind.Belt, null, true), Item("i-fcx")],
        [Facility("f-fc")],
        [Recipe("r-fcx", [Pair("r-fcx", "f-fc", 30.0, "env-fcx", ("i-fuel", 30.0))],
            [("i-ore", 1.0)], [("i-fcx", 1.0)])],
        [Env("env-fcx", "f-fc", "i-ore", 60.0)]);

    /// <summary>F-14: 引き戻しで休眠したペアと、別ペア稼働の競合（SEL-11 用）。</summary>
    /// <remarks>
    /// i-y は r-yz（ペア A）で生産開始 → r-m の副産物で引き戻されペア A が休眠。
    /// 次の反復で env-x が i-z 需要を追加してペア B が稼働し、さらに次の反復で
    /// env-y が i-y 需要を追加して休眠中のペア A が復帰しようとする。
    /// </remarks>
    public static MasterDataSnapshot F14() => Snapshot(
        [
            Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            Item("i-y"), Item("i-z"), Item("i-m"), Item("i-w"),
        ],
        [Facility("f-a"), Facility("f-b"), Facility("f-m"), Facility("f-disp"), Facility("f-w")],
        [
            Recipe("r-m", "f-m", 6.0, [("i-ore", 1.0)], [("i-m", 1.0), ("i-y", 1.0)]),
            Recipe("r-yz",
                [Pair("r-yz", "f-a", 4.0), Pair("r-yz", "f-b", 8.0, "env-y")],
                [("i-ore", 1.0)], [("i-y", 1.0), ("i-z", 1.0)], versionAdded: "2.0.0"),
            Recipe("r-w", "f-w", 6.0, [("i-ore", 1.0)], [("i-w", 1.0)], environmentId: "env-x"),
        ],
        [
            Env("env-x", "f-disp", "i-z", 120.0),
            // 散布機 1 台で済むようカバー台数を大きく取る。BQ で台数が機械数比例になると、
            // 消費対象が自環境の生産物だと「需要→機械数→散布機→消費」の増幅ループで発散する
            // （旧仕様のレシピ数=台数では増幅係数 0 で収束していた）。シナリオの本質は
            // 休眠ペアの復帰なので、増幅が起きない 1 台固定相当へ寄せる。
            Env("env-y", "f-disp", "i-y", 240.0, coverableMachines: 64),
        ]);

    /// <summary>F-12: イベント限定アイテム（i-ltd・i-ltd-raw が ev-ltd 所属）。</summary>
    public static MasterDataSnapshot F12() => Snapshot(
        [
            Item("i-ltd", "部品", TransportKind.Belt, "ev-ltd"),
            Item("i-ltd-raw", "採取素材", TransportKind.Belt, "ev-ltd", true),
            Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            Item("i-fin"),
        ],
        [Facility("f-asm")],
        [
            Recipe("r-ltd", "f-asm", 6.0, [("i-ore", 1.0)], [("i-ltd", 1.0)]),
            Recipe("r-fin", "f-asm", 6.0, [("i-ltd", 2.0)], [("i-fin", 1.0)]),
        ],
        gameEvents: [GameEvent("ev-ltd")]);

    /// <summary>F-13: 収束（環境消費が生産へ展開するケースと、発散する固定消費ケース）。</summary>
    public static MasterDataSnapshot F13() => Snapshot(
        [
            Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            Item("i-gasp", "採取素材", TransportKind.Pipe, null, true),
            Item("i-xp"), Item("i-fuelself"),
        ],
        [Facility("f-xp"), Facility("f-mix"), Facility("f-disp"), Facility("f-self")],
        [
            Recipe("r-xp", [Pair("r-xp", "f-xp", 4.0, "env-gasp")],
                [("i-ore", 1.0)], [("i-xp", 1.0)]),
            Recipe("r-gasp", "f-mix", 6.0, [("i-ore", 1.0)], [("i-gasp", 10.0)]),
            Recipe("r-self", [Pair("r-self", "f-self", 60.0, null, ("i-fuelself", 120.0))],
                [("i-ore", 1.0)], [("i-fuelself", 1.0)]),
        ],
        [Env("env-gasp", "f-disp", "i-gasp", 360.0)]);

    /// <summary>F-15 系のマップ一覧。ev-off は無効イベント、m-ev・m-ev-shard はその所属マップ。</summary>
    private static GameMap[] F15Maps() =>
    [
        Map("m-cap", [("i-ore", false, 60.0), ("i-stone", false, 30.0), ("i-shard", false, 10.0)]),
        Map("m-inf", [("i-ore", true, null)]),
        Map("m-none"),
        Map("m-ev", [("i-ore", false, 999.0)], "ev-off"),
        Map("m-ev-shard", [("i-shard", false, 999.0)], "ev-off"),
    ];

    /// <summary>
    /// F-15: 採取上限。i-ore は採取素材かつ r-ore で生産可、i-stone は r-ore の入力、
    /// i-shard は採取素材だが生産レシピなし、i-part は非採取。
    /// r-ore: f-mine 4秒 i-stone×1 → i-ore×1、r-part: f-asm 4秒 i-ore×2 → i-part×1。
    /// </summary>
    public static MasterDataSnapshot F15() => Snapshot(
        [
            Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            Item("i-stone", "採取素材", TransportKind.Belt, null, true),
            Item("i-shard", "採取素材", TransportKind.Belt, null, true),
            Item("i-part"),
            Item("i-pack"),
            Item("i-fx"),
            Item("i-hot"),
        ],
        [Facility("f-mine"), Facility("f-asm"), Facility("f-fx"), Facility("f-disp")],
        [
            Recipe("r-ore", "f-mine", 4.0, [("i-stone", 1.0)], [("i-ore", 1.0)]),
            Recipe("r-part", "f-asm", 4.0, [("i-ore", 2.0)], [("i-part", 1.0)]),
        ],
        gameEvents: [GameEvent("ev-off")],
        maps: F15Maps());

    /// <summary>F-15 派生: i-ore を固定消費する r-fx（f-fx 30秒、i-ore 16個/分）を追加（GAT-12 用）。</summary>
    public static MasterDataSnapshot F15WithFixedConsumption()
    {
        MasterDataSnapshot base_ = F15();
        return Snapshot(
            base_.Items,
            base_.Facilities,
            [
                .. base_.Recipes,
                Recipe("r-fx", [Pair("r-fx", "f-fx", 30.0, null, ("i-ore", 16.0))],
                    [("i-stone", 1.0)], [("i-fx", 1.0)]),
            ],
            gameEvents: base_.GameEvents,
            maps: base_.Maps);
    }

    /// <summary>F-15 派生: i-ore を環境消費する env-burn を使う r-hot を追加（GAT-13 用）。</summary>
    public static MasterDataSnapshot F15WithEnvConsumption()
    {
        MasterDataSnapshot base_ = F15();
        return Snapshot(
            base_.Items,
            base_.Facilities,
            [
                .. base_.Recipes,
                Recipe("r-hot", "f-asm", 4.0, [("i-stone", 1.0)], [("i-hot", 1.0)], environmentId: "env-burn"),
            ],
            [Env("env-burn", "f-disp", "i-ore", 80.0)],
            base_.GameEvents,
            base_.Maps);
    }

    /// <summary>
    /// F-15 派生: i-ore を 30/サイクル副産する r-side（i-stone×1 → i-part×1 + i-ore×30）。
    /// i-part 向けに r-part より新しい VersionAdded で既定選択される（GAT-15 用）。
    /// </summary>
    public static MasterDataSnapshot F15WithByproduct() => Snapshot(
        [
            Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            Item("i-stone", "採取素材", TransportKind.Belt, null, true),
            Item("i-part"),
        ],
        [Facility("f-mine"), Facility("f-asm")],
        [
            Recipe("r-ore", "f-mine", 4.0, [("i-stone", 1.0)], [("i-ore", 1.0)]),
            Recipe("r-part", "f-asm", 4.0, [("i-ore", 2.0)], [("i-part", 1.0)]),
            Recipe("r-side", "f-asm", 6.0, [("i-stone", 1.0)], [("i-part", 1.0), ("i-ore", 30.0)], versionAdded: "2.0.0"),
        ],
        gameEvents: [GameEvent("ev-off")],
        maps: F15Maps());

    /// <summary>F-15 派生: i-ore の超過レシピ r-ore が i-x を要し、i-x が i-ore を要する循環（GAT-16 用）。</summary>
    public static MasterDataSnapshot F15WithCycle() => Snapshot(
        [
            Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            Item("i-x"),
        ],
        [Facility("f-mine"), Facility("f-asm")],
        [
            Recipe("r-ore", "f-mine", 4.0, [("i-x", 1.0)], [("i-ore", 1.0)]),
            Recipe("r-x", "f-asm", 4.0, [("i-ore", 1.0)], [("i-x", 1.0)]),
        ],
        maps: [Map("m-cap", [("i-ore", false, 60.0)])]);

    /// <summary>
    /// F-16: 採取上限超過の不足が後の引き戻しで解消されるケース（GAT-20 用）。
    /// i-x を r-x（i-shard×2 → i-x×1）で展開すると i-shard が上限超過で不足するが、
    /// i-y 目標の r-y が i-x を 10 副産するため r-x が引き戻され i-shard の未充足が消える。
    /// m-g16: i-shard 上限 10、i-ore 無限。
    /// </summary>
    public static MasterDataSnapshot F16() => Snapshot(
        [
            Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            Item("i-shard", "採取素材", TransportKind.Belt, null, true),
            Item("i-x"), Item("i-y"),
        ],
        [Facility("f-asm")],
        [
            Recipe("r-x", "f-asm", 4.0, [("i-shard", 2.0)], [("i-x", 1.0)]),
            Recipe("r-y", "f-asm", 4.0, [("i-ore", 1.0)], [("i-y", 1.0), ("i-x", 10.0)]),
        ],
        maps: [Map("m-g16", [("i-shard", false, 10.0), ("i-ore", true, null)])]);

    /// <summary>F-15 派生: 採取素材 i-ore が無効イベント ev-off 所属（GAT-19 用）。</summary>
    public static MasterDataSnapshot F15WithInactiveGatherable() => Snapshot(
        [
            Item("i-ore", "採取素材", TransportKind.Belt, "ev-off", true),
            Item("i-stone", "採取素材", TransportKind.Belt, null, true),
        ],
        [Facility("f-mine")],
        [Recipe("r-ore", "f-mine", 4.0, [("i-stone", 1.0)], [("i-ore", 1.0)])],
        gameEvents: [GameEvent("ev-off")],
        maps: [Map("m-cap", [("i-ore", false, 60.0), ("i-stone", false, 30.0)])]);

    /// <summary>
    /// F-17: 種↔作物の正味増循環（芽針型、作物側が正味増。ループゲイン 0.5）。
    /// r-grow: i-seed×1 + i-water×1 → i-crop×2。r-pick: i-crop×1 → i-seed×1。
    /// i-water は m-all で採取無限。
    /// </summary>
    public static MasterDataSnapshot F17() => Snapshot(
        [
            Item("i-seed"), Item("i-crop"),
            Item("i-water", "採取素材", TransportKind.Belt, null, true),
        ],
        [Facility("f-grow"), Facility("f-pick")],
        [
            Recipe("r-grow", "f-grow", 4.0, [("i-seed", 1.0), ("i-water", 1.0)], [("i-crop", 2.0)]),
            Recipe("r-pick", "f-pick", 4.0, [("i-crop", 1.0)], [("i-seed", 1.0)]),
        ],
        maps: [Map("m-all", [("i-water", true, null)])]);

    /// <summary>F-17 派生: 種側が正味増（サンドリーフ型。ループゲイン 0.5）。</summary>
    public static MasterDataSnapshot F17SeedPositive() => Snapshot(
        [Item("i-seed"), Item("i-crop")],
        [Facility("f-grow"), Facility("f-pick")],
        [
            Recipe("r-grow2", "f-grow", 4.0, [("i-seed", 1.0)], [("i-crop", 1.0)]),
            Recipe("r-pick2", "f-pick", 4.0, [("i-crop", 1.0)], [("i-seed", 2.0)]),
        ]);

    /// <summary>F-17 派生: 正味減循環（r-na: i-b×2 → i-a×1、r-nb: i-a×1 → i-b×1。ループゲイン 2）。</summary>
    public static MasterDataSnapshot F17Negative() => Snapshot(
        [Item("i-a"), Item("i-b")],
        [Facility("f-asm")],
        [
            Recipe("r-na", "f-asm", 4.0, [("i-b", 2.0)], [("i-a", 1.0)]),
            Recipe("r-nb", "f-asm", 4.0, [("i-a", 1.0)], [("i-b", 1.0)]),
        ]);

    /// <summary>F-17 派生: i-crop を入力とする下流レシピ r-char（i-crop×1 → i-char×2）を追加。</summary>
    public static MasterDataSnapshot F17WithDownstream()
    {
        MasterDataSnapshot base_ = F17();
        return Snapshot(
            [.. base_.Items, Item("i-char")],
            [.. base_.Facilities, Facility("f-asm")],
            [
                .. base_.Recipes,
                Recipe("r-char", "f-asm", 4.0, [("i-crop", 1.0)], [("i-char", 2.0)]),
            ],
            maps: base_.Maps);
    }

    /// <summary>
    /// F-17 派生: 採取素材を含む正味増循環（ループゲイン 0.5）。
    /// i-ore は m-cap で採取上限 60。r-ore: i-x×1 → i-ore×2、r-x: i-ore×1 → i-x×1。
    /// </summary>
    public static MasterDataSnapshot F17Gatherable() => Snapshot(
        [
            Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            Item("i-x"),
        ],
        [Facility("f-mine"), Facility("f-asm")],
        [
            Recipe("r-ore", "f-mine", 4.0, [("i-x", 1.0)], [("i-ore", 2.0)]),
            Recipe("r-x", "f-asm", 4.0, [("i-ore", 1.0)], [("i-x", 1.0)]),
        ],
        maps: [Map("m-cap", [("i-ore", false, 60.0)])]);

    /// <summary>
    /// F-17 派生: 正味増と正味減が同一アイテムに絡む混合循環（NCP-09 用）。
    /// r-a: i-b×1 + i-c×1 → i-a×1。r-b: i-a×1 → i-b×2（A→B→A はゲイン 0.5）。
    /// r-c: i-a×2 → i-c×1（A→C→A はゲイン 2）。
    /// </summary>
    public static MasterDataSnapshot F17Mixed() => Snapshot(
        [Item("i-a"), Item("i-b"), Item("i-c")],
        [Facility("f-asm")],
        [
            Recipe("r-a", "f-asm", 4.0, [("i-b", 1.0), ("i-c", 1.0)], [("i-a", 1.0)]),
            Recipe("r-b", "f-asm", 4.0, [("i-a", 1.0)], [("i-b", 2.0)]),
            Recipe("r-c", "f-asm", 4.0, [("i-a", 2.0)], [("i-c", 1.0)]),
        ]);

    /// <summary>
    /// F-17 混合型に i-x・i-ore・r-px（i-ore×1 → i-x×1 + i-c×1）を追加（NCP-10 用）。
    /// r-px の副産物 i-c が A→C→A の正味減枝の需要を賄い、その枝を死なせる。
    /// </summary>
    public static MasterDataSnapshot F17MixedByproduct()
    {
        MasterDataSnapshot base_ = F17Mixed();
        return Snapshot(
            [.. base_.Items, Item("i-x"), Item("i-ore", "採取素材", TransportKind.Belt, null, true)],
            [.. base_.Facilities, Facility("f-x")],
            [.. base_.Recipes,
                Recipe("r-px", "f-x", 4.0, [("i-ore", 1.0)], [("i-x", 1.0), ("i-c", 1.0)])]);
    }

    /// <summary>F-17 派生: r-grow のペアに i-water 固定消費 6/分を追加。</summary>
    public static MasterDataSnapshot F17WithFixedConsumption()
    {
        MasterDataSnapshot base_ = F17();
        return Snapshot(
            base_.Items,
            base_.Facilities,
            [
                Recipe("r-grow", "f-grow", 4.0,
                    [("i-seed", 1.0), ("i-water", 1.0)], [("i-crop", 2.0)],
                    fixedConsumption: ("i-water", 6.0)),
                Recipe("r-pick", "f-pick", 4.0, [("i-crop", 1.0)], [("i-seed", 1.0)]),
            ],
            maps: base_.Maps);
    }

    /// <summary>
    /// F-18: 実効出力レートによる既定レシピ選択（仕様決定 BA）。
    /// 同一 VersionAdded のレシピ対で、Id 昇順と実効レート降順が逆方向に効く命名にする。
    /// env-q5・env-q6 は所属イベント ev-off が無効のコンテキストでペア不適格。
    /// </summary>
    public static MasterDataSnapshot F18() => Snapshot(
        [
            Item("i-q1"), Item("i-q2"), Item("i-q3"), Item("i-q4"),
            Item("i-q5"), Item("i-q6"), Item("i-q7"), Item("i-q8"), Item("i-other"),
            Item("i-ore", "採取素材", TransportKind.Belt, null, true),
            Item("i-gas", "採取素材", TransportKind.Pipe, null, true),
        ],
        [Facility("f-a"), Facility("f-b"), Facility("f-disp")],
        [
            Recipe("r-q1-lean", "f-a", 3.0, [("i-ore", 1.0)], [("i-q1", 1.0)]),
            Recipe("r-q1-rich", "f-a", 3.0, [("i-ore", 1.0)], [("i-q1", 2.0)]),
            Recipe("r-q2-a-slow", "f-a", 6.0, [("i-ore", 1.0)], [("i-q2", 1.0)]),
            Recipe("r-q2-b-fast", "f-a", 3.0, [("i-ore", 1.0)], [("i-q2", 1.0)]),
            Recipe("r-q3-a", "f-a", 6.0, [("i-ore", 1.0)], [("i-q3", 1.0)]),
            Recipe("r-q3-b", "f-a", 6.0, [("i-ore", 1.0)], [("i-q3", 1.0)]),
            Recipe("r-q4-alt", "f-a", 5.0, [("i-ore", 1.0)], [("i-q4", 1.0)]),
            Recipe("r-q4-multi", [
                    Pair("r-q4-multi", "f-a", 6.0),
                    Pair("r-q4-multi", "f-b", 2.0),
                ],
                [("i-ore", 1.0)], [("i-q4", 1.0)]),
            Recipe("r-q5", [
                    Pair("r-q5", "f-a", 1.0, "env-q5"),
                    Pair("r-q5", "f-a", 8.0),
                ],
                [("i-ore", 1.0)], [("i-q5", 1.0)]),
            Recipe("r-q5-alt", "f-a", 4.0, [("i-ore", 1.0)], [("i-q5", 1.0)]),
            Recipe("r-q6-env", "f-a", 1.0,
                [("i-ore", 1.0)], [("i-q6", 1.0)], "1.0.0", null, null, "env-q6"),
            Recipe("r-q6-slow", "f-a", 4.0, [("i-ore", 1.0)], [("i-q6", 1.0)]),
            Recipe("r-q7-new", "f-a", 10.0, [("i-ore", 1.0)], [("i-q7", 1.0)], "2.0.0"),
            Recipe("r-q7-old", "f-a", 1.0, [("i-ore", 1.0)], [("i-q7", 1.0)], "1.0.0"),
            Recipe("r-q8-a-thin", "f-a", 6.0, [("i-ore", 1.0)], [("i-q8", 1.0)]),
            Recipe("r-q8-b-rich", "f-a", 6.0,
                [("i-ore", 1.0)], [("i-other", 1.0), ("i-q8", 2.0)]),
        ],
        environments:
        [
            Env("env-q5", "f-disp", "i-gas", 60.0, "ev-off"),
            Env("env-q6", "f-disp", "i-gas", 60.0, "ev-off"),
        ],
        gameEvents: [GameEvent("ev-off")]);
}
