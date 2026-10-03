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

    /// <summary>
    /// ユニット実体化上限や計画不整合で末尾へ集約された専用機械群。各エントリは
    /// 同一ランの機械群。容量判定は部分占有機械と各群の 1 機あたり入力の最大で行う
    /// （合算流量を Used で平均化すると、別ランの高流量機械の超過が隠れる）。
    /// </summary>
    public List<FacilityUnitOverspill> OverspillGroups { get; } = new();
}

/// <summary>
/// 末尾ユニットへ集約された同一ランの機械群。SharePortion は RunShares に含まれる
/// 集約分の比率（部分占有分との切り分け用）。PerMachineInputs は機械群 1 台あたりの
/// レシピ入力（アイテム Id → 個/分）。
/// </summary>
public sealed record FacilityUnitOverspill(
    int RunIndex,
    double SharePortion,
    IReadOnlyDictionary<string, double> PerMachineInputs);

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
    /// 容量系の判定（輸送警告・グラフの容量超過フラグ）は集約流量を 1 機の入力とせず、
    /// 部分占有機械の入力と集約機械群ごとの 1 機あたり入力（OverspillGroups）の最大を
    /// 取る（MaxMachineInputs 参照）。台数分表示のノード数は実台数を下回る点だけ
    /// 実台数とずれる（表示上の近似）。
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

            double scale = runScales is not null && runIndex < runScales.Count
                ? runScales[runIndex]
                : 1.0;
            double machines = RunMachines(run, snapshot, pairByRun) * scale;
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

            // 計画不整合やユニット実体化上限で収まらない分は末尾ユニットに載せる。
            // 容量判定が機械群ごとの最大を取れるよう、群の 1 機あたり入力を記録する
            // （機械あたりの流量は scale を掛けても不変＝ cycles × scale / machines）。
            if (remaining > ProductionCalculator.Epsilon)
            {
                FacilityUnitSlot last = units[^1];
                double sharePortion = remaining / machines;
                last.RunShares[runIndex] =
                    last.RunShares.GetValueOrDefault(runIndex) + sharePortion;
                last.Used += remaining;
                if (snapshot.RecipesById.TryGetValue(run.RecipeId, out Recipe? recipe))
                {
                    // 1 機以上の溢れは満機レート、1 機未満は端数機の占有率ぶん。
                    double occupancy = Math.Min(remaining, 1.0);
                    double perMachineCycles = run.CyclesPerMinute * scale / machines;
                    last.OverspillGroups.Add(new FacilityUnitOverspill(
                        runIndex,
                        sharePortion,
                        recipe.Inputs.ToDictionary(
                            i => i.ItemId,
                            i => i.Quantity * perMachineCycles * occupancy,
                            StringComparer.Ordinal)));
                }
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

    /// <summary>
    /// ユニットが背負う機械のうち最も厳しい 1 台のレシピ入力（アイテム Id → 個/分）。
    /// 部分占有のみのユニットは占有比率に比例した入力合計をそのまま返す。
    /// 末尾集約スロットは「部分占有機械の入力合計」と「各専用機械群の 1 機あたり入力」の
    /// 最大を取る（異なるランの機械は同一機械へ合算しない。輸送容量は機械単位で判定する
    /// ため、平均化すると別ランの高流量機械の超過が隠れる）。
    /// 固定消費・環境消費のような機械ごとの定数は含まない。runScales は Allocate と
    /// 同じものを渡す（未調整ビューではエッジ流量・機械数とも倍率を掛けた値になる）。
    /// </summary>
    public static Dictionary<string, double> MaxMachineInputs(
        FacilityUnitSlot unit,
        IReadOnlyList<RecipeRun> recipeRuns,
        MasterDataSnapshot snapshot,
        IReadOnlyList<double>? runScales = null)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(recipeRuns);
        ArgumentNullException.ThrowIfNull(snapshot);

        var inputs = new Dictionary<string, double>(StringComparer.Ordinal);
        var pileShares = new Dictionary<int, double>();
        foreach (FacilityUnitOverspill group in unit.OverspillGroups)
        {
            pileShares[group.RunIndex] =
                pileShares.GetValueOrDefault(group.RunIndex) + group.SharePortion;
        }

        // 部分占有機械: 各ランの占有比率（集約分を除く）に比例した入力の合計。
        foreach ((int runIndex, double share) in unit.RunShares)
        {
            double partialShare = share - pileShares.GetValueOrDefault(runIndex);
            if (partialShare <= ProductionCalculator.Epsilon)
            {
                continue;
            }

            RecipeRun run = recipeRuns[runIndex];
            if (!snapshot.RecipesById.TryGetValue(run.RecipeId, out Recipe? recipe))
            {
                continue;
            }

            double scale = runScales is not null && runIndex < runScales.Count
                ? runScales[runIndex]
                : 1.0;
            foreach (RecipeInput input in recipe.Inputs)
            {
                inputs[input.ItemId] = inputs.GetValueOrDefault(input.ItemId)
                    + run.CyclesPerMinute * scale * input.Quantity * partialShare;
            }
        }

        // 集約された専用機械群は 1 機あたりの入力を群単位で評価して最大を取る。
        foreach (FacilityUnitOverspill group in unit.OverspillGroups)
        {
            foreach ((string itemId, double rate) in group.PerMachineInputs)
            {
                if (rate > inputs.GetValueOrDefault(itemId))
                {
                    inputs[itemId] = rate;
                }
            }
        }

        return inputs;
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
