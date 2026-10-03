using EndfieldAicWeb.Domain.Calculation;

namespace EndfieldAicWeb.Application;

/// <summary>
/// 計算結果の既定ビュー規則（仕様決定 O・I・BS の UI 既定）。
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
    /// 環境ごとの散布機台数入力の範囲（仕様決定 BS）。
    /// 下限は環境要件が持つ必要台数（機械数からの自動見積もり、仕様決定 BQ・BR）、
    /// 上限はその環境を利用する機械数合計の切上げ。計画に登場しない環境は (0, 0)。
    /// 上書き後の台数ではなく需要基準のため、入力を変えても範囲は変わらない。
    /// </summary>
    public static (int Min, int Max) DispenserRange(ProductionPlan plan, string environmentId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        EnvironmentRequirement? env = plan.EnvironmentRequirements
            .FirstOrDefault(e => e.EnvironmentId == environmentId);
        return env is null
            ? (0, 0)
            : (env.RequiredDispenserCount, (int)Math.Ceiling(env.UsedMachineCount - Epsilon));
    }

    /// <summary>切上げ時に引く端数誤差分（Domain の ProductionCalculator.Ceil と同じ扱い）。</summary>
    private const double Epsilon = 1e-9;
}
