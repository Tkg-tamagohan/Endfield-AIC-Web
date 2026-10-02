using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application;

/// <summary>素材行の表示用モデル。</summary>
public sealed record MaterialViewRow(
    string ItemId,
    double RequiredPerMinute,
    IReadOnlyList<SupplyPortion> Supplies,
    double UnmetPerMinute,
    string? SelectedPairKey);

/// <summary>設備行の表示用モデル。FlowLimits は調整済ビューでのみ入る。</summary>
public sealed record FacilityViewRow(
    string FacilityId,
    double ExactCount,
    int CeilCount,
    IReadOnlyList<FlowAdjustment> FlowLimits);

/// <summary>結果画面の表示用モデル（調整済／未調整のいずれか 1 状態）。</summary>
public sealed record ResultView(
    IReadOnlyList<MaterialViewRow> Materials,
    IReadOnlyList<FacilityViewRow> Facilities,
    IReadOnlyList<EnvironmentRequirement> Environments,
    double TotalPowerConsumption,
    IReadOnlyList<SurplusProduction> Surpluses,
    IReadOnlyList<CalculationWarning> Warnings);

/// <summary>
/// ProductionPlan から表示用の 2 状態（仕様決定 O）を導出する。
/// 調整済: 計画どおりの供給量（余剰は計算結果のもののみ、推奨流量制限を表示）。
/// 未調整: 切上げ台数でフル稼働させた想定。設備ごとの倍率
/// s(F) = (CeilCount(F) − 散布機台数(F)) / レシピ実数台数(F) をレシピ生産に掛け、
/// 余剰は produced' − 需要で再計算する（推測 → docs/phases/implementation-plan-phase4.md §5）。
/// </summary>
public static class ResultViewBuilder
{
    private const double Epsilon = 1e-9;

    public static ResultView Build(
        ProductionPlan plan,
        MasterDataSnapshot snapshot,
        ContextFilter context,
        bool unadjusted)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);

        // レシピ Id → 稼働ラン（ペアはレシピごと一意のため RecipeId で引ける）。
        var runsByRecipe = plan.RecipeRuns
            .GroupBy(r => r.RecipeId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var scaleByFacility = new Dictionary<string, double>(StringComparer.Ordinal);
        if (unadjusted)
        {
            scaleByFacility = ComputeUnadjustedFacilityScales(plan, snapshot);
        }

        var selectionsByItem = plan.PairSelections
            .GroupBy(s => s.ItemId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var materials = plan.ItemRequirements
            .Select(req =>
            {
                IReadOnlyList<SupplyPortion> supplies = unadjusted
                    ? req.Supplies
                        .Select(p => p.RecipeId is not null && runsByRecipe.TryGetValue(p.RecipeId, out RecipeRun? run)
                            ? p with { AmountPerMinute = p.AmountPerMinute * scaleByFacility[run.FacilityId] }
                            : p)
                        .ToList()
                    : req.Supplies;
                string? selectedKey = selectionsByItem.TryGetValue(req.ItemId, out PairSelection? sel)
                    ? PairOptionKey.Create(sel)
                    : null;
                return new MaterialViewRow(req.ItemId, req.RequiredPerMinute, supplies, req.UnmetPerMinute, selectedKey);
            })
            .ToList();

        var limitsByFacility = new Dictionary<string, List<FlowAdjustment>>(StringComparer.Ordinal);
        if (!unadjusted)
        {
            foreach (FlowAdjustment adjustment in plan.FlowAdjustments)
            {
                if (!runsByRecipe.TryGetValue(adjustment.RecipeId, out RecipeRun? run))
                {
                    continue;
                }

                if (!limitsByFacility.TryGetValue(run.FacilityId, out List<FlowAdjustment>? list))
                {
                    list = [];
                    limitsByFacility[run.FacilityId] = list;
                }

                list.Add(adjustment);
            }
        }

        var facilities = plan.FacilityRequirements
            .Select(f => new FacilityViewRow(
                f.FacilityId,
                f.ExactCount,
                f.CeilCount,
                limitsByFacility.TryGetValue(f.FacilityId, out List<FlowAdjustment>? limits)
                    ? limits
                    : []))
            .ToList();

        IReadOnlyList<SurplusProduction> surpluses = unadjusted
            ? RecomputeSurpluses(plan, snapshot, context, runsByRecipe, scaleByFacility)
            : plan.Surpluses;

        return new ResultView(
            materials,
            facilities,
            plan.EnvironmentRequirements,
            plan.TotalPowerConsumption,
            surpluses,
            plan.Warnings);
    }

    /// <summary>
    /// 未調整ビューの設備倍率 s(F)。FlowGraphModelBuilder がグラフの流量を
    /// 表示中ビューと一致させるために共用する（implementation-plan-phase15 §3）。
    /// 確定ペアを RecipeId から引く。同レシピに同設備の複数ペア行がありうるため
    /// FacilityId では実際に稼働中のペアを一意に特定できない。
    /// </summary>
    internal static Dictionary<string, double> ComputeUnadjustedFacilityScales(
        ProductionPlan plan,
        MasterDataSnapshot snapshot)
    {
        var pairByRecipe = plan.PairSelections
            .GroupBy(s => s.RecipeId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Pair, StringComparer.Ordinal);
        return ComputeFacilityScales(plan, snapshot, pairByRecipe);
    }

    /// <summary>
    /// 未調整ビューの余剰再計算。FlowGraphModelBuilder が共用する。
    /// </summary>
    internal static List<SurplusProduction> ComputeUnadjustedSurpluses(
        ProductionPlan plan,
        MasterDataSnapshot snapshot,
        ContextFilter context,
        IReadOnlyDictionary<string, double> scaleByFacility)
    {
        var runsByRecipe = plan.RecipeRuns
            .GroupBy(r => r.RecipeId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return RecomputeSurpluses(plan, snapshot, context, runsByRecipe, scaleByFacility);
    }

    /// <summary>設備ごとの未調整倍率 s(F)。散布機のみの設備は 1。</summary>
    private static Dictionary<string, double> ComputeFacilityScales(
        ProductionPlan plan,
        MasterDataSnapshot snapshot,
        IReadOnlyDictionary<string, RecipeFacility> pairByRecipe)
    {
        // レシピ由来の実数台数（Σ cycles × cycleTime / 60）。
        var recipeExact = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (RecipeRun run in plan.RecipeRuns)
        {
            double cycleTime;
            if (pairByRecipe.TryGetValue(run.RecipeId, out RecipeFacility? pair))
            {
                cycleTime = pair.CycleTime;
            }
            else if (snapshot.RecipesById.TryGetValue(run.RecipeId, out Recipe? recipe)
                && recipe.Facilities.FirstOrDefault(p => p.FacilityId == run.FacilityId) is { } fallbackPair)
            {
                cycleTime = fallbackPair.CycleTime;
            }
            else
            {
                continue;
            }

            recipeExact[run.FacilityId] = recipeExact.GetValueOrDefault(run.FacilityId)
                + run.CyclesPerMinute * cycleTime / 60.0;
        }

        var dispenserByFacility = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (EnvironmentRequirement env in plan.EnvironmentRequirements)
        {
            dispenserByFacility[env.ProviderFacilityId] =
                dispenserByFacility.GetValueOrDefault(env.ProviderFacilityId) + env.DispenserCount;
        }

        var scales = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (FacilityRequirement f in plan.FacilityRequirements)
        {
            double recipePart = recipeExact.GetValueOrDefault(f.FacilityId);
            if (recipePart <= Epsilon)
            {
                scales[f.FacilityId] = 1.0;
                continue;
            }

            double available = f.CeilCount - dispenserByFacility.GetValueOrDefault(f.FacilityId);
            scales[f.FacilityId] = Math.Max(available / recipePart, 0);
        }

        return scales;
    }

    /// <summary>
    /// 未調整の余剰を再計算する。produced' = Σ run(cycles × s(F) × 出力個数)。
    /// イベント不可アイテムは全量が余剰（仕様決定 X を調整済と同じ規則で適用）。
    /// </summary>
    private static List<SurplusProduction> RecomputeSurpluses(
        ProductionPlan plan,
        MasterDataSnapshot snapshot,
        ContextFilter context,
        IReadOnlyDictionary<string, RecipeRun> runsByRecipe,
        IReadOnlyDictionary<string, double> scaleByFacility)
    {
        var produced = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (RecipeRun run in plan.RecipeRuns)
        {
            if (!snapshot.RecipesById.TryGetValue(run.RecipeId, out Recipe? recipe))
            {
                continue;
            }

            double scale = scaleByFacility.GetValueOrDefault(run.FacilityId, 1.0);
            foreach (RecipeOutput output in recipe.Outputs)
            {
                produced[output.ItemId] = produced.GetValueOrDefault(output.ItemId)
                    + run.CyclesPerMinute * scale * output.Quantity;
            }
        }

        var demandByItem = plan.ItemRequirements
            .ToDictionary(r => r.ItemId, r => r.RequiredPerMinute, StringComparer.Ordinal);

        // 採取（外部調達）の供給も需要の充当に含める。採取＋レシピの併存供給で
        // 生産分だけを見ると余剰が過小になるため（仕様決定 AD で併存が生じた）。
        var gatheredByItem = plan.ItemRequirements
            .ToDictionary(
                r => r.ItemId,
                r => r.Supplies.Where(s => s.Kind == SupplyKind.Gathered).Sum(s => s.AmountPerMinute),
                StringComparer.Ordinal);

        var surpluses = new List<SurplusProduction>();
        foreach ((string itemId, double amount) in produced)
        {
            bool inactive = snapshot.ItemsById.TryGetValue(itemId, out Item? item)
                && item.GameEventId is not null
                && !context.ActiveGameEventIds.Contains(item.GameEventId);

            double excess = inactive
                ? amount
                : amount + gatheredByItem.GetValueOrDefault(itemId) - demandByItem.GetValueOrDefault(itemId);
            if (excess > Epsilon)
            {
                surpluses.Add(new SurplusProduction(itemId, excess));
            }
        }

        return surpluses;
    }
}
