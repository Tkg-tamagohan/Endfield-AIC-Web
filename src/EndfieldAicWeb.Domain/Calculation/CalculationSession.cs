using EndfieldAicWeb.Domain.Models;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Domain.Calculation;

/// <summary>
/// 1回分の計算の内部状態（展開→引き戻し→循環解放の固定点ソルバー）。
/// <see cref="ProductionCalculator"/> 専用の内部実装であり、計算の外からは使わない。
/// </summary>
internal sealed class CalculationSession
{
    private readonly MasterDataSnapshot _master;
    private readonly ContextFilter _context;
    private readonly IReadOnlyList<PairOverride> _overrides;
    private readonly IReadOnlyList<EnvironmentCountOverride> _environmentOverrides;
    private readonly IReadOnlyList<GatherRateOverride> _gatherOverrides;

    /// <summary>採取素材ごとの有効採取上限（個/分、PositiveInfinity は上限なし）。構築時に一度だけ解決する。</summary>
    private readonly Dictionary<string, double> _gatherCaps;

    /// <summary>
    /// 採取上限を超えて未充足になった採取素材。警告は均衡化後の最終 Unmet で発行するため
    /// ここでは候補のみ記録する（後の供給や引き戻しで不足が解消されることがある）。
    /// </summary>
    private readonly HashSet<string> _gatherCapShortfall = new(StringComparer.Ordinal);

    /// <summary>
    /// 循環検出で未充足へ計上したアイテム。解放ステップの対象一覧と、
    /// 警告の遅延発行の根拠に使う（仕様決定 AQ）。
    /// </summary>
    private readonly HashSet<string> _cycleDeposits = new(StringComparer.Ordinal);

    /// <summary>循環検出 1 件の記録。パス上のレシピ比率の積、パス文字列、最初の枝のアイテムを持つ。</summary>
    private sealed record CycleDetection(double Gain, string Path, string FirstHop);

    /// <summary>
    /// アイテムごとの循環検出一覧。解放可否は検出単位で判定する：
    /// 枝の先頭アイテムがまだ供給を要する（ライブな）検出のうち、ゲインが 1 以上のものが
    /// 1 本でもあれば解放しない。副産物等で先頭アイテムの需要が余剰込みで賄われた検出は
    /// 枝が死んでいるため拒否権を持たない（仕様決定 AQ）。
    /// </summary>
    private readonly Dictionary<string, List<CycleDetection>> _cycleDetections = new(StringComparer.Ordinal);

    /// <summary>解放で残差が縮まなかった場合に、以後の解放を打ち切るフラグ（AQ の保険）。</summary>
    private bool _cycleReleaseStopped;

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

    /// <summary>処理需要として需要へ計上済みの量（個/分）。現在余剰の冪等な再評価と収束判定に使う（暫定解釈 7）。</summary>
    internal readonly Dictionary<string, double> DisposalApplied = new(StringComparer.Ordinal);

    /// <summary>
    /// 処理ランとして登録中のペア（処理レシピ Id → ラン）。
    /// 出力なしレシピのランは Selection（アイテムの産出レシピ選択）に載らないため別管理にする（CB・CC）。
    /// </summary>
    private readonly Dictionary<string, PairSelector.Selection> _disposalRuns = new(StringComparer.Ordinal);

    /// <summary>
    /// 処理対象として確定したアイテム → それを処理するランのレシピ Id
    /// （1 アイテム 1 処理レシピ、暫定解釈 2・9）。
    /// </summary>
    private readonly Dictionary<string, string> _disposalRecipeByItem = new(StringComparer.Ordinal);

    internal readonly WarningBag Warnings = new();

    private readonly List<string> _stack = [];
    private readonly HashSet<string> _stackSet = new(StringComparer.Ordinal);

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

    /// <summary>採取素材の残り採取可能量（個/分）。採取素材以外や上限の解決漏れは 0。</summary>
    private double GatherRemaining(string itemId) =>
        Math.Max(0.0, _gatherCaps.GetValueOrDefault(itemId) - ProductionCalculator.GetOrZero(Raw, itemId));

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

    /// <summary>未充足の計上。全原因の寄与を unmetAdded 帳簿へ併記する（BR の比例配分用）。</summary>
    private void AddUnmet(string itemId, double amount)
    {
        Unmet[itemId] = ProductionCalculator.GetOrZero(Unmet, itemId) + amount;
        _unmetAdded[itemId] = _unmetAdded.GetValueOrDefault(itemId) + amount;
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
                    $"アイテム {itemId} の需要が採取上限（{_gatherCaps.GetValueOrDefault(itemId):0.###} 個/分）を超え、代替レシピもないため {unmet:0.###} 個/分が不足します。"));
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
                    $"環境 {envId} の散布機がカバーできる機械数（{cap:0.###} 機）を超える {effectiveMachines:0.###} 機分の生産が未充足です。"));
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

    /// <summary>
    /// 選択ペアの稼働を「他経路の供給を差し引いた残差」まで縮小する。
    /// 副産物が後から供給されて自レシピが不要・過剰になった場合に稼働を取り消し、
    /// 入力需要・生産量の帳簿も差し戻す。0 になった稼働は取り除く。
    /// </summary>
    private bool Retract()
    {
        bool changed = false;
        foreach (string itemId in DemandOrder)
        {
            changed |= TrimTerminal(itemId);
        }

        foreach (PairSelector.Selection run in RunOrder.ToArray())
        {
            Recipe recipe = run.Recipe;

            // 出力なしレシピのラン（処理ラン）は引き戻しの対象外。Selection に現れないランは
            // 現行評価で required=0 と判定され除去されてしまうため明示的に除外する（CC）。
            if (recipe.Outputs.Count == 0)
            {
                continue;
            }

            double currentCycles = RunCycles[run];
            double required = 0;
            foreach (string itemId in DemandOrder)
            {
                if (!Selection.TryGetValue(itemId, out PairSelector.Selection? selected)
                    || !ReferenceEquals(selected, run))
                {
                    continue;
                }

                double outputQty = recipe.Outputs
                    .Where(o => o.ItemId == itemId)
                    .Sum(o => o.Quantity);
                if (outputQty <= ProductionCalculator.Epsilon)
                {
                    continue;
                }

                double otherSupply = ProductionCalculator.GetOrZero(Produced, itemId) - currentCycles * outputQty
                    + ProductionCalculator.GetOrZero(Raw, itemId) + ProductionCalculator.GetOrZero(Unmet, itemId);
                double need = ProductionCalculator.GetOrZero(Demand, itemId) - otherSupply;
                required = Math.Max(required, Math.Max(need, 0) / outputQty);
            }

            double delta = currentCycles - required;
            if (delta <= ProductionCalculator.Epsilon)
            {
                continue;
            }

            if (required <= ProductionCalculator.Epsilon)
            {
                RunCycles.Remove(run);
                RunOrder.Remove(run);
            }
            else
            {
                RunCycles[run] = required;
            }

            foreach (RecipeOutput output in recipe.Outputs)
            {
                Produced[output.ItemId] =
                    Math.Max(0, ProductionCalculator.GetOrZero(Produced, output.ItemId) - delta * output.Quantity);
            }

            foreach (RecipeInput input in recipe.Inputs)
            {
                Demand[input.ItemId] =
                    Math.Max(0, ProductionCalculator.GetOrZero(Demand, input.ItemId) - delta * input.Quantity);
                TrimTerminal(input.ItemId);
            }

            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// 処理需要の再評価（仕様決定 CA・CB）。均衡化後の現在余剰に対し、処理レシピの入力に
    /// 登場するアイテムへ処理ランを割り当て直す。評価順はアイテム Id 昇順で決定的（暫定解釈 3）。
    /// 処理需要は ExtraApplied と同型の差分適用で需要へ反映する。
    /// 処理ランの変更または需要への差分適用があれば true を返す（収束判定に含める、暫定解釈 6）。
    /// </summary>
    private bool ReevaluateDisposal(HashSet<string> targetItemIds)
    {
        if (_master.DisposalRecipesByInputItemId.Count == 0)
        {
            return false;
        }

        // 現在余剰（処理で計上済みの需要を除いた帳簿、暫定解釈 7）。
        // 採取（外部調達）供給分は対象にしない（暫定解釈 1）。
        double Remaining(string itemId) =>
            ProductionCalculator.GetOrZero(Produced, itemId)
            - (ProductionCalculator.GetOrZero(Demand, itemId) - DisposalApplied.GetValueOrDefault(itemId));

        // 処理ランごとの消費予定の合算。処理対象入力の上限は残り余剰から
        // 他ランの消費分を除いた残量とする。
        var consumed = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (PairSelector.Selection run in _disposalRuns.Values)
        {
            foreach (RecipeInput input in run.Recipe.Inputs)
            {
                consumed[input.ItemId] = consumed.GetValueOrDefault(input.ItemId)
                    + RunCycles[run] * input.Quantity;
            }
        }

        bool runsChanged = false;
        var evaluatedRecipes = new HashSet<string>(StringComparer.Ordinal);
        foreach (string itemId in _master.DisposalRecipesByInputItemId.Keys
                     .OrderBy(id => id, StringComparer.Ordinal)
                     .ToList())
        {
            // 計算目標のアイテムは余剰があっても処理対象外（CA）。
            if (targetItemIds.Contains(itemId))
            {
                continue;
            }

            bool claimed = _disposalRecipeByItem.TryGetValue(itemId, out string? claimedRecipeId);
            if (!claimed && Remaining(itemId) <= ProductionCalculator.Epsilon)
            {
                continue;
            }

            PairSelector.Selection? run;
            if (claimed)
            {
                run = _disposalRuns[claimedRecipeId!];
            }
            else
            {
                // 適格な処理レシピ・ペアがない場合は需要を加えず余剰のまま残す（CA・CC）。
                PairSelector.Selection? selected =
                    DisposalSelector.Select(itemId, _master, _context, Warnings);
                if (selected is null)
                {
                    continue;
                }

                // 同一処理レシピが複数の対象アイテムで選ばれた場合は既存ランへ合流する（CB）。
                run = _disposalRuns.TryGetValue(selected.Recipe.Id, out PairSelector.Selection? existing)
                    ? existing
                    : selected;
            }

            // 同一ランの再評価は 1 反復 1 回でよい（サイクル上限は対象入力すべてで決まる）。
            if (!evaluatedRecipes.Add(run!.Recipe.Id))
            {
                continue;
            }

            runsChanged |= RecomputeDisposalRun(run, Remaining, consumed);
        }

        // 処理需要を差分適用する（暫定解釈 6・7）。
        bool changed = false;
        foreach (string itemId in consumed.Keys.Union(DisposalApplied.Keys))
        {
            double required = consumed.GetValueOrDefault(itemId);
            double applied = DisposalApplied.GetValueOrDefault(itemId);
            double diff = required - applied;
            if (Math.Abs(diff) <= ProductionCalculator.Epsilon)
            {
                continue;
            }

            AddDemand(itemId, diff);
            DisposalApplied[itemId] = required;
            changed = true;
        }

        return changed || runsChanged;
    }

    /// <summary>
    /// 1 つの処理ランのサイクル数を現在余剰へ合わせて再確定する。
    /// 上限は「処理対象の入力」（残り余剰がある入力と処理ランが登録済みの入力）すべての
    /// 残り余剰 ÷ 1 サイクル入力量の最小値（暫定解釈 9）。
    /// 環境を要するペアは散布機台数上書きのカバー上限内に稼働を削る
    /// （削られた分は処理しきれない余剰として残し、未充足にはしない、暫定解釈 5）。
    /// ランの登録・除去またはサイクル数の変更があれば true を返す。
    /// </summary>
    private bool RecomputeDisposalRun(
        PairSelector.Selection run,
        Func<string, double> remaining,
        Dictionary<string, double> consumed)
    {
        Recipe recipe = run.Recipe;

        // サイクル数を確定し直すため、このランの既存消費分を先に帳簿から外す。
        double oldCycles = RunCycles.GetValueOrDefault(run);
        if (oldCycles > ProductionCalculator.Epsilon)
        {
            foreach (RecipeInput input in recipe.Inputs)
            {
                consumed[input.ItemId] =
                    consumed.GetValueOrDefault(input.ItemId) - oldCycles * input.Quantity;
            }

            RunCycles.Remove(run);
            RunOrder.Remove(run);
        }

        double cap = double.PositiveInfinity;
        bool hasTargetInput = false;
        foreach (RecipeInput input in recipe.Inputs)
        {
            // 残り余剰がある入力と処理対象として確定済みの入力が上限の対象（暫定解釈 9）。
            bool isTargetInput = remaining(input.ItemId) > ProductionCalculator.Epsilon
                || _disposalRecipeByItem.ContainsKey(input.ItemId);
            if (!isTargetInput)
            {
                continue;
            }

            hasTargetInput = true;
            double headroom = remaining(input.ItemId) - consumed.GetValueOrDefault(input.ItemId);
            cap = Math.Min(cap, headroom / input.Quantity);
        }

        double cycles = hasTargetInput ? Math.Max(0.0, cap) : 0.0;

        if (cycles > ProductionCalculator.Epsilon
            && run.Pair.EnvironmentId is { } envId
            && _envCaps.TryGetValue(envId, out double envCap))
        {
            double allowed = Math.Max(0.0, envCap - EnvUsedMachines(envId)) * 60.0 / run.Pair.CycleTime;
            cycles = Math.Min(cycles, allowed);
        }

        if (cycles > ProductionCalculator.Epsilon)
        {
            RunCycles[run] = cycles;
            RunOrder.Add(run);
            _disposalRuns[recipe.Id] = run;
            foreach (RecipeInput input in recipe.Inputs)
            {
                consumed[input.ItemId] = consumed.GetValueOrDefault(input.ItemId) + cycles * input.Quantity;

                // 残り余剰がある入力をこのランの処理対象として確定する
                // （他ランの処理対象はそのランに属するため移さない）。
                if (remaining(input.ItemId) > ProductionCalculator.Epsilon
                    && !_disposalRecipeByItem.ContainsKey(input.ItemId))
                {
                    _disposalRecipeByItem[input.ItemId] = recipe.Id;
                }
            }

            return Math.Abs(cycles - oldCycles) > ProductionCalculator.Epsilon;
        }

        // 処理対象の残量が尽きたランは除去し、このランが確定した処理対象を解除する。
        _disposalRuns.Remove(recipe.Id);
        foreach (RecipeInput input in recipe.Inputs)
        {
            if (_disposalRecipeByItem.TryGetValue(input.ItemId, out string? owner)
                && owner == recipe.Id)
            {
                _disposalRecipeByItem.Remove(input.ItemId);
            }
        }

        return oldCycles > ProductionCalculator.Epsilon;
    }

    /// <summary>
    /// 端末計上（Raw = 外部調達、Unmet = 充足不能）を残差需要 `max(0, Demand − Produced)` に揃える。
    /// 副産物で生産が増えたり縮小で入力需要が減ったりした際に、帳簿上残った過剰分を引き戻す。
    /// </summary>
    private bool TrimTerminal(string itemId)
    {
        // イベント不可アイテムは生産量を供給として認めないため、残差は需要全量（仕様決定 X）。
        double residual = IsItemInactive(itemId)
            ? ProductionCalculator.GetOrZero(Demand, itemId)
            : Math.Max(0, ProductionCalculator.GetOrZero(Demand, itemId) - ProductionCalculator.GetOrZero(Produced, itemId));
        double excess = ProductionCalculator.GetOrZero(Raw, itemId) + ProductionCalculator.GetOrZero(Unmet, itemId) - residual;
        if (excess <= ProductionCalculator.Epsilon)
        {
            return false;
        }

        double trimUnmet = Math.Min(ProductionCalculator.GetOrZero(Unmet, itemId), excess);
        if (trimUnmet > 0)
        {
            Unmet[itemId] = ProductionCalculator.GetOrZero(Unmet, itemId) - trimUnmet;
        }

        double trimRaw = excess - trimUnmet;
        if (trimRaw > 0)
        {
            Raw[itemId] = Math.Max(0, ProductionCalculator.GetOrZero(Raw, itemId) - trimRaw);
        }

        return true;
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

    private void Expand(string itemId)
    {
        // アイテム自体の所属イベントが非有効なら生産・外部調達・副産物充当とも不可（仕様決定 X）。
        // 副産物で生産されても需要は未充足のままとするため、需要全量を未充足へ計上する。
        if (IsItemInactive(itemId))
        {
            double need = ProductionCalculator.GetOrZero(Demand, itemId) - ProductionCalculator.GetOrZero(Unmet, itemId);
            if (need > ProductionCalculator.Epsilon)
            {
                AddUnmet(itemId, need);
                Warnings.Add(new CalculationWarning(
                    WarningCode.EventItemUnavailable,
                    $"アイテム {itemId} はイベント {_master.ItemsById[itemId].GameEventId} が有効でないため生産・調達できません。"));
            }

            return;
        }

        double net = ProductionCalculator.GetOrZero(Demand, itemId) - ProductionCalculator.GetOrZero(Produced, itemId) - ProductionCalculator.GetOrZero(Raw, itemId) - ProductionCalculator.GetOrZero(Unmet, itemId);
        if (net <= ProductionCalculator.Epsilon)
        {
            return;
        }

        double remainder = net;
        if (IsGatherable(itemId))
        {
            // 採取素材は有効採取上限までを採取（外部調達）とし、超過分のみをレシピへ展開する
            // （採取優先。従来のレシピ優先からの変更。仕様決定 AD）。
            // 採取は終端処理のため循環検出より先に行い、残差のみが以降の経路へ進む。
            double gathered = Math.Min(remainder, GatherRemaining(itemId));
            if (gathered > ProductionCalculator.Epsilon)
            {
                Raw[itemId] = ProductionCalculator.GetOrZero(Raw, itemId) + gathered;
                remainder -= gathered;
            }

            if (remainder <= ProductionCalculator.Epsilon)
            {
                return;
            }
        }

        if (_stackSet.Contains(itemId))
        {
            // 循環依存: 警告は即時には出さず、検出パスとループゲインを記録する（仕様決定 AQ）。
            // 正味増（ゲイン < 1）の残差は均衡化ラウンドの解放ステップで再展開し、
            // 残った未充足は Run 末尾で警告へ変える。
            RecordCycle(itemId);
            AddUnmet(itemId, remainder);
            return;
        }

        if (!Selection.TryGetValue(itemId, out PairSelector.Selection? selection))
        {
            PairSelector.Result result = PairSelector.Select(itemId, _master, _context, _overrides);
            foreach (CalculationWarning warning in result.Warnings)
            {
                Warnings.Add(warning);
            }

            selection = result.Selection;
            Selection[itemId] = selection;
        }

        if (selection is not null)
        {
            // 同一レシピに別ペアが既に稼働中なら先に確定したペアを採用する（仕様決定 BN）。
            // 引き戻しで休眠したキャッシュ済み選択にも適用し、稼働中インスタンスへ正規化する。
            PairSelector.Selection? running = RunOrder
                .FirstOrDefault(r => r.Recipe.Id == selection.Recipe.Id);
            if (running is not null)
            {
                if (!ReferenceEquals(running.Pair, selection.Pair))
                {
                    Warnings.Add(new CalculationWarning(
                        WarningCode.PairConflict,
                        $"アイテム {itemId} のレシピ {selection.Recipe.Id} には既に別のペアが稼働中のため、先に確定したペア（{running.Pair.FacilityId}）を採用します。"));
                }

                selection = running;
                Selection[itemId] = running;
            }
        }

        if (selection is null)
        {
            AddUnmet(itemId, remainder);
            if (IsGatherable(itemId))
            {
                // 警告の発行は Run 末尾で最終 Unmet を見て行う（途中の不足が後で解消されうる）。
                _gatherCapShortfall.Add(itemId);
            }
            else
            {
                Warnings.Add(new CalculationWarning(
                    WarningCode.NoRecipeAvailable,
                    $"アイテム {itemId} を生産できるレシピがありません。"));
            }

            return;
        }

        Recipe recipe = selection.Recipe;
        double outputQty = recipe.Outputs
            .Where(o => o.ItemId == itemId)
            .Sum(o => o.Quantity);
        double delta = remainder / outputQty;
        if (!double.IsFinite(delta) || delta <= 0)
        {
            AddUnmet(itemId, remainder);
            Warnings.Add(new CalculationWarning(
                WarningCode.NoRecipeAvailable,
                $"アイテム {itemId} のレシピ {recipe.Id} の出力数量が 0 以下のため生産できません。"));
            return;
        }

        // 散布機台数の上書きは 台数×CoverableMachines の機械数上限になる（仕様決定 BR）。
        // 上限にかかる分はランの稼働を削って当該アイテムを未充足へ計上し、削減記録は
        // Run 末尾の確定時に残存 Unmet へ比例配分する。環境の占有順は展開順。
        if (selection.Pair.EnvironmentId is string capEnvId
            && _envCaps.TryGetValue(capEnvId, out double envCap))
        {
            double allowedDelta =
                Math.Max(0.0, envCap - EnvUsedMachines(capEnvId)) * 60.0 / selection.Pair.CycleTime;
            if (allowedDelta < delta)
            {
                double cut = (delta - allowedDelta) * outputQty;
                AddUnmet(itemId, cut);
                _envBlockedUnmet[(capEnvId, itemId)] =
                    _envBlockedUnmet.TryGetValue((capEnvId, itemId), out EnvBlockedPortion? portion)
                        ? portion.Add(cut)
                        : new EnvBlockedPortion(cut, selection.Pair.CycleTime / (60.0 * outputQty));
                delta = allowedDelta;
                // 全量停止のランは RunCycles・RunOrder に登録しない（切断の繰り返しを避ける）。
                if (delta <= ProductionCalculator.Epsilon)
                {
                    return;
                }
            }
        }

        _stack.Add(itemId);
        _stackSet.Add(itemId);

        if (!RunCycles.TryAdd(selection, delta))
        {
            RunCycles[selection] += delta;
        }
        else
        {
            RunOrder.Add(selection);
        }

        foreach (RecipeOutput output in recipe.Outputs)
        {
            Produced[output.ItemId] = ProductionCalculator.GetOrZero(Produced, output.ItemId) + delta * output.Quantity;
            TrimTerminal(output.ItemId);
        }

        foreach (RecipeInput input in recipe.Inputs)
        {
            AddDemand(input.ItemId, delta * input.Quantity);
            Expand(input.ItemId);
        }

        _stack.RemoveAt(_stack.Count - 1);
        _stackSet.Remove(itemId);
    }

    /// <summary>
    /// 展開スタック上の再要求を循環依存として記録する（仕様決定 AQ）。
    /// 検出パス上の隣接アイテム対について、後続アイテムの入力量÷先行アイテムの出力量を辺の比率とし、
    /// その積をループゲインとして、パス・枝の先頭アイテムとともに記録する。
    /// 同一パスの再検出は重複登録しない。
    /// </summary>
    private void RecordCycle(string itemId)
    {
        int cycleStart = _stack.IndexOf(itemId);
        double gain = 1.0;
        for (int i = cycleStart; i < _stack.Count; i++)
        {
            string current = _stack[i];
            string next = i + 1 < _stack.Count ? _stack[i + 1] : itemId;
            // スタック上のアイテムは選択確定済み（未選択はスタックに積まれない）ため null になり得ない。
            Recipe recipe = Selection[current]!.Recipe;
            double outputQty = recipe.Outputs
                .Where(o => o.ItemId == current)
                .Sum(o => o.Quantity);
            double inputQty = recipe.Inputs
                .Where(input => input.ItemId == next)
                .Sum(input => input.Quantity);
            gain *= inputQty / outputQty;
        }

        string path = string.Join(" → ", _stack.Skip(cycleStart).Append(itemId));
        string firstHop = cycleStart + 1 < _stack.Count ? _stack[cycleStart + 1] : itemId;
        if (!_cycleDetections.TryGetValue(itemId, out List<CycleDetection>? detections))
        {
            detections = [];
            _cycleDetections[itemId] = detections;
        }
        if (!detections.Any(d => d.Path == path))
        {
            detections.Add(new CycleDetection(gain, path, firstHop));
        }

        _cycleDeposits.Add(itemId);
    }

    /// <summary>
    /// 循環検出を持つアイテムの残差を解放してよいか（仕様決定 AQ）。
    /// ライブな検出（枝の先頭アイテムの需要が生産＋外部供給で余剰込みに賄われていないもの）の
    /// ゲインが 1 以上なら解放しない——解放してもその枝へ需要が戻り発散するため。
    /// 先頭アイテムが副産物余剰で賄われた検出は枝が死んだとみなし拒否権を持たない。
    /// </summary>
    private bool IsReleasable(string itemId)
    {
        foreach (CycleDetection detection in _cycleDetections[itemId])
        {
            bool hopLive = ProductionCalculator.GetOrZero(Demand, detection.FirstHop)
                - ProductionCalculator.GetOrZero(Produced, detection.FirstHop)
                - ProductionCalculator.GetOrZero(Raw, detection.FirstHop) >= -ProductionCalculator.Epsilon;
            if (hopLive && detection.Gain >= 1.0 - ProductionCalculator.Epsilon)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 循環検出で未充足へ計上した残差を解放して再展開する（仕様決定 AQ）。
    /// 解放するのはライブな循環がすべて正味増のアイテムだけで、残差は反復のたびに
    /// おおよそゲイン倍に縮み、外部投入なしの定常解へ収束する。
    /// 解放後も残差合計が縮まない場合は、解放と再展開の帳簿一式を解放前へ差し戻して
    /// 以後の解放を打ち切る（複雑に絡む循環の保険。解放しなかった計算と同じ帳簿へ戻る）。
    /// </summary>
    private bool ReleaseCycleResiduals()
    {
        if (_cycleReleaseStopped)
        {
            return false;
        }

        double before = _cycleDeposits.Sum(id => ProductionCalculator.GetOrZero(Unmet, id));
        if (before <= ProductionCalculator.Epsilon)
        {
            return false;
        }

        ReleaseSnapshot snapshot = CaptureReleaseState();
        bool released = false;
        foreach (string itemId in _cycleDeposits
            .Where(id => ProductionCalculator.GetOrZero(Unmet, id) > ProductionCalculator.Epsilon && IsReleasable(id))
            .OrderBy(id => DemandOrder.IndexOf(id))
            .ToList())
        {
            Unmet[itemId] = 0;
            // 解放した残差の未充足帳簿もリセットする。再展開で削られれば記録し直され、
            // 副産物充当で解消する場合は削減記録を残さないため（§3.3 の比例配分を正しく保つ）。
            _unmetAdded[itemId] = 0;
            foreach ((string _, string ItemId) key in
                _envBlockedUnmet.Keys.Where(k => k.ItemId == itemId).ToList())
            {
                _envBlockedUnmet.Remove(key);
            }
            Expand(itemId);
            released = true;
        }

        if (!released)
        {
            return false;
        }

        double after = _cycleDeposits.Sum(id => ProductionCalculator.GetOrZero(Unmet, id));
        if (after >= before - ProductionCalculator.Epsilon)
        {
            RestoreReleaseState(snapshot);
            _cycleReleaseStopped = true;
            return false;
        }

        return true;
    }

    /// <summary>
    /// 解放ステップの帳簿一式の控え（仕様決定 AQ のロールバック保険）。
    /// 環境上限の使用済み機械数は RunCycles から導出されるため、RunCycles の復元で追随する（BR）。
    /// </summary>
    private sealed class ReleaseSnapshot
    {
        internal required Dictionary<string, double> Demand;
        internal required Dictionary<string, double> Produced;
        internal required Dictionary<string, double> Raw;
        internal required Dictionary<string, double> Unmet;
        internal required Dictionary<PairSelector.Selection, double> RunCycles;
        internal required List<PairSelector.Selection> RunOrder;
        internal required Dictionary<string, PairSelector.Selection?> Selection;
        internal required List<string> DemandOrder;
        internal required Dictionary<string, double> ExtraApplied;
        internal required HashSet<string> GatherCapShortfall;
        internal required HashSet<string> CycleDeposits;
        internal required Dictionary<string, List<CycleDetection>> CycleDetections;
        internal required List<CalculationWarning> Warnings;
        internal required Dictionary<string, double> UnmetAdded;
        internal required Dictionary<(string EnvId, string ItemId), EnvBlockedPortion> EnvBlockedUnmet;
        internal required Dictionary<string, double> DisposalApplied;
        internal required Dictionary<string, PairSelector.Selection> DisposalRuns;
        internal required Dictionary<string, string> DisposalRecipeByItem;
    }

    private ReleaseSnapshot CaptureReleaseState() => new()
    {
        Demand = new Dictionary<string, double>(Demand),
        Produced = new Dictionary<string, double>(Produced),
        Raw = new Dictionary<string, double>(Raw),
        Unmet = new Dictionary<string, double>(Unmet),
        RunCycles = new Dictionary<PairSelector.Selection, double>(RunCycles),
        RunOrder = [.. RunOrder],
        Selection = new Dictionary<string, PairSelector.Selection?>(Selection),
        DemandOrder = [.. DemandOrder],
        ExtraApplied = new Dictionary<string, double>(ExtraApplied),
        GatherCapShortfall = new HashSet<string>(_gatherCapShortfall, StringComparer.Ordinal),
        CycleDeposits = new HashSet<string>(_cycleDeposits, StringComparer.Ordinal),
        CycleDetections = _cycleDetections.ToDictionary(
            kv => kv.Key, kv => new List<CycleDetection>(kv.Value), StringComparer.Ordinal),
        Warnings = Warnings.AsList().ToList(),
        UnmetAdded = new Dictionary<string, double>(_unmetAdded, StringComparer.Ordinal),
        EnvBlockedUnmet = new Dictionary<(string, string), EnvBlockedPortion>(_envBlockedUnmet),
        DisposalApplied = new Dictionary<string, double>(DisposalApplied, StringComparer.Ordinal),
        DisposalRuns = new Dictionary<string, PairSelector.Selection>(_disposalRuns, StringComparer.Ordinal),
        DisposalRecipeByItem = new Dictionary<string, string>(_disposalRecipeByItem, StringComparer.Ordinal),
    };

    private void RestoreReleaseState(ReleaseSnapshot snapshot)
    {
        static void RestoreMap(Dictionary<string, double> map, Dictionary<string, double> from)
        {
            map.Clear();
            foreach (KeyValuePair<string, double> kv in from)
            {
                map[kv.Key] = kv.Value;
            }
        }

        RestoreMap(Demand, snapshot.Demand);
        RestoreMap(Produced, snapshot.Produced);
        RestoreMap(Raw, snapshot.Raw);
        RestoreMap(Unmet, snapshot.Unmet);
        RestoreMap(ExtraApplied, snapshot.ExtraApplied);
        RestoreMap(DisposalApplied, snapshot.DisposalApplied);
        RestoreMap(_unmetAdded, snapshot.UnmetAdded);
        _envBlockedUnmet.Clear();
        foreach (KeyValuePair<(string EnvId, string ItemId), EnvBlockedPortion> kv in snapshot.EnvBlockedUnmet)
        {
            _envBlockedUnmet[kv.Key] = kv.Value;
        }
        RunCycles.Clear();
        foreach (KeyValuePair<PairSelector.Selection, double> kv in snapshot.RunCycles)
        {
            RunCycles[kv.Key] = kv.Value;
        }

        RunOrder.Clear();
        RunOrder.AddRange(snapshot.RunOrder);
        Selection.Clear();
        foreach (KeyValuePair<string, PairSelector.Selection?> kv in snapshot.Selection)
        {
            Selection[kv.Key] = kv.Value;
        }

        DemandOrder.Clear();
        DemandOrder.AddRange(snapshot.DemandOrder);
        _gatherCapShortfall.Clear();
        _gatherCapShortfall.UnionWith(snapshot.GatherCapShortfall);
        _cycleDeposits.Clear();
        _cycleDeposits.UnionWith(snapshot.CycleDeposits);
        _cycleDetections.Clear();
        foreach (KeyValuePair<string, List<CycleDetection>> kv in snapshot.CycleDetections)
        {
            _cycleDetections[kv.Key] = [.. kv.Value];
        }

        _disposalRuns.Clear();
        foreach (KeyValuePair<string, PairSelector.Selection> kv in snapshot.DisposalRuns)
        {
            _disposalRuns[kv.Key] = kv.Value;
        }

        _disposalRecipeByItem.Clear();
        foreach (KeyValuePair<string, string> kv in snapshot.DisposalRecipeByItem)
        {
            _disposalRecipeByItem[kv.Key] = kv.Value;
        }

        Warnings.Clear();
        foreach (CalculationWarning warning in snapshot.Warnings)
        {
            Warnings.Add(warning);
        }
    }

    private bool IsGatherable(string itemId) =>
        _master.ItemsById.TryGetValue(itemId, out Item? item) && item.IsGatherable;

    /// <summary>アイテムの所属イベントがコンテキスト上で非有効か（仕様決定 X）。</summary>
    internal bool IsItemInactive(string itemId) =>
        _master.ItemsById.TryGetValue(itemId, out Item? item)
            && item.GameEventId is not null
            && !_context.ActiveGameEventIds.Contains(item.GameEventId);

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
                    $"環境 {envId} の散布機台数の上書きが負のため既定値を使います: {envOverride.Count}"));
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
                    $"散布機台数の上書きが存在しない環境を指しています: {envOverride.EnvironmentId}"));
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
