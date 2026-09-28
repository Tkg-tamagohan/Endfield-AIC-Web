using EndfieldAicWeb.Domain.Models;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Domain.Calculation;

/// <summary>
/// 生産計画の計算（需要展開・副産物充当・設備台数・環境計上・固定消費・流量調整・輸送容量）を行う。
/// 骨格は旧 ProductionCalculator の Session（展開→引き戻しの固定点反復）を移植したもので、
/// ペア選択（F/U）・環境計上（I）・固定消費（J/V）・イベント不可扱い（T/X）・収束反復を含む。
/// </summary>
public sealed class ProductionCalculator
{
    internal const double Epsilon = 1e-9;

    /// <summary>ベルトの輸送上限（個/s）。</summary>
    internal const double BeltCapacityPerSecond = 30.0;

    /// <summary>パイプの輸送上限（個/s）。</summary>
    internal const double PipeCapacityPerSecond = 60.0;

    /// <summary>環境消費・固定消費の追加需要が収束するまでの反復上限（implementation-plan §3-8）。</summary>
    internal const int MaxConvergenceIterations = 10;

    /// <summary>生産計画を計算する。</summary>
    public ProductionPlan Calculate(
        MasterDataSnapshot master,
        IReadOnlyList<ProductionTarget> targets,
        ContextFilter context,
        IReadOnlyList<PairOverride> overrides,
        IReadOnlyList<EnvironmentCountOverride> environmentOverrides)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(environmentOverrides);

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

        var session = new Session(master, context, overrides, environmentOverrides);
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
            IReadOnlyList<EnvironmentCountOverride> environmentOverrides)
        {
            _master = master;
            _context = context;
            _overrides = overrides;
            _environmentOverrides = environmentOverrides;
        }

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
                if (!expanded && !retracted)
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

            if (_stackSet.Contains(itemId))
            {
                int cycleStart = _stack.IndexOf(itemId);
                string path = string.Join(" → ", _stack.Skip(cycleStart).Append(itemId));
                Warnings.Add(new CalculationWarning(
                    WarningCode.CycleDetected,
                    $"循環依存を検出したため展開を打ち切りました: {path}"));
                Unmet[itemId] = Get(Unmet, itemId) + net;
                return;
            }

            if (!Selection.TryGetValue(itemId, out PairSelector.Selection? selection))
            {
                selection = PairSelector.Select(itemId, _master, _context, _overrides, Warnings);
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
                if (IsRawMaterial(itemId))
                {
                    Raw[itemId] = Get(Raw, itemId) + net;
                }
                else
                {
                    Unmet[itemId] = Get(Unmet, itemId) + net;
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
            double delta = net / outputQty;
            if (!double.IsFinite(delta) || delta <= 0)
            {
                Unmet[itemId] = Get(Unmet, itemId) + net;
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

        private bool IsRawMaterial(string itemId) =>
            _master.ItemsById.TryGetValue(itemId, out Item? item) && item.Category == "基礎素材";

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
        /// 環境の消費アイテム需要（ConsumeRatePerSecond×60×台数）と
        /// 固定消費需要（RatePerSecond×60×設備の切上げ台数）の合計（個/分）。
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
                    + env.ConsumeRatePerSecond * 60.0 * count;
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
                    + fixedConsumption.RatePerSecond * 60.0 * ceilCount;
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
                supplies.Add(new SupplyPortion(SupplyKind.RawMaterial, null, raw));
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
                env.ConsumeRatePerSecond * dispenserCount));

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

        var flowAdjustments = BuildFlowAdjustments(session, facilityRequirements);

        WarningBag warnings = session.Warnings;
        AddTransportWarnings(master, session, warnings);

        return new ProductionPlan
        {
            ItemRequirements = itemRequirements,
            FacilityRequirements = facilityRequirements,
            RecipeRuns = recipeRuns,
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
    /// CeilCount &gt; ExactCount の設備に属するレシピの入力について推奨流量制限を出力する。
    /// </summary>
    private static List<FlowAdjustment> BuildFlowAdjustments(
        Session session,
        List<FacilityRequirement> facilityRequirements)
    {
        var adjustments = new Dictionary<(string RecipeId, string InputItemId), double>();
        var facilitiesWithSlack = new HashSet<string>(
            facilityRequirements
                .Where(f => f.CeilCount > f.ExactCount + Epsilon)
                .Select(f => f.FacilityId),
            StringComparer.Ordinal);

        foreach (PairSelector.Selection run in session.RunOrder)
        {
            if (!facilitiesWithSlack.Contains(run.Pair.FacilityId))
            {
                continue;
            }

            foreach (RecipeInput input in run.Recipe.Inputs)
            {
                var key = (run.Recipe.Id, input.ItemId);
                adjustments[key] = adjustments.GetValueOrDefault(key)
                    + session.RunCycles[run] * input.Quantity / 60.0;
            }
        }

        return adjustments
            .Where(kv => kv.Value > Epsilon)
            .Select(kv => new FlowAdjustment(kv.Key.RecipeId, kv.Key.InputItemId, kv.Value, kv.Value))
            .ToList();
    }

    /// <summary>
    /// 需要または生産のあったアイテムについて、その流量が
    /// 輸送媒体（ベルト 30 個/s・パイプ 60 個/s）の上限を超える場合に警告を追加する。
    /// </summary>
    private static void AddTransportWarnings(
        MasterDataSnapshot master,
        Session session,
        WarningBag warnings)
    {
        IEnumerable<string> itemIds = session.Demand.Keys
            .Union(session.Produced.Keys)
            .Union(session.Raw.Keys);

        foreach (string itemId in itemIds)
        {
            if (!master.ItemsById.TryGetValue(itemId, out Item? item))
            {
                continue;
            }

            double limit = item.TransportKind switch
            {
                TransportKind.Belt => BeltCapacityPerSecond,
                TransportKind.Pipe => PipeCapacityPerSecond,
                _ => 0,
            };
            if (limit <= 0)
            {
                continue;
            }

            double flowPerSecond = Math.Max(
                GetFrom(session.Demand, itemId), GetFrom(session.Produced, itemId)) / 60.0;
            flowPerSecond = Math.Max(flowPerSecond, GetFrom(session.Raw, itemId) / 60.0);

            if (flowPerSecond > limit + Epsilon)
            {
                int lanes = (int)Math.Ceiling(flowPerSecond / limit - 1e-9);
                warnings.Add(new CalculationWarning(
                    WarningCode.TransportCapacityExceeded,
                    $"アイテム {itemId} の必要流量 {flowPerSecond:F2} 個/s が輸送容量（{item.TransportKind} {limit:F0} 個/s）を超えています。必要レーン数: {lanes}"));
            }
        }
    }
}
