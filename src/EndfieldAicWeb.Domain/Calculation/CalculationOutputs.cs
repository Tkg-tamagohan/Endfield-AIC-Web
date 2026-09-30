using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Domain.Calculation;

/// <summary>
/// 計算結果全体。素材・設備・電力・余剰・警告をまとめて返す。
/// 「未調整／調整済」の 2 状態は表示層の切替であり、計算結果は共用する（仕様決定 O）。
/// </summary>
public sealed class ProductionPlan
{
    /// <summary>需要のあるアイテムごとの要求量と供給内訳。</summary>
    public required IReadOnlyList<ItemRequirement> ItemRequirements { get; init; }

    /// <summary>設備ごとの必要台数（実数と切上げ）。散布機を含む。</summary>
    public required IReadOnlyList<FacilityRequirement> FacilityRequirements { get; init; }

    /// <summary>稼働が確定したペアとそのサイクル数/分。選択結果の可視化に使う。</summary>
    public required IReadOnlyList<RecipeRun> RecipeRuns { get; init; }

    /// <summary>
    /// 需要アイテムごとの確定ペア（実際に稼働中のもののみ）。
    /// UI がアイテム単位のペア代替選択で現在値を表示するために使う。
    /// </summary>
    public required IReadOnlyList<PairSelection> PairSelections { get; init; }

    /// <summary>稼働に必要となった環境ごとの散布機台数と消費流量。</summary>
    public required IReadOnlyList<EnvironmentRequirement> EnvironmentRequirements { get; init; }

    /// <summary>Σ(設備の消費電力 × 切上げ台数)。散布機分を含む。発電側は計算しない（Q/Y）。</summary>
    public required double TotalPowerConsumption { get; init; }

    /// <summary>副産物の充当残と流量調整で抑制しきれない過剰分。</summary>
    public required IReadOnlyList<SurplusProduction> Surpluses { get; init; }

    /// <summary>切上げ台数の過剰生産を抑制するための推奨流量制限。</summary>
    public required IReadOnlyList<FlowAdjustment> FlowAdjustments { get; init; }

    public required IReadOnlyList<CalculationWarning> Warnings { get; init; }
}

/// <summary>
/// アイテムごとの要求量と供給内訳、未充足量。
/// </summary>
public sealed record ItemRequirement(
    string ItemId,
    double RequiredPerMinute,
    IReadOnlyList<SupplyPortion> Supplies,
    double UnmetPerMinute);

/// <summary>
/// 供給内訳の1要素（どの経路から何個/分が供給されるか）。
/// </summary>
public sealed record SupplyPortion(SupplyKind Kind, string? RecipeId, double AmountPerMinute);

public enum SupplyKind
{
    /// <summary>そのアイテムの選択ペアによる生産。</summary>
    Recipe,

    /// <summary>他レシピの副産物による充当。</summary>
    Byproduct,

    /// <summary>採取素材。レシピを持たない終端で外部調達扱い。</summary>
    Gathered,
}

/// <summary>
/// 設備の必要台数（実数値と切上げ整数を併記）。散布機は実数=切上げの指定台数。
/// </summary>
public sealed record FacilityRequirement(string FacilityId, double ExactCount, int CeilCount);

/// <summary>
/// 稼働が確定したペアとそのサイクル数/分。
/// </summary>
public sealed record RecipeRun(string RecipeId, string FacilityId, double CyclesPerMinute);

/// <summary>
/// 必要となった環境とその供給設備の台数・消費流量（仕様決定 I）。
/// </summary>
public sealed record EnvironmentRequirement(
    string EnvironmentId,
    string ProviderFacilityId,
    int DispenserCount,
    string ConsumeItemId,
    double ConsumeRatePerMinuteTotal);

/// <summary>
/// 充当しきれなかった余剰生産（副産物残など）。
/// </summary>
public sealed record SurplusProduction(string ItemId, double ExcessPerMinute);

/// <summary>
/// 切上げ台数の過剰生産を抑制するための、レシピ×入力アイテム単位の推奨流量制限。
/// 推奨制限値は要求流量の実数値をそのまま出力し、丸めない。
/// </summary>
public sealed record FlowAdjustment(
    string RecipeId,
    string InputItemId,
    double RequiredPerSecond,
    double RecommendedLimitPerSecond);

/// <summary>
/// 需要アイテムに対して確定した（実際に稼働中の）ペア。
/// </summary>
public sealed record PairSelection(string ItemId, string RecipeId, RecipeFacility Pair);
