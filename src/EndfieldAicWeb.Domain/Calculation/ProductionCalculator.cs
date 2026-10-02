using EndfieldAicWeb.Domain.Models;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Domain.Calculation;

/// <summary>
/// 生産計画の計算（需要展開・副産物充当・設備台数・環境計上・固定消費・流量調整・輸送容量）を行う。
/// 骨格は旧 ProductionCalculator の Session（展開→引き戻しの固定点反復）を移植したもので、
/// ペア選択（F/U）・環境計上（I）・固定消費（J/V）・イベント不可扱い（T/X）・収束反復を含む。
/// </summary>
public static class ProductionCalculator
{
    internal const double Epsilon = 1e-9;

    /// <summary>ベルトの輸送上限（個/分）。</summary>
    public const double BeltCapacityPerMinute = 30.0;

    /// <summary>パイプの輸送上限（個/分）。</summary>
    public const double PipeCapacityPerMinute = 60.0;

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

        foreach (ProductionTarget target in targets)
        {
            if (target.RatePerMinute <= 0 || !double.IsFinite(target.RatePerMinute))
            {
                throw new ArgumentException(
                    $"目標の RatePerMinute は正の有限値である必要があります: {target.ItemId} = {target.RatePerMinute}");
            }

            if (!master.ItemsById.ContainsKey(target.ItemId))
            {
                throw new ArgumentException($"目標のアイテムがマスタに存在しません: {target.ItemId}");
            }
        }

        var session = new Session(master, context, overrides, environmentOverrides, gatherOverrides);
        session.Run(targets);
        return Aggregate(master, session);
    }

    /// <summary>1回分の計算の内部状態。</summary>
    private sealed class Session
    {
        private readonly MasterDataSnapshot _master;
        private readonly ContextFilter _context;
        private readonly IReadOnlyList<PairOverride> _overrides;
        private readonly IReadOnlyList<EnvironmentCountOverride> _environmentOverrides;
        private readonly IReadOnlyList<GatherRateOverride> _gatherOverrides;

        /// <summary>採取素材ごとの有効採取上限（個/分、PositiveInfinity は上限なし）。構築時に一度だけ解決する。</summary>
        private readonly Dictionary<string, double> _gatherCaps = new(StringComparer.Ordinal);

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

        private readonly List<string> _stack = [];
        private readonly HashSet<string> _stackSet = new(StringComparer.Ordinal);

        internal Session(
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
            ResolveGatherCaps();
        }

        /// <summary>
        /// 採取素材ごとの有効採取上限（個/分、無限は <see cref="double.PositiveInfinity"/>）を解決する。
        /// 解決順は 上書き → マップの採取レート行 → 行なしは 0 で、無限行は上限なし、
        /// マップ未選択は全採取素材を無制限とする（仕様決定 AC・AE）。
        /// マップが存在しない、または所属イベントが非有効なら全採取素材を採取不可（上限 0）とし、
        /// 採取レートのユーザー上書きは適用しない（仕様決定 AD、X と同型）。
        /// </summary>
        private void ResolveGatherCaps()
        {
            List<string> gatherableIds = _master.Items.Where(i => i.IsGatherable).Select(i => i.Id).ToList();

            void CapAll(double cap)
            {
                foreach (string id in gatherableIds)
                {
                    _gatherCaps[id] = cap;
                }
            }

            if (_context.MapId is null)
            {
                // 未選択は無制限。上書きは有効なマップ選択に対してのみ適用される（仕様決定 AE）。
                CapAll(double.PositiveInfinity);
                return;
            }

            if (!_master.MapsById.TryGetValue(_context.MapId, out GameMap? map))
            {
                Warnings.Add(new CalculationWarning(
                    WarningCode.InvalidGatherMap,
                    $"選択されたマップ {_context.MapId} はマスタに存在しないため、採取素材はすべて採取できません。"));
                CapAll(0);
                return;
            }

            if (map.GameEventId is not null && !_context.ActiveGameEventIds.Contains(map.GameEventId))
            {
                Warnings.Add(new CalculationWarning(
                    WarningCode.GatherMapUnavailable,
                    $"選択されたマップ {map.Id} はイベント {map.GameEventId} が有効でないため、採取素材はすべて採取できません。"));
                CapAll(0);
                return;
            }

            // 上書きの検証。存在しない・採取素材でないアイテムへの指定と、負・非有限の値は無視する。
            var overrideByItem = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (GatherRateOverride gatherOverride in _gatherOverrides)
            {
                if (!_master.ItemsById.TryGetValue(gatherOverride.ItemId, out Item? overrideItem)
                    || !overrideItem.IsGatherable)
                {
                    Warnings.Add(new CalculationWarning(
                        WarningCode.InvalidGatherRateOverride,
                        $"採取レートの上書きが採取素材でないアイテムを指しているため無視します: {gatherOverride.ItemId}"));
                    continue;
                }

                if (!double.IsFinite(gatherOverride.RatePerMinute) || gatherOverride.RatePerMinute < 0)
                {
                    Warnings.Add(new CalculationWarning(
                        WarningCode.InvalidGatherRateOverride,
                        $"アイテム {gatherOverride.ItemId} の採取レート上書き {gatherOverride.RatePerMinute} は 0 以上の有限値ではないため無視します。"));
                    continue;
                }

                // 同一アイテムへの重複上書きは先頭を採用する（散布機台数上書きと同型）。
                overrideByItem.TryAdd(gatherOverride.ItemId, gatherOverride.RatePerMinute);
            }

            foreach (string id in gatherableIds)
            {
                if (overrideByItem.TryGetValue(id, out double overridden))
                {
                    _gatherCaps[id] = overridden;
                    continue;
                }

                GatherRate? row = map.GatherRates.FirstOrDefault(r => r.ItemId == id);
                _gatherCaps[id] = row switch
                {
                    null => 0,
                    { IsUnlimited: true } => double.PositiveInfinity,
                    _ => row.RatePerMinute ?? 0,
                };
            }
        }

        /// <summary>採取素材の残り採取可能量（個/分）。採取素材以外や上限の解決漏れは 0。</summary>
        private double GatherRemaining(string itemId) =>
            Math.Max(0.0, _gatherCaps.GetValueOrDefault(itemId) - Get(Raw, itemId));

        /// <summary>均衡化（展開→引き戻し）の反復上限。目標順によらず収束先が一意になることを保証するための仕組み。</summary>
        private const int MaxBalanceRounds = 100;

        internal void Run(IReadOnlyList<ProductionTarget> targets)
        {
            foreach (ProductionTarget target in targets)
            {
                AddDemand(target.ItemId, target.RatePerMinute);
            }

            // 展開→台数確定→環境消費・固定消費の追加需要 の一巡を収束まで反復する（§3-8）。
            bool converged = false;
            for (int iteration = 0; iteration < MaxConvergenceIterations; iteration++)
            {
                BalanceToFixpoint();

                FacilityCounts counts = ComputeCounts();
                Dictionary<string, double> extra = ComputeExtraDemand(counts);

                bool changed = false;
                foreach (string itemId in extra.Keys.Union(ExtraApplied.Keys))
                {
                    double required = extra.GetValueOrDefault(itemId);
                    double applied = ExtraApplied.GetValueOrDefault(itemId);
                    double diff = required - applied;
                    if (Math.Abs(diff) <= Epsilon)
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
                    $"環境消費・固定消費の追加需要が {MaxConvergenceIterations} 回の反復内に収束しませんでした。結果は途中経過のものです。"));
            }

            // 採取上限超過の警告は均衡化後の最終 Unmet で発行する（仕様決定 AD）。
            // 後から供給された副産物や引き戻しで不足が解消されたアイテムには警告を残さない。
            foreach (string itemId in _gatherCapShortfall)
            {
                double unmet = Get(Unmet, itemId);
                if (unmet > Epsilon)
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
                if (Get(Unmet, itemId) > Epsilon && _cycleDetections.TryGetValue(itemId, out List<CycleDetection>? detections))
                {
                    string path = detections.MaxBy(d => d.Gain)!.Path;
                    Warnings.Add(new CalculationWarning(
                        WarningCode.CycleDetected,
                        $"循環依存を検出したため展開を打ち切りました: {path}"));
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

                bool expanded = RunCycles.Values.Sum() > cyclesBefore + Epsilon;
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
                    if (outputQty <= Epsilon)
                    {
                        continue;
                    }

                    double otherSupply = Get(Produced, itemId) - currentCycles * outputQty
                        + Get(Raw, itemId) + Get(Unmet, itemId);
                    double need = Get(Demand, itemId) - otherSupply;
                    required = Math.Max(required, Math.Max(need, 0) / outputQty);
                }

                double delta = currentCycles - required;
                if (delta <= Epsilon)
                {
                    continue;
                }

                if (required <= Epsilon)
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
                        Math.Max(0, Get(Produced, output.ItemId) - delta * output.Quantity);
                }

                foreach (RecipeInput input in recipe.Inputs)
                {
                    Demand[input.ItemId] =
                        Math.Max(0, Get(Demand, input.ItemId) - delta * input.Quantity);
                    TrimTerminal(input.ItemId);
                }

                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// 端末計上（Raw = 外部調達、Unmet = 充足不能）を残差需要 `max(0, Demand − Produced)` に揃える。
        /// 副産物で生産が増えたり縮小で入力需要が減ったりした際に、帳簿上残った過剰分を引き戻す。
        /// </summary>
        private bool TrimTerminal(string itemId)
        {
            // イベント不可アイテムは生産量を供給として認めないため、残差は需要全量（仕様決定 X）。
            double residual = IsItemInactive(itemId)
                ? Get(Demand, itemId)
                : Math.Max(0, Get(Demand, itemId) - Get(Produced, itemId));
            double excess = Get(Raw, itemId) + Get(Unmet, itemId) - residual;
            if (excess <= Epsilon)
            {
                return false;
            }

            double trimUnmet = Math.Min(Get(Unmet, itemId), excess);
            if (trimUnmet > 0)
            {
                Unmet[itemId] = Get(Unmet, itemId) - trimUnmet;
            }

            double trimRaw = excess - trimUnmet;
            if (trimRaw > 0)
            {
                Raw[itemId] = Math.Max(0, Get(Raw, itemId) - trimRaw);
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

        private static double Get(Dictionary<string, double> map, string key) =>
            map.TryGetValue(key, out double value) ? value : 0;

        private void Expand(string itemId)
        {
            // アイテム自体の所属イベントが非有効なら生産・外部調達・副産物充当とも不可（仕様決定 X）。
            // 副産物で生産されても需要は未充足のままとするため、需要全量を未充足へ計上する。
            if (IsItemInactive(itemId))
            {
                double need = Get(Demand, itemId) - Get(Unmet, itemId);
                if (need > Epsilon)
                {
                    Unmet[itemId] = Get(Unmet, itemId) + need;
                    Warnings.Add(new CalculationWarning(
                        WarningCode.EventItemUnavailable,
                        $"アイテム {itemId} はイベント {_master.ItemsById[itemId].GameEventId} が有効でないため生産・調達できません。"));
                }

                return;
            }

            double net = Get(Demand, itemId) - Get(Produced, itemId) - Get(Raw, itemId) - Get(Unmet, itemId);
            if (net <= Epsilon)
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
                if (gathered > Epsilon)
                {
                    Raw[itemId] = Get(Raw, itemId) + gathered;
                    remainder -= gathered;
                }

                if (remainder <= Epsilon)
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
                Unmet[itemId] = Get(Unmet, itemId) + remainder;
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
                Unmet[itemId] = Get(Unmet, itemId) + remainder;
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
                Unmet[itemId] = Get(Unmet, itemId) + remainder;
                Warnings.Add(new CalculationWarning(
                    WarningCode.NoRecipeAvailable,
                    $"アイテム {itemId} のレシピ {recipe.Id} の出力数量が 0 以下のため生産できません。"));
                return;
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
                Produced[output.ItemId] = Get(Produced, output.ItemId) + delta * output.Quantity;
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
                bool hopLive = Get(Demand, detection.FirstHop)
                    - Get(Produced, detection.FirstHop)
                    - Get(Raw, detection.FirstHop) >= -Epsilon;
                if (hopLive && detection.Gain >= 1.0 - Epsilon)
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

            double before = _cycleDeposits.Sum(id => Get(Unmet, id));
            if (before <= Epsilon)
            {
                return false;
            }

            ReleaseSnapshot snapshot = CaptureReleaseState();
            bool released = false;
            foreach (string itemId in _cycleDeposits
                .Where(id => Get(Unmet, id) > Epsilon && IsReleasable(id))
                .OrderBy(id => DemandOrder.IndexOf(id))
                .ToList())
            {
                Unmet[itemId] = 0;
                Expand(itemId);
                released = true;
            }

            if (!released)
            {
                return false;
            }

            double after = _cycleDeposits.Sum(id => Get(Unmet, id));
            if (after >= before - Epsilon)
            {
                RestoreReleaseState(snapshot);
                _cycleReleaseStopped = true;
                return false;
            }

            return true;
        }

        /// <summary>解放ステップの帳簿一式の控え（仕様決定 AQ のロールバック保険）。</summary>
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
            var envPairCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (PairSelector.Selection run in RunOrder)
            {
                double facilityTime = RunCycles[run] * run.Pair.CycleTime / 60.0;
                exactByFacility[run.Pair.FacilityId] =
                    exactByFacility.GetValueOrDefault(run.Pair.FacilityId) + facilityTime;

                if (run.Pair.EnvironmentId is string envId)
                {
                    envPairCount[envId] = envPairCount.GetValueOrDefault(envId) + 1;
                }
            }

            // 散布機台数: 既定はその環境を必要とする稼働中レシピ数（レシピにつき 1 台）。
            // ユーザー上書きを優先する（仕様決定 I）。計算に登場しない環境への上書きは無視する。
            var dispenserCountByEnv = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach ((string envId, int needed) in envPairCount)
            {
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
                    : needed;
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

            return new FacilityCounts(exactByFacility, dispenserCountByEnv);
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

                int ceilCount = Ceil(counts.ExactByFacility.GetValueOrDefault(run.Pair.FacilityId)
                    + dispenserCountByFacility.GetValueOrDefault(run.Pair.FacilityId));
                extra[fixedConsumption.ItemId] = extra.GetValueOrDefault(fixedConsumption.ItemId)
                    + fixedConsumption.RatePerMinute * ceilCount;
            }

            return extra;
        }
    }

    /// <summary>設備ごとの実数台数と環境ごとの散布機台数。</summary>
    private sealed record FacilityCounts(
        Dictionary<string, double> ExactByFacility,
        Dictionary<string, int> DispenserCountByEnv);

    private static ProductionPlan Aggregate(MasterDataSnapshot master, Session session)
    {
        var itemRequirements = new List<ItemRequirement>();
        foreach (string itemId in session.DemandOrder)
        {
            // 引き戻しで需要が帳簿上 0 になったアイテムは要求行として出さない。
            if (session.Demand[itemId] <= Epsilon)
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
                if (portion <= Epsilon)
                {
                    continue;
                }

                SupplyKind kind = ReferenceEquals(selected, run) ? SupplyKind.Recipe : SupplyKind.Byproduct;
                supplies.Add(new SupplyPortion(kind, run.Recipe.Id, portion));
            }

            double raw = itemInactive ? 0 : GetFrom(session.Raw, itemId);
            if (raw > Epsilon)
            {
                supplies.Add(new SupplyPortion(SupplyKind.Gathered, null, raw));
            }

            itemRequirements.Add(new ItemRequirement(
                itemId,
                session.Demand[itemId],
                supplies,
                GetFrom(session.Unmet, itemId)));
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
                env.ConsumeRatePerMinute * dispenserCount));

            // 散布機は設備要件・消費電力に計上する（実数=切上げの指定台数）。
            exactByFacility[env.ProviderFacilityId] =
                exactByFacility.GetValueOrDefault(env.ProviderFacilityId) + dispenserCount;
        }

        var facilityRequirements = exactByFacility
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new FacilityRequirement(kv.Key, kv.Value, Ceil(kv.Value)))
            .ToList();

        var recipeRuns = session.RunOrder
            .Select(r => new RecipeRun(r.Recipe.Id, r.Pair.FacilityId, session.RunCycles[r]))
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
                : produced - GetFrom(session.Demand, itemId);
            if (excess > Epsilon)
            {
                surpluses.Add(new SurplusProduction(itemId, excess));
            }
        }

        var flowAdjustments = BuildFlowAdjustments(session);

        WarningBag warnings = session.Warnings;
        AddTransportWarnings(master, recipeRuns, facilityRequirements, environmentRequirements, pairSelections, warnings);

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

    private static int Ceil(double value) => (int)Math.Ceiling(value - Epsilon);

    private static double GetFrom(Dictionary<string, double> map, string key) =>
        map.TryGetValue(key, out double value) ? value : 0;

    /// <summary>
    /// 自身の使用台数に端数（設備の一部余力）があるレシピの入力について推奨流量制限を出力する。
    /// 判定はランごとの使用台数で行う。設備を共用する場合、0.5 台ずつの使用でも各レシピに
    /// 制限を出し、整数台の全速稼働（余力なし）には出さない。
    /// </summary>
    private static List<FlowAdjustment> BuildFlowAdjustments(Session session)
    {
        var adjustments = new Dictionary<(string RecipeId, string InputItemId), double>();

        foreach (PairSelector.Selection run in session.RunOrder)
        {
            double machines = session.RunCycles[run] * run.Pair.CycleTime / 60.0;
            if (Ceil(machines) <= machines + Epsilon)
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
            .Where(kv => kv.Value > Epsilon)
            .Select(kv => new FlowAdjustment(kv.Key.RecipeId, kv.Key.InputItemId, kv.Value, kv.Value))
            .ToList();
    }

    /// <summary>
    /// 設備 1 ユニットへの入力流量が輸送媒体（ベルト 30 個/分・パイプ 60 個/分）の上限を
    /// 超える場合に警告を追加する（仕様決定 AN）。
    /// 台数・レーンを増やせば解消できる集計超過（需要・生産・採取の流量）は対象外。
    /// </summary>
    private static void AddTransportWarnings(
        MasterDataSnapshot master,
        IReadOnlyList<RecipeRun> recipeRuns,
        IReadOnlyList<FacilityRequirement> facilityRequirements,
        IReadOnlyList<EnvironmentRequirement> environmentRequirements,
        IReadOnlyList<PairSelection> pairSelections,
        WarningBag warnings)
    {
        Dictionary<string, List<FacilityUnitSlot>> unitsByFacility = FacilityUnitLayout.Allocate(
            recipeRuns, facilityRequirements, environmentRequirements, master, pairSelections);

        var maxRateByItem = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach ((string facilityId, List<FacilityUnitSlot> units) in unitsByFacility)
        {
            var inputs = units
                .Select(_ => new Dictionary<string, double>(StringComparer.Ordinal))
                .ToList();

            for (int runIndex = 0; runIndex < recipeRuns.Count; runIndex++)
            {
                RecipeRun run = recipeRuns[runIndex];
                if (run.FacilityId != facilityId
                    || !master.RecipesById.TryGetValue(run.RecipeId, out Recipe? recipe))
                {
                    continue;
                }

                // レシピ入力はユニットの占有比率で分かれる。
                foreach (FacilityUnitSlot unit in units)
                {
                    double share = unit.RunShares.GetValueOrDefault(runIndex);
                    if (share <= Epsilon)
                    {
                        continue;
                    }

                    Dictionary<string, double> unitInputs = inputs[unit.Index];
                    foreach (RecipeInput input in recipe.Inputs)
                    {
                        unitInputs[input.ItemId] = unitInputs.GetValueOrDefault(input.ItemId)
                            + run.CyclesPerMinute * input.Quantity * share;
                    }
                }

                // 固定消費はユニットごとに同量を消費する。
                RecipeFacility? pair = pairSelections
                    .Where(s => s.RecipeId == run.RecipeId && s.Pair.FacilityId == run.FacilityId)
                    .Select(s => s.Pair)
                    .FirstOrDefault()
                    ?? recipe.Facilities.FirstOrDefault(p => p.FacilityId == run.FacilityId);
                if (pair?.FixedConsumption is { } fixedConsumption)
                {
                    foreach (Dictionary<string, double> unitInputs in inputs)
                    {
                        unitInputs[fixedConsumption.ItemId] =
                            unitInputs.GetValueOrDefault(fixedConsumption.ItemId)
                            + fixedConsumption.RatePerMinute;
                    }
                }
            }

            // 環境消費はその環境の散布機ユニットへ台数ぶん等量に分かれる。
            foreach (EnvironmentRequirement env in environmentRequirements)
            {
                if (env.ProviderFacilityId != facilityId || env.DispenserCount <= 0)
                {
                    continue;
                }

                double perUnit = env.ConsumeRatePerMinuteTotal / env.DispenserCount;
                foreach (FacilityUnitSlot unit in units
                    .Where(u => u.DispenserEnvironmentId == env.EnvironmentId))
                {
                    Dictionary<string, double> unitInputs = inputs[unit.Index];
                    unitInputs[env.ConsumeItemId] =
                        unitInputs.GetValueOrDefault(env.ConsumeItemId) + perUnit;
                }
            }

            foreach (Dictionary<string, double> unitInputs in inputs)
            {
                foreach ((string itemId, double rate) in unitInputs)
                {
                    if (rate > maxRateByItem.GetValueOrDefault(itemId))
                    {
                        maxRateByItem[itemId] = rate;
                    }
                }
            }
        }

        foreach ((string itemId, double rate) in maxRateByItem.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!master.ItemsById.TryGetValue(itemId, out Item? item))
            {
                continue;
            }

            double limit = item.TransportKind switch
            {
                TransportKind.Belt => BeltCapacityPerMinute,
                TransportKind.Pipe => PipeCapacityPerMinute,
                _ => 0,
            };
            if (limit <= 0 || rate <= limit + Epsilon)
            {
                continue;
            }

            warnings.Add(new CalculationWarning(
                WarningCode.TransportCapacityExceeded,
                $"アイテム {itemId} の設備 1 台への入力流量 {rate:F2} 個/分 が輸送容量（{item.TransportKind} {limit:F0} 個/分）を超えています"));
        }
    }
}
