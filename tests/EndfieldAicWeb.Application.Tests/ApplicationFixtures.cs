using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using DomainEnvironment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Application.Tests;

/// <summary>
/// docs/phases/test-specification-phase4.md のフィクスチャ A-01〜A-05 に対応するマスタ構築。
/// </summary>
internal static class ApplicationFixtures
{
    public static Item Item(string id, string name, TransportKind kind = TransportKind.Belt, string? eventId = null, bool gatherable = false) =>
        new()
        {
            Id = id,
            Name = name,
            Category = "",
            TransportKind = kind,
            VersionAdded = "1.0.0",
            GameEventId = eventId,
            IsGatherable = gatherable,
        };

    public static Facility Facility(string id, string name, double power) =>
        new() { Id = id, Name = name, PowerConsumption = power, VersionAdded = "1.0.0" };

    public static DomainEnvironment Env(string id, string name, string providerFacilityId, string consumeItemId, double ratePerMinute, string? eventId = null) =>
        new()
        {
            Id = id,
            Name = name,
            ProviderFacilityId = providerFacilityId,
            ConsumeItemId = consumeItemId,
            ConsumeRatePerMinute = ratePerMinute,
            VersionAdded = "1.0.0",
            GameEventId = eventId,
        };

    public static RecipeFacility Pair(string facilityId, double cycleTime, string? envId = null, FixedConsumption? fc = null, string recipeId = "") =>
        new() { RecipeId = recipeId, FacilityId = facilityId, CycleTime = cycleTime, EnvironmentId = envId, FixedConsumption = fc };

    public static Recipe Recipe(
        string id,
        string name,
        IReadOnlyList<(string ItemId, double Quantity)> inputs,
        IReadOnlyList<(string ItemId, double Quantity)> outputs,
        IReadOnlyList<RecipeFacility> pairs,
        string? eventId = null) =>
        new()
        {
            Id = id,
            Name = name,
            GameEventId = eventId,
            Inputs = inputs.Select(i => new RecipeInput { ItemId = i.ItemId, Quantity = i.Quantity }).ToList(),
            Outputs = outputs.Select(o => new RecipeOutput { ItemId = o.ItemId, Quantity = o.Quantity }).ToList(),
            Facilities = pairs.Select(p => { p.RecipeId = id; return p; }).ToList(),
            VersionAdded = "1.0.0",
        };

    public static GameEvent Event(string id, string name, DateTime? from = null, DateTime? to = null) =>
        new() { Id = id, Name = name, ActiveFrom = from, ActiveTo = to, VersionAdded = "1.0.0" };

    public static MasterDataSnapshot Snapshot(
        IReadOnlyList<Item> items,
        IReadOnlyList<Facility> facilities,
        IReadOnlyList<DomainEnvironment> environments,
        IReadOnlyList<GameEvent> gameEvents,
        IReadOnlyList<Recipe> recipes,
        IReadOnlyList<GameMap>? maps = null) =>
        new()
        {
            Items = items,
            Facilities = facilities,
            Environments = environments,
            GameEvents = gameEvents,
            Recipes = recipes,
            Maps = maps ?? [],
        };

    public static ContextFilter Context(params string[] activeEventIds) =>
        new() { ActiveGameEventIds = activeEventIds };

    /// <summary>
    /// A-01: 環境付きペアを含む単一レシピ。
    /// i-part 60/分 → 3 秒 env-gas ペアが既定 → 加工機 3 台 + 散布機 1 台、ガス 360/分。
    /// </summary>
    public static MasterDataSnapshot A01()
    {
        return Snapshot(
            [Item("i-ore", "鉄鉱石", gatherable: true), Item("i-gas", "活性ガス", TransportKind.Pipe, gatherable: true), Item("i-part", "汎用部品")],
            [Facility("f-asm", "加工機", 50), Facility("f-disp", "ガス散布機", 20)],
            [Env("env-gas", "ガス散布", "f-disp", "i-gas", 360)],
            [],
            [
                Recipe("r-part", "汎用部品", [("i-ore", 2)], [("i-part", 1)],
                    [Pair("f-asm", 4), Pair("f-asm", 3, "env-gas")]),
            ]);
    }

    /// <summary>
    /// A-02: 実数台数が切上げを生む単一レシピ。
    /// i-t 72/分 → 72 サイクル × 2 秒 = 2.4 台 → 切上げ 3 台（未調整倍率 1.25）。
    /// </summary>
    public static MasterDataSnapshot A02()
    {
        return Snapshot(
            [Item("i-u", "上流素材", gatherable: true), Item("i-t", "加工品")],
            [Facility("f-t", "組立機", 30)],
            [],
            [],
            [Recipe("r-t", "加工品", [("i-u", 4)], [("i-t", 1)], [Pair("f-t", 2)])]);
    }

    /// <summary>
    /// A-03: 同一設備を共用する 2 レシピ。合計実数 3.33… → 切上げ 4 で両方に同じ倍率。
    /// r-x 30/分 → 2.0、r-y 20/分 → 4/3、計 10/3。
    /// </summary>
    public static MasterDataSnapshot A03()
    {
        return Snapshot(
            [Item("i-u", "上流素材", gatherable: true), Item("i-x", "中間品X"), Item("i-y", "中間品Y")],
            [Facility("f-sh", "共用機", 10)],
            [],
            [],
            [
                Recipe("r-x", "中間品X", [("i-u", 1)], [("i-x", 1)], [Pair("f-sh", 4)]),
                Recipe("r-y", "中間品Y", [("i-u", 1)], [("i-y", 1)], [Pair("f-sh", 4)]),
            ]);
    }

    /// <summary>
    /// A-04: ペア候補フィルタ用。i-x のレシピ:
    /// r-x-main（常設、f-a 6 秒 / f-b 3 秒）・r-x-ev（ev-off 所属）・r-x-env（常設だが env-ltd 環境は ev-off 所属）。
    /// ev-off 無効時の候補は r-x-main の 2 ペアのみ、既定は 3 秒ペア。
    /// </summary>
    public static MasterDataSnapshot A04()
    {
        return Snapshot(
            [Item("i-u", "上流素材", gatherable: true), Item("i-x", "中間品X")],
            [Facility("f-a", "機A", 10), Facility("f-b", "機B", 10), Facility("f-disp", "散布機", 5)],
            [Env("env-ltd", "限定環境", "f-disp", "i-u", 60, "ev-off")],
            [Event("ev-off", "終了イベント")],
            [
                Recipe("r-x-main", "中間品X", [("i-u", 1)], [("i-x", 1)],
                    [Pair("f-a", 6), Pair("f-b", 3)]),
                Recipe("r-x-ev", "中間品X（イベント）", [("i-u", 1)], [("i-x", 1)],
                    [Pair("f-a", 2)], "ev-off"),
                Recipe("r-x-env", "中間品X（限定環境）", [("i-u", 1)], [("i-x", 1)],
                    [Pair("f-a", 4, "env-ltd")]),
            ]);
    }

    /// <summary>
    /// A-05: 未使用イベントアイテムの副産物。r-side は i-side と i-ltd（ev-ltd 限定）を同量産む。
    /// i-side 8/分 → 8 サイクル × 6 秒 = 0.8 台 → 切上げ 1 台（倍率 1.25 → 産出 10/分）。
    /// ev-ltd 無効時、i-ltd は産出全量が余剰。
    /// </summary>
    public static MasterDataSnapshot A05()
    {
        return Snapshot(
            [
                Item("i-u", "上流素材", gatherable: true), Item("i-side", "主産物"), Item("i-ltd", "限定副産物", eventId: "ev-ltd"),
            ],
            [Facility("f-asm", "加工機", 10)],
            [],
            [Event("ev-ltd", "限定イベント")],
            [
                Recipe("r-side", "主産物", [("i-u", 1)], [("i-side", 1), ("i-ltd", 1)],
                    [Pair("f-asm", 6)]),
            ]);
    }
}
