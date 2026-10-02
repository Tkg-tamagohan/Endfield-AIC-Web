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
/// </summary>
public static class FacilityUnitLayout
{
    public static Dictionary<string, List<FacilityUnitSlot>> Allocate(
        IReadOnlyList<RecipeRun> recipeRuns,
        IReadOnlyList<FacilityRequirement> facilityRequirements,
        IReadOnlyList<EnvironmentRequirement> environmentRequirements,
        MasterDataSnapshot snapshot,
        IReadOnlyList<PairSelection> pairSelections)
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

            var units = new List<FacilityUnitSlot>(f.CeilCount);
            for (int i = 0; i < f.CeilCount; i++)
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

            double machines = RunMachines(run, snapshot, pairByRun);
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
