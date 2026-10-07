using EndfieldAicWeb.Domain.Models;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Domain.Calculation;

/// <summary>
/// <see cref="CalculationSession"/> の帳簿から <see cref="ProductionPlan"/> を組み立てる。
/// <see cref="ProductionCalculator"/> 専用の内部実装。
/// </summary>
internal static class ProductionPlanAggregator
{
    internal static ProductionPlan Aggregate(MasterDataSnapshot master, CalculationSession session)
    {
        var itemRequirements = new List<ItemRequirement>();
        foreach (string itemId in session.DemandOrder)
        {
            // 引き戻しで需要が帳簿上 0 になったアイテムは要求行として出さない。
            if (session.Demand[itemId] <= ProductionCalculator.Epsilon)
            {
                continue;
            }

            var supplies = new List<SupplyPortion>();
            PairSelector.Selection? selected = session.Selection.GetValueOrDefault(itemId);
            // イベント不可アイテムは生産量を供給として表示しない（仕様決定 X）。
            bool itemInactive = session.IsItemInactive(itemId);
            foreach (PairSelector.Selection run in session.RunOrder)
            {
                if (itemInactive)
                {
                    break;
                }

                double outputQty = run.Recipe.Outputs
                    .Where(o => o.ItemId == itemId)
                    .Sum(o => o.Quantity);
                if (outputQty <= 0)
                {
                    continue;
                }

                double portion = session.RunCycles[run] * outputQty;
                if (portion <= ProductionCalculator.Epsilon)
                {
                    continue;
                }

                SupplyKind kind = ReferenceEquals(selected, run) ? SupplyKind.Recipe : SupplyKind.Byproduct;
                supplies.Add(new SupplyPortion(kind, run.Recipe.Id, portion));
            }

            double raw = itemInactive ? 0 : ProductionCalculator.GetOrZero(session.Raw, itemId);
            if (raw > ProductionCalculator.Epsilon)
            {
                // 基礎素材指定の Raw は外部調達として別種別にする（仕様決定 DB）。
                SupplyKind kind = session.SpecifiedBaseItemIds.Contains(itemId)
                    ? SupplyKind.ExternalProcurement
                    : SupplyKind.Gathered;
                supplies.Add(new SupplyPortion(kind, null, raw));
            }

            itemRequirements.Add(new ItemRequirement(
                itemId,
                session.Demand[itemId],
                supplies,
                ProductionCalculator.GetOrZero(session.Unmet, itemId)));
        }

        FacilityCounts counts = session.ComputeCounts();
        var exactByFacility = new Dictionary<string, double>(counts.ExactByFacility, StringComparer.Ordinal);
        var environmentRequirements = new List<EnvironmentRequirement>();
        foreach ((string envId, int dispenserCount) in counts.DispenserCountByEnv)
        {
            if (!master.EnvironmentsById.TryGetValue(envId, out Environment? env))
            {
                continue;
            }

            environmentRequirements.Add(new EnvironmentRequirement(
                envId,
                env.ProviderFacilityId,
                dispenserCount,
                env.ConsumeItemId,
                env.ConsumeRatePerMinute * dispenserCount,
                counts.RequiredCountByEnv.GetValueOrDefault(envId),
                counts.UsedMachinesByEnv.GetValueOrDefault(envId)));

            // 散布機は設備要件・消費電力に計上する（実数=切上げの指定台数）。
            exactByFacility[env.ProviderFacilityId] =
                exactByFacility.GetValueOrDefault(env.ProviderFacilityId) + dispenserCount;
        }

        var facilityRequirements = exactByFacility
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new FacilityRequirement(kv.Key, kv.Value, ProductionCalculator.Ceil(kv.Value)))
            .ToList();

        var recipeRuns = session.RunOrder
            .Select(r => new RecipeRun(r.Recipe.Id, r.Pair.FacilityId, session.RunCycles[r], r.Pair))
            .ToList();

        // 確定ペアは実際に稼働中（RunOrder に残る）のものだけを出す。
        // 引き戻しで休眠したペアは Selection に残るが出力しない。
        var pairSelections = session.Selection
            .Where(kv => kv.Value is not null && session.RunOrder.Contains(kv.Value))
            .Select(kv => new PairSelection(kv.Key, kv.Value!.Recipe.Id, kv.Value.Pair))
            .ToList();

        double totalPower = facilityRequirements
            .Sum(f => master.FacilitiesById.TryGetValue(f.FacilityId, out Facility? facility)
                ? facility.PowerConsumption * f.CeilCount
                : 0);

        var surpluses = new List<SurplusProduction>();
        foreach ((string itemId, double produced) in session.Produced)
        {
            // イベント不可アイテムの生産量は需要へ充当できないため、全量が余剰（仕様決定 X）。
            double excess = session.IsItemInactive(itemId)
                ? produced
                : produced - ProductionCalculator.GetOrZero(session.Demand, itemId);
            if (excess > ProductionCalculator.Epsilon)
            {
                surpluses.Add(new SurplusProduction(itemId, excess));
            }
        }

        var flowAdjustments = BuildFlowAdjustments(session);

        WarningBag warnings = session.Warnings;

        return new ProductionPlan
        {
            ItemRequirements = itemRequirements,
            FacilityRequirements = facilityRequirements,
            RecipeRuns = recipeRuns,
            PairSelections = pairSelections,
            EnvironmentRequirements = environmentRequirements,
            TotalPowerConsumption = totalPower,
            Surpluses = surpluses,
            FlowAdjustments = flowAdjustments,
            Warnings = warnings.AsList(),
        };
    }

    /// <summary>
    /// 自身の使用台数に端数（設備の一部余力）があるレシピの入力について推奨流量制限を出力する。
    /// 判定はランごとの使用台数で行う。設備を共用する場合、0.5 台ずつの使用でも各レシピに
    /// 制限を出し、整数台の全速稼働（余力なし）には出さない。
    /// </summary>
    internal static List<FlowAdjustment> BuildFlowAdjustments(CalculationSession session)
    {
        var adjustments = new Dictionary<(string RecipeId, string InputItemId), double>();

        foreach (PairSelector.Selection run in session.RunOrder)
        {
            // 出力なしレシピのラン（処理ラン）に推奨流量制限は意味を持たない（CC）。
            if (run.Recipe.Outputs.Count == 0)
            {
                continue;
            }

            double machines = session.RunCycles[run] * run.Pair.CycleTime / 60.0;
            if (ProductionCalculator.Ceil(machines) <= machines + ProductionCalculator.Epsilon)
            {
                continue;
            }

            foreach (RecipeInput input in run.Recipe.Inputs)
            {
                var key = (run.Recipe.Id, input.ItemId);
                adjustments[key] = adjustments.GetValueOrDefault(key)
                    + session.RunCycles[run] * input.Quantity;
            }
        }

        return adjustments
            .Where(kv => kv.Value > ProductionCalculator.Epsilon)
            .Select(kv => new FlowAdjustment(kv.Key.RecipeId, kv.Key.InputItemId, kv.Value, kv.Value))
            .ToList();
    }
}

/// <summary>設備ごとの実数台数と環境ごとの散布機台数・必要台数（自動見積もり）・利用機械数。</summary>
internal sealed record FacilityCounts(
    Dictionary<string, double> ExactByFacility,
    Dictionary<string, int> DispenserCountByEnv,
    Dictionary<string, int> RequiredCountByEnv,
    Dictionary<string, double> UsedMachinesByEnv);
