using EndfieldAicWeb.Domain.Calculation;

namespace EndfieldAicWeb.Application;

/// <summary>
/// 計算結果の既定ビュー規則（仕様決定 O・I の UI 既定）。
/// </summary>
public static class PlanViewDefaults
{
    /// <summary>
    /// 未調整表示を既定とするか。全設備が整数台数（切上げ過剰なし）の計画では
    /// 両ビューの見え方が同じため未調整を既定とする。切上げ過剰が出る計画では
    /// 調整済が既定になる（仕様決定 O の UI 既定）。
    /// </summary>
    public static bool DefaultUnadjusted(ProductionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.FacilityRequirements.All(f => f.ExactCount == f.CeilCount);
    }

    /// <summary>
    /// 環境ごとの散布機台数入力の上限。環境要件が持つ必要台数（機械数からの
    /// 自動見積もり、仕様決定 BQ・BR）を返す。
    /// 上書き後の台数ではなく需要基準のため、0 に下げても元の台数へ戻せる。
    /// </summary>
    public static int DispenserLimit(ProductionPlan plan, string environmentId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.EnvironmentRequirements
            .FirstOrDefault(e => e.EnvironmentId == environmentId)
            ?.RequiredDispenserCount ?? 0;
    }
}
