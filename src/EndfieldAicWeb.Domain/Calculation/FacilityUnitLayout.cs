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
    /// 同一ランの機械群。容量判定はラン順に機械容量 1.0 の仮想機械へ充填して行う
    /// （MaxMachineInputs 参照。合算流量を Used で平均化すると、別ランの高流量機械の
    /// 超過が隠れる）。
    /// </summary>
    public List<FacilityUnitOverspill> OverspillGroups { get; } = new();
}

/// <summary>
/// 末尾ユニットへ集約された同一ランの機械群。Machines は溢れた機械数（端数含む）、
/// SharePortion は RunShares に含まれる集約分の比率（部分占有分との切り分け用）。
/// PerMachineInputs は機械群 1 台あたりのレシピ入力（アイテム Id → 個/分）。
/// 端数機は次のランの端数機と同一機械を共用しうるため、占有率の換算は評価側で行う。
/// </summary>
public sealed record FacilityUnitOverspill(
    int RunIndex,
    double Machines,
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
    /// 部分占有機械の入力と「OverspillGroups をラン順に仮想機械へ充填した列」の各機械
    /// 入力の最大を取る（MaxMachineInputs 参照）。台数分表示のノード数は実台数を下回る
    /// 点だけ実台数とずれる（表示上の近似）。
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
                    // 機械群は満機レートで記録し、端数機の占有率換算と共用は評価側
                    // （MaxMachineInputs の仮想機械充填）で行う。
                    double perMachineCycles = run.CyclesPerMinute * scale / machines;
                    last.OverspillGroups.Add(new FacilityUnitOverspill(
                        runIndex,
                        remaining,
                        sharePortion,
                        recipe.Inputs.ToDictionary(
                            i => i.ItemId,
                            i => i.Quantity * perMachineCycles,
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
    /// 末尾集約スロットは「部分占有機械の入力合計」と「集約機械群をラン順に容量 1.0 の
    /// 仮想機械へ充填した列」の各機械入力の最大を取る（端数機は次のランの端数機と
    /// 同一機械を共用しうる。輸送容量は機械単位で判定するため、群ごとの独立評価や
    /// 平均化では共用機械の合算超過や高流量機械の超過が抜ける）。同一ランの整数機械は
    /// 全て同じ入力なので列挙せず代表 1 台だけ評価する。
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

        // 集約分はラン順に機械容量 1.0 の仮想機械へ逐次充填する。混在は境界の機械だけ
        // 起きるため、開いている機械・整数分の純粋機械・末尾の端数機を順に評価すれば
        // 機械を列挙しなくても最大入力が求まる。
        var openMachine = new Dictionary<string, double>(StringComparer.Ordinal);
        double openFree = 1.0;
        bool hasOpenMachine = false;
        foreach (FacilityUnitOverspill group in unit.OverspillGroups)
        {
            double left = group.Machines;

            // 直前のランの端数機へ続けて充填する。
            if (hasOpenMachine && left > ProductionCalculator.Epsilon)
            {
                double pour = Math.Min(left, openFree);
                AddInputs(openMachine, group.PerMachineInputs, pour);
                left -= pour;
                openFree -= pour;
                if (openFree <= ProductionCalculator.Epsilon)
                {
                    MergeMaxInputs(inputs, openMachine);
                    openMachine.Clear();
                    openFree = 1.0;
                    hasOpenMachine = false;
                }
            }

            // 整数分は全て同一の純粋機械なので、代表として 1 台ぶんの入力を評価する。
            if (left >= 1.0 - ProductionCalculator.Epsilon)
            {
                MergeMaxInputs(inputs, group.PerMachineInputs);
                left -= Math.Floor(left);
            }

            // 末尾の端数機は次のランと共用されうる開いた機械として残す。
            if (left > ProductionCalculator.Epsilon)
            {
                hasOpenMachine = true;
                AddInputs(openMachine, group.PerMachineInputs, left);
                openFree = 1.0 - left;
            }
        }
        if (hasOpenMachine)
        {
            MergeMaxInputs(inputs, openMachine);
        }

        return inputs;
    }

    private static void AddInputs(
        Dictionary<string, double> machine,
        IReadOnlyDictionary<string, double> perMachineInputs,
        double occupancy)
    {
        foreach ((string itemId, double rate) in perMachineInputs)
        {
            machine[itemId] = machine.GetValueOrDefault(itemId) + rate * occupancy;
        }
    }

    private static void MergeMaxInputs(
        Dictionary<string, double> inputs,
        IReadOnlyDictionary<string, double> machineInputs)
    {
        foreach ((string itemId, double rate) in machineInputs)
        {
            if (rate > inputs.GetValueOrDefault(itemId))
            {
                inputs[itemId] = rate;
            }
        }
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
