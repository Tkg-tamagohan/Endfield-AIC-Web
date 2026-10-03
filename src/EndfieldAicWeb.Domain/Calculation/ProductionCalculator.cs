using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;

namespace EndfieldAicWeb.Domain.Calculation;

/// <summary>
/// 生産計画の計算（需要展開・副産物充当・設備台数・環境計上・固定消費・流量調整）を行う。
/// 骨格は旧 ProductionCalculator の Session（展開→引き戻しの固定点反復）を移植したもので、
/// ペア選択（F/U）・環境計上（I）・固定消費（J/V）・イベント不可扱い（T/X）・収束反復を含む。
/// 公開 API と共有の定数・小ヘルパーのみを持ち、計算状態は <see cref="CalculationSession"/>、
/// 結果の組み立ては <see cref="ProductionPlanAggregator"/>、採取上限の解決は <see cref="GatherCapResolver"/> が担う。
/// </summary>
public static class ProductionCalculator
{
    internal const double Epsilon = 1e-9;

    /// <summary>環境消費・固定消費の追加需要が収束するまでの反復上限（docs/implementation-plan.md §3-8）。</summary>
    internal const int MaxConvergenceIterations = 10;

    /// <summary>生産計画を計算する。</summary>
    public static ProductionPlan Calculate(
        MasterDataSnapshot master,
        IReadOnlyList<ProductionTarget> targets,
        ContextFilter context,
        IReadOnlyList<PairOverride> overrides,
        IReadOnlyList<EnvironmentCountOverride> environmentOverrides,
        IReadOnlyList<GatherRateOverride> gatherOverrides)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(environmentOverrides);
        ArgumentNullException.ThrowIfNull(gatherOverrides);

        var display = new EntityDisplay(master);
        foreach (ProductionTarget target in targets)
        {
            if (target.RatePerMinute <= 0 || !double.IsFinite(target.RatePerMinute))
            {
                throw new ArgumentException(
                    $"目標の RatePerMinute は正の有限値である必要があります: {display.Item(target.ItemId)} = {target.RatePerMinute}");
            }

            if (!master.ItemsById.ContainsKey(target.ItemId))
            {
                throw new ArgumentException($"目標のアイテムがマスタに存在しません: {display.Item(target.ItemId)}");
            }
        }

        var session = new CalculationSession(master, context, overrides, environmentOverrides, gatherOverrides);
        session.Run(targets);
        return ProductionPlanAggregator.Aggregate(master, session);
    }

    /// <summary>帳簿の端数を含む実数値の切上げ（誤差分を引いてから天井を取る）。</summary>
    internal static int Ceil(double value) => (int)Math.Ceiling(value - Epsilon);

    /// <summary>個数帳簿からの読み出し。未登録のキーは 0 とみなす。</summary>
    internal static double GetOrZero(Dictionary<string, double> map, string key) =>
        map.TryGetValue(key, out double value) ? value : 0;
}
