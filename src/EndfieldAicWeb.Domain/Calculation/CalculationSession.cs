using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Domain.Calculation;

/// <summary>
/// 1回分の計算の内部状態（展開→引き戻し→循環解放の固定点ソルバー）。
/// <see cref="ProductionCalculator"/> 専用の内部実装であり、計算の外からは使わない。
/// </summary>
internal sealed partial class CalculationSession
{
    private readonly MasterDataSnapshot _master;
    private readonly ContextFilter _context;
    private readonly IReadOnlyList<PairOverride> _overrides;
    private readonly IReadOnlyList<EnvironmentCountOverride> _environmentOverrides;
    private readonly IReadOnlyList<GatherRateOverride> _gatherOverrides;

    /// <summary>警告文面のエンティティ参照を `名前（Id）` へ整形する解決器（仕様決定 CF）。</summary>
    private readonly EntityDisplay _display;

    internal readonly Dictionary<string, double> Demand = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, double> Produced = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, double> Raw = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, double> Unmet = new(StringComparer.Ordinal);

    /// <summary>稼働中ペアごとの 1 分あたりサイクル数。</summary>
    internal readonly Dictionary<PairSelector.Selection, double> RunCycles = new();

    /// <summary>稼働中のペア（確定順）。</summary>
    internal readonly List<PairSelector.Selection> RunOrder = [];

    /// <summary>アイテムごとの選択結果（未選択はキーなし、選択不可は null）。</summary>
    internal readonly Dictionary<string, PairSelector.Selection?> Selection = new(StringComparer.Ordinal);

    /// <summary>需要が発生した順のアイテム列。</summary>
    internal readonly List<string> DemandOrder = [];

    /// <summary>環境消費・固定消費として需要へ追加済みの量（個/分）。収束判定に使う。</summary>
    internal readonly Dictionary<string, double> ExtraApplied = new(StringComparer.Ordinal);

    internal readonly WarningBag Warnings = new();

    internal CalculationSession(
        MasterDataSnapshot master,
        ContextFilter context,
        IReadOnlyList<PairOverride> overrides,
        IReadOnlyList<EnvironmentCountOverride> environmentOverrides,
        IReadOnlyList<GatherRateOverride> gatherOverrides)
    {
        _master = master;
        _context = context;
        _overrides = overrides;
        _environmentOverrides = environmentOverrides;
        _gatherOverrides = gatherOverrides;
        _display = new EntityDisplay(master);
        _gatherCaps = GatherCapResolver.Resolve(master, context, gatherOverrides, Warnings);

        // 散布機台数の上書きを機械数上限へ展開する（仕様決定 BR）。
        // 同一環境へ複数上書きされたときは先勝ち（ComputeCounts の FirstOrDefault と同じ評価）。
        var resolvedEnvOverrides = new HashSet<string>(StringComparer.Ordinal);
        foreach (EnvironmentCountOverride envOverride in environmentOverrides)
        {
            if (!resolvedEnvOverrides.Add(envOverride.EnvironmentId) || envOverride.Count < 0)
            {
                continue;
            }
            if (_master.EnvironmentsById.TryGetValue(envOverride.EnvironmentId, out Environment? env))
            {
                _envCaps[envOverride.EnvironmentId] = envOverride.Count * (double)env.CoverableMachines;
            }
        }
    }

    /// <summary>均衡化（展開→引き戻し）の反復上限。目標順によらず収束先が一意になることを保証するための仕組み。</summary>
    private const int MaxBalanceRounds = 100;

    internal void Run(IReadOnlyList<ProductionTarget> targets)
    {
        var targetItemIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ProductionTarget target in targets)
        {
            AddDemand(target.ItemId, target.RatePerMinute);
            targetItemIds.Add(target.ItemId);
        }

        // 展開→処理需要→台数確定→環境消費・固定消費の追加需要 の一巡を収束まで反復する（§3-8・CA）。
        bool converged = false;
        for (int iteration = 0; iteration < ProductionCalculator.MaxConvergenceIterations; iteration++)
        {
            BalanceToFixpoint();

            // 処理需要は均衡化後の余剰へ追随して毎反復で再評価する（CA・暫定解釈 3・7）。
            // 台数確定と追加需要の計算の前に行い、処理ラン由来の台数・固定消費・環境消費を
            // 同じ反復の追加需要計算に載せる。
            bool changed = ReevaluateDisposal(targetItemIds);

            FacilityCounts counts = ComputeCounts();
            Dictionary<string, double> extra = ComputeExtraDemand(counts);

            foreach (string itemId in extra.Keys.Union(ExtraApplied.Keys))
            {
                double required = extra.GetValueOrDefault(itemId);
                double applied = ExtraApplied.GetValueOrDefault(itemId);
                double diff = required - applied;
                if (Math.Abs(diff) <= ProductionCalculator.Epsilon)
                {
                    continue;
                }

                AddDemand(itemId, diff);
                ExtraApplied[itemId] = required;
                changed = true;
            }

            if (!changed)
            {
                converged = true;
                break;
            }
        }

        if (!converged)
        {
            // 最終反復で追加した需要も帳簿へ反映し、未収束でも要求・供給・未充足が
            // 同じスナップショットを指すようにする。
            BalanceToFixpoint();

            Warnings.Add(new CalculationWarning(
                WarningCode.ConvergenceNotReached,
                $"環境消費・固定消費の追加需要が {ProductionCalculator.MaxConvergenceIterations} 回の反復内に収束しませんでした。結果は途中経過のものです。"));
        }

        // 採取上限超過の警告は均衡化後の最終 Unmet で発行する（仕様決定 AD）。
        // 後から供給された副産物や引き戻しで不足が解消されたアイテムには警告を残さない。
        foreach (string itemId in _gatherCapShortfall)
        {
            double unmet = ProductionCalculator.GetOrZero(Unmet, itemId);
            if (unmet > ProductionCalculator.Epsilon)
            {
                Warnings.Add(new CalculationWarning(
                    WarningCode.GatherCapExceeded,
                    $"アイテム {_display.Item(itemId)} の需要が採取上限（{_gatherCaps.GetValueOrDefault(itemId):0.###} 個/分）を超え、代替レシピもないため {unmet:0.###} 個/分が不足します。"));
            }
        }

        // 循環依存の警告も均衡化後の最終 Unmet で発行する（仕様決定 AQ）。
        // 解放反復で解消した循環や、後から届いた副産物で未充足が消えた循環には警告を残さない。
        foreach (string itemId in _cycleDeposits)
        {
            if (ProductionCalculator.GetOrZero(Unmet, itemId) > ProductionCalculator.Epsilon && _cycleDetections.TryGetValue(itemId, out List<CycleDetection>? detections))
            {
                string path = detections.MaxBy(d => d.Gain)!.Path;
                Warnings.Add(new CalculationWarning(
                    WarningCode.CycleDetected,
                    $"循環依存を検出したため展開を打ち切りました: {path}"));
            }
        }

        // カバー不足の警告も同じく有効削減機械数で発行する（仕様決定 BR）。
        // 副産物の充当や引き戻しで解消された停止分には警告を残さない。
        foreach ((string envId, double effectiveMachines) in EffectiveBlockedMachines())
        {
            if (effectiveMachines > ProductionCalculator.Epsilon)
            {
                double cap = _envCaps.GetValueOrDefault(envId);
                Warnings.Add(new CalculationWarning(
                    WarningCode.EnvironmentCoverageExceeded,
                    $"環境 {_display.Environment(envId)} の散布機がカバーできる機械数（{cap:0.###} 機）を超える {effectiveMachines:0.###} 機分の生産が未充足です。"));
            }
        }
    }

    /// <summary>
    /// 展開と引き戻しを交互に繰り返して固定点に収束させる。
    /// 後から供給される副産物で不要になった先行レシピの稼働を取り消し、
    /// 引き戻しで不足し直したアイテムは次の展開で残差だけ補う。
    /// </summary>
    private void BalanceToFixpoint()
    {
        for (int round = 0; round < MaxBalanceRounds; round++)
        {
            double cyclesBefore = RunCycles.Values.Sum();
            foreach (string itemId in DemandOrder.ToArray())
            {
                Expand(itemId);
            }

            bool expanded = RunCycles.Values.Sum() > cyclesBefore + ProductionCalculator.Epsilon;
            bool retracted = Retract();
            bool released = ReleaseCycleResiduals();
            if (!expanded && !retracted && !released)
            {
                break;
            }
        }
    }

    private void AddDemand(string itemId, double amount)
    {
        if (!Demand.TryAdd(itemId, amount))
        {
            Demand[itemId] = Math.Max(0, Demand[itemId] + amount);
        }
        else
        {
            DemandOrder.Add(itemId);
        }
    }

    /// <summary>アイテムの所属イベントがコンテキスト上で非有効か（仕様決定 X）。</summary>
    internal bool IsItemInactive(string itemId) =>
        _master.ItemsById.TryGetValue(itemId, out Item? item)
            && item.GameEventId is not null
            && !_context.ActiveGameEventIds.Contains(item.GameEventId);
}
