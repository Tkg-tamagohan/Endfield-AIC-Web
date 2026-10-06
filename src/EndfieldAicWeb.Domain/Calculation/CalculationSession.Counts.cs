using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Domain.Calculation;

// 環境カバーと台数・追加需要（EnvUsedMachines、EffectiveBlockedMachines、ComputeCounts、ComputeExtraDemand）。
internal sealed partial class CalculationSession
{
    /// <summary>
    /// 散布機台数の上書きが決める環境ごとの機械数上限（台数 × CoverableMachines、仕様決定 BR）。
    /// 上書きのない環境は自動台数が需要を常に満たすため上限を持たない。
    /// 負数・未知環境の上書きは ComputeCounts 側の警告と同じく既定台数扱いで上限を持たない。
    /// </summary>
    private readonly Dictionary<string, double> _envCaps = new(StringComparer.Ordinal);

    /// <summary>
    /// （環境 Id, アイテム Id）ごとのカバー不足削減記録。未充足寄与量と
    /// 機械数換算係数（CycleTime / 60 / 出力量）を持つ（仕様決定 BR）。
    /// </summary>
    private readonly Dictionary<(string EnvId, string ItemId), EnvBlockedPortion> _envBlockedUnmet = new();

    /// <summary>Unmet への全加算の合算（全原因の寄与）。削減記録の残存率計算に使う（BR）。</summary>
    private readonly Dictionary<string, double> _unmetAdded = new(StringComparer.Ordinal);

    /// <summary>環境の使用済み機械数（同環境を要する稼働中ランの機械数合算）。占有順は展開順。</summary>
    private double EnvUsedMachines(string envId)
    {
        double used = 0.0;
        foreach (PairSelector.Selection run in RunOrder)
        {
            if (run.Pair.EnvironmentId == envId)
            {
                used += RunCycles[run] * run.Pair.CycleTime / 60.0;
            }
        }
        return used;
    }

    /// <summary>
    /// 環境ごとの有効削減機械数。カバー不足で記録した未充足寄与を残存 Unmet へ
    /// 比例配分して機械数へ換算する（仕様決定 BR、implementation-plan-phase26 §3.3）。
    /// </summary>
    private Dictionary<string, double> EffectiveBlockedMachines()
    {
        var blocked = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (((string envId, string itemId), EnvBlockedPortion portion) in _envBlockedUnmet)
        {
            double added = _unmetAdded.GetValueOrDefault(itemId);
            double share = added <= ProductionCalculator.Epsilon
                ? 0.0
                : Math.Min(1.0, ProductionCalculator.GetOrZero(Unmet, itemId) / added);
            blocked[envId] = blocked.GetValueOrDefault(envId)
                + portion.UnmetAmount * share * portion.MachinesPerUnmet;
        }
        return blocked;
    }

    /// <summary>カバー不足で削った未充足寄与 1 件（アイテム量と機械数換算係数）。</summary>
    private sealed record EnvBlockedPortion(double UnmetAmount, double MachinesPerUnmet)
    {
        public EnvBlockedPortion Add(double amount) =>
            this with { UnmetAmount = UnmetAmount + amount };
    }

    /// <summary>
    /// 確定ペアから設備ごとの実数台数と、必要となった環境の一覧を求める。
    /// 収束反復と最終集計の双方で同じ定義を使う。
    /// </summary>
    internal FacilityCounts ComputeCounts()
    {
        var exactByFacility = new Dictionary<string, double>(StringComparer.Ordinal);
        var envMachines = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (PairSelector.Selection run in RunOrder)
        {
            double facilityTime = RunCycles[run] * run.Pair.CycleTime / 60.0;
            exactByFacility[run.Pair.FacilityId] =
                exactByFacility.GetValueOrDefault(run.Pair.FacilityId) + facilityTime;

            if (run.Pair.EnvironmentId is string envId)
            {
                envMachines[envId] = envMachines.GetValueOrDefault(envId) + facilityTime;
            }
        }

        // カバー不足で削られた機械分。残存 Unmet への比例配分が有効削減機械数（BR、§3.3）。
        Dictionary<string, double> blockedMachinesByEnv = EffectiveBlockedMachines();

        // 散布機台数: 既定は同環境を要する稼働機械数と有効削減機械数の合計を
        // CoverableMachines で割った切上げ（仕様決定 BQ。I の「レシピにつき 1 台」の改定）。
        // その合計は利用機械数として環境要件へ残し、UI の入力上限の算定に使う（仕様決定 BS）。
        // ユーザー上書きを優先する。計算に登場しない環境への上書きは無視する。
        // 環境行は稼働中ランの環境に加え、カバー不足で稼働が停止した要求を持つ環境も出す（§3.2）。
        var dispenserCountByEnv = new Dictionary<string, int>(StringComparer.Ordinal);
        var requiredCountByEnv = new Dictionary<string, int>(StringComparer.Ordinal);
        var usedMachinesByEnv = new Dictionary<string, double>(StringComparer.Ordinal);
        IEnumerable<string> envIds = envMachines.Keys.Concat(
            blockedMachinesByEnv
                .Where(kv => kv.Value > ProductionCalculator.Epsilon)
                .Select(kv => kv.Key));
        foreach (string envId in envIds)
        {
            if (!_master.EnvironmentsById.TryGetValue(envId, out Environment? env))
            {
                continue;
            }

            double usedMachines =
                envMachines.GetValueOrDefault(envId) + blockedMachinesByEnv.GetValueOrDefault(envId);
            usedMachinesByEnv[envId] = usedMachines;
            int required = ProductionCalculator.Ceil(usedMachines / env.CoverableMachines);
            requiredCountByEnv[envId] = required;

            EnvironmentCountOverride? envOverride =
                _environmentOverrides.FirstOrDefault(o => o.EnvironmentId == envId);
            if (envOverride is { Count: < 0 })
            {
                Warnings.Add(new CalculationWarning(
                    WarningCode.InvalidEnvironmentOverride,
                    $"環境 {_display.Environment(envId)} の散布機台数の上書きが負のため既定値を使います: {envOverride.Count}"));
            }

            dispenserCountByEnv[envId] = envOverride is { Count: >= 0 }
                ? envOverride.Count
                : required;
        }

        foreach (EnvironmentCountOverride envOverride in _environmentOverrides)
        {
            if (!_master.EnvironmentsById.ContainsKey(envOverride.EnvironmentId))
            {
                Warnings.Add(new CalculationWarning(
                    WarningCode.InvalidEnvironmentOverride,
                    $"散布機台数の上書きが存在しない環境を指しています: {_display.Environment(envOverride.EnvironmentId)}"));
            }
        }

        return new FacilityCounts(exactByFacility, dispenserCountByEnv, requiredCountByEnv, usedMachinesByEnv);
    }

    /// <summary>
    /// 環境の消費アイテム需要（ConsumeRatePerMinute×台数）と
    /// 固定消費需要（RatePerMinute×設備の切上げ台数）の合計（個/分）。
    /// </summary>
    private Dictionary<string, double> ComputeExtraDemand(FacilityCounts counts)
    {
        var extra = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach ((string envId, int count) in counts.DispenserCountByEnv)
        {
            if (count <= 0 || !_master.EnvironmentsById.TryGetValue(envId, out Environment? env))
            {
                continue;
            }

            extra[env.ConsumeItemId] = extra.GetValueOrDefault(env.ConsumeItemId)
                + env.ConsumeRatePerMinute * count;
        }

        // 固定消費の乗数は Aggregate の FacilityRequirement と同じ「最終切上台数」。
        // 提供設備がレシピ設備と兼用の場合は散布機分も同じ台数に含める。
        var dispenserCountByFacility = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string envId, int count) in counts.DispenserCountByEnv)
        {
            if (count <= 0 || !_master.EnvironmentsById.TryGetValue(envId, out Environment? env))
            {
                continue;
            }

            dispenserCountByFacility[env.ProviderFacilityId] =
                dispenserCountByFacility.GetValueOrDefault(env.ProviderFacilityId) + count;
        }

        foreach (PairSelector.Selection run in RunOrder)
        {
            FixedConsumption? fixedConsumption = run.Pair.FixedConsumption;
            if (fixedConsumption is null)
            {
                continue;
            }

            int ceilCount = ProductionCalculator.Ceil(counts.ExactByFacility.GetValueOrDefault(run.Pair.FacilityId)
                + dispenserCountByFacility.GetValueOrDefault(run.Pair.FacilityId));
            extra[fixedConsumption.ItemId] = extra.GetValueOrDefault(fixedConsumption.ItemId)
                + fixedConsumption.RatePerMinute * ceilCount;
        }

        return extra;
    }
}
