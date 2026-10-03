using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Domain.Calculation;

/// <summary>
/// 設備 1 ユニット分の占有状態。台数分表示と輸送容量超過判定で共用する。
/// Used はラン占有の合計（ユニット容量 1.0 基準）、RunShares はラン index → そのランの
/// 機械数に対する占有比率を持つ。
/// </summary>
public sealed class FacilityUnitSlot
{
    public required int Index { get; init; }
    public double Used { get; internal set; }
    public bool IsDispenser { get; internal set; }

    /// <summary>散布機ユニットの場合、担当する環境の Id。</summary>
    public string? DispenserEnvironmentId { get; internal set; }

    public Dictionary<int, double> RunShares { get; } = new();
}

/// <summary>
/// 設備を切上台数ぶんのユニットへ展開し、ランの占有を割り当てる（仕様決定 AO）。
/// ランの機械数（CyclesPerMinute × CycleTime / 60）をユニット容量 1.0 へ RecipeRuns 順に
/// 逐次充填し、ラン占有ユニットの後ろを環境ごとの散布機ユニットとする。
/// runScales を渡すとラン index ごとの倍率を機械数へ掛けて割り当てる
/// （未調整ビュー: 実機械が全速稼働する想定の配置。環境ランはカバー配分で
/// 個別倍率を持ちうるため設備単位ではなくラン単位、仕様決定 BR）。
/// </summary>
public static class FacilityUnitLayout
{
    /// <summary>
    /// ユニットへ実体化する台数の防御的上限。発散した計画（ConvergenceNotReached 付きで
    /// 集計へ進むもの）では台数が int 規模の巨大な実数になり、ユニットごとのスロット
    /// 生成でメモリ・時間を使い果たすため、先頭 MaxUnitSlots 個だけを実体化する。
    /// 上限を超えた分は既存の「収まらない分は末尾ユニットへ載せる」規則で末尾スロットに
    /// 集約され、そのスロットは Used&gt;1 の「複数機を背負うユニット」となる。
    /// 容量系の判定（輸送警告・グラフの容量超過フラグ）はラン由来の入力を Used で割って
    /// 機械あたりへ換算するので、集約された流量を 1 機の入力と誤判定しない
    /// （複数ランが混ざる末尾スロットでは平均化の近似となる）。台数分表示のノード数は
    /// 実台数を下回る点だけ実台数とずれる（表示上の近似）。
    /// </summary>
    internal const int MaxUnitSlots = 10_000;

    public static Dictionary<string, List<FacilityUnitSlot>> Allocate(
        IReadOnlyList<RecipeRun> recipeRuns,
        IReadOnlyList<FacilityRequirement> facilityRequirements,
        IReadOnlyList<EnvironmentRequirement> environmentRequirements,
        MasterDataSnapshot snapshot,
        IReadOnlyList<PairSelection> pairSelections,
        IReadOnlyList<double>? runScales = null)
    {
        ArgumentNullException.ThrowIfNull(recipeRuns);
        ArgumentNullException.ThrowIfNull(facilityRequirements);
        ArgumentNullException.ThrowIfNull(environmentRequirements);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(pairSelections);

        var pairByRun = pairSelections
            .GroupBy(s => (s.RecipeId, s.Pair.FacilityId))
            .ToDictionary(g => g.Key, g => g.First().Pair);

        var unitsByFacility = new Dictionary<string, List<FacilityUnitSlot>>(StringComparer.Ordinal);
        foreach (FacilityRequirement f in facilityRequirements)
        {
            if (f.CeilCount < 1)
            {
                continue;
            }

            int emitCount = Math.Min(f.CeilCount, MaxUnitSlots);
            var units = new List<FacilityUnitSlot>(emitCount);
            for (int i = 0; i < emitCount; i++)
            {
                units.Add(new FacilityUnitSlot { Index = i });
            }
            unitsByFacility[f.FacilityId] = units;
        }

        for (int runIndex = 0; runIndex < recipeRuns.Count; runIndex++)
        {
            RecipeRun run = recipeRuns[runIndex];
            if (!unitsByFacility.TryGetValue(run.FacilityId, out List<FacilityUnitSlot>? units))
            {
                continue;
            }

            double machines = RunMachines(run, snapshot, pairByRun)
                * (runScales is not null && runIndex < runScales.Count ? runScales[runIndex] : 1.0);
            if (machines <= ProductionCalculator.Epsilon)
            {
                continue;
            }

            double remaining = machines;
            foreach (FacilityUnitSlot unit in units)
            {
                if (remaining <= ProductionCalculator.Epsilon)
                {
                    break;
                }

                double free = 1.0 - unit.Used;
                if (free <= ProductionCalculator.Epsilon)
                {
                    continue;
                }

                double take = Math.Min(remaining, free);
                unit.RunShares[runIndex] = take / machines;
                unit.Used += take;
                remaining -= take;
            }

            // 計画不整合でユニットに収まらない分は末尾ユニットに載せる。
            if (remaining > ProductionCalculator.Epsilon)
            {
                FacilityUnitSlot last = units[^1];
                last.RunShares[runIndex] =
                    last.RunShares.GetValueOrDefault(runIndex) + remaining / machines;
                last.Used += remaining;
            }
        }

        // 散布機ユニットはラン占有ユニットの後ろへ、環境の順に振り分ける。
        var nextDispenser = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (EnvironmentRequirement env in environmentRequirements)
        {
            if (!unitsByFacility.TryGetValue(env.ProviderFacilityId, out List<FacilityUnitSlot>? units))
            {
                continue;
            }

            int start = nextDispenser.GetValueOrDefault(
                env.ProviderFacilityId, units.Count(u => u.Used > ProductionCalculator.Epsilon));
            for (int j = 0; j < env.DispenserCount && start + j < units.Count; j++)
            {
                units[start + j].IsDispenser = true;
                units[start + j].DispenserEnvironmentId = env.EnvironmentId;
            }
            nextDispenser[env.ProviderFacilityId] = start + env.DispenserCount;
        }

        return unitsByFacility;
    }

    /// <summary>ランの占有機械数（CyclesPerMinute × CycleTime / 60）。確定ペア優先で CycleTime を引く。</summary>
    private static double RunMachines(
        RecipeRun run,
        MasterDataSnapshot snapshot,
        IReadOnlyDictionary<(string RecipeId, string FacilityId), RecipeFacility> pairByRun)
    {
        RecipeFacility? pair = pairByRun.GetValueOrDefault((run.RecipeId, run.FacilityId));
        if (pair is null && snapshot.RecipesById.TryGetValue(run.RecipeId, out Recipe? recipe))
        {
            pair = recipe.Facilities.FirstOrDefault(p => p.FacilityId == run.FacilityId);
        }
        return pair is null ? 0 : run.CyclesPerMinute * pair.CycleTime / 60.0;
    }
}
