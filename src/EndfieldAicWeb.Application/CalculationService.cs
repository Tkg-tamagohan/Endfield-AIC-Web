using EndfieldAicWeb.Domain.Calculation;

namespace EndfieldAicWeb.Application;

/// <summary>
/// 計算結果と、需要アイテムごとのペア候補の組。
/// </summary>
public sealed record CalculationOutcome(
    ProductionPlan Plan,
    IReadOnlyDictionary<string, IReadOnlyList<PairOption>> PairOptionsByItemId);

/// <summary>
/// 計算アプリのユースケース入口。計算の実行とペア候補列挙を 1 回で返す。
/// </summary>
public sealed class CalculationService
{
    public CalculationOutcome Calculate(
        MasterDataSnapshot snapshot,
        IReadOnlyList<ProductionTarget> targets,
        ContextFilter context,
        IReadOnlyList<PairOverride> pairOverrides,
        IReadOnlyList<EnvironmentCountOverride> environmentOverrides)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pairOverrides);
        ArgumentNullException.ThrowIfNull(environmentOverrides);

        ProductionPlan plan = new ProductionCalculator().Calculate(
            snapshot, targets, context, pairOverrides, environmentOverrides);

        var options = new Dictionary<string, IReadOnlyList<PairOption>>(StringComparer.Ordinal);
        foreach (ItemRequirement requirement in plan.ItemRequirements)
        {
            List<PairOption> list = PairSelector
                .ListCandidates(requirement.ItemId, snapshot, context)
                .Select(c => new PairOption(
                    PairOptionKey.Create(c.Recipe, c.Pair),
                    c.Recipe.Id,
                    c.Pair.FacilityId,
                    c.Pair.CycleTime,
                    c.Pair.EnvironmentId,
                    c.Pair.FixedConsumption,
                    c.IsDefault))
                .ToList();
            if (list.Count > 0)
            {
                options[requirement.ItemId] = list;
            }
        }

        return new CalculationOutcome(plan, options);
    }
}
