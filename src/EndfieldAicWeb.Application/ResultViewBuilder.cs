using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

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

        // レシピ Id → 稼働ラン index（ペアはレシピごと一意のため RecipeId で引ける）。
        var runIndexByRecipe = plan.RecipeRuns
            .Select((run, index) => (run, index))
            .GroupBy(t => t.run.RecipeId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().index, StringComparer.Ordinal);
        var runsByRecipe = plan.RecipeRuns
            .GroupBy(r => r.RecipeId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        IReadOnlyList<double> runScales = unadjusted
            ? ComputeUnadjustedRunScales(plan, snapshot)
            : [];

        var selectionsByItem = plan.PairSelections
            .GroupBy(s => s.ItemId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var materials = plan.ItemRequirements
            .Select(req =>
            {
                IReadOnlyList<SupplyPortion> supplies = unadjusted
                    ? req.Supplies
                        .Select(p => p.RecipeId is not null && runIndexByRecipe.TryGetValue(p.RecipeId, out int runIndex)
                            ? p with { AmountPerMinute = p.AmountPerMinute * runScales[runIndex] }
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
            ? RecomputeSurpluses(plan, snapshot, context, runScales)
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
    /// 未調整ビューのランごとの倍率（RecipeRuns と同じ index 順）。
    /// FlowGraphModelBuilder がグラフの流量を表示中ビューと一致させるために共用する
    /// （implementation-plan-phase15 §3、phase26 §3.4）。
    /// 確定ペアを RecipeId から引く。同レシピに同設備の複数ペア行がありうるため
    /// FacilityId では実際に稼働中のペアを一意に特定できない。
    /// </summary>
    internal static IReadOnlyList<double> ComputeUnadjustedRunScales(
        ProductionPlan plan,
        MasterDataSnapshot snapshot)
    {
        var pairByRecipe = plan.PairSelections
            .GroupBy(s => s.RecipeId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Pair, StringComparer.Ordinal);
        Dictionary<string, double> facilityScales = ComputeFacilityScales(plan, snapshot, pairByRecipe);

        var scales = new double[plan.RecipeRuns.Count];
        for (int i = 0; i < scales.Length; i++)
        {
            scales[i] = facilityScales.GetValueOrDefault(plan.RecipeRuns[i].FacilityId, 1.0);
        }

        // 環境を要するランの未調整稼働は「散布機台数 × CoverableMachines」の機械数上限で
        // さらに絞る（仕様決定 BR、implementation-plan-phase26 §3.4）。配分は 2 段:
        // 先に全環境ランの調整済み機械数を保留し、残量を計画の RunOrder 順に配る。
        var envReqById = plan.EnvironmentRequirements
            .GroupBy(e => e.EnvironmentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var envRunIndexes = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int i = 0; i < plan.RecipeRuns.Count; i++)
        {
            if (pairByRecipe.TryGetValue(plan.RecipeRuns[i].RecipeId, out RecipeFacility? pair)
                && pair.EnvironmentId is string envId)
            {
                if (!envRunIndexes.TryGetValue(envId, out List<int>? indexes))
                {
                    indexes = [];
                    envRunIndexes[envId] = indexes;
                }
                indexes.Add(i);
            }
        }

        foreach ((string envId, List<int> indexes) in envRunIndexes)
        {
            if (!envReqById.TryGetValue(envId, out EnvironmentRequirement? envReq)
                || !snapshot.EnvironmentsById.TryGetValue(envId, out Environment? env))
            {
                continue;
            }

            double cap = envReq.DispenserCount * (double)env.CoverableMachines;
            var machines = new double[indexes.Count];
            for (int j = 0; j < indexes.Count; j++)
            {
                RecipeRun run = plan.RecipeRuns[indexes[j]];
                machines[j] = run.CyclesPerMinute * pairByRecipe[run.RecipeId].CycleTime / 60.0;
            }

            double remaining = Math.Max(0.0, cap - machines.Sum());
            for (int j = 0; j < indexes.Count; j++)
            {
                if (machines[j] <= Epsilon)
                {
                    scales[indexes[j]] = 0.0;
                    continue;
                }

                // 調整済み機械数を超える全速化分を残りカバー容量から配分する。
                double desired = machines[j] * scales[indexes[j]];
                double grant = Math.Min(desired - machines[j], remaining);
                remaining -= grant;
                scales[indexes[j]] = (machines[j] + grant) / machines[j];
            }
        }

        return scales;
    }

    /// <summary>
    /// 未調整ビューの余剰再計算。FlowGraphModelBuilder が共用する。
    /// runScales は <see cref="ComputeUnadjustedRunScales"/> の戻り値（RecipeRuns 順）。
    /// </summary>
    internal static List<SurplusProduction> ComputeUnadjustedSurpluses(
        ProductionPlan plan,
        MasterDataSnapshot snapshot,
        ContextFilter context,
        IReadOnlyList<double> runScales)
    {
        return RecomputeSurpluses(plan, snapshot, context, runScales);
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
    /// 未調整の余剰を再計算する。produced' = Σ run(cycles × runScale × 出力個数)。
    /// イベント不可アイテムは全量が余剰（仕様決定 X を調整済と同じ規則で適用）。
    /// </summary>
    private static List<SurplusProduction> RecomputeSurpluses(
        ProductionPlan plan,
        MasterDataSnapshot snapshot,
        ContextFilter context,
        IReadOnlyList<double> runScales)
    {
        var produced = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < plan.RecipeRuns.Count; i++)
        {
            RecipeRun run = plan.RecipeRuns[i];
            if (!snapshot.RecipesById.TryGetValue(run.RecipeId, out Recipe? recipe))
            {
                continue;
            }

            double scale = i < runScales.Count ? runScales[i] : 1.0;
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
