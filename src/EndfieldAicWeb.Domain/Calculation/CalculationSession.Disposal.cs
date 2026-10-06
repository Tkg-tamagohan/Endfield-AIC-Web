using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Domain.Calculation;

// 処理需要の再評価と処理ランの台数確定（ReevaluateDisposal、RecomputeDisposalRun）。
internal sealed partial class CalculationSession
{
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

        // 処理対象の起点・上限・確定の対象になる入力か。計算目標のアイテム（CA）と
        // イベントが非有効なアイテム（供給不能のため、仕様決定 X 準用）は外す。
        // いずれの入力にも処理需要は計上する（補助入力と同型の帳簿付け。非有効な
        // 入力は通常の展開で未充足になる、Devin Review の指摘に基づく暫定解釈）。
        bool IsDisposalInput(string itemId) =>
            !targetItemIds.Contains(itemId) && !IsItemInactive(itemId);

        // 処理ランごとの消費予定の合算。全入力を計上し、処理対象入力の上限は
        // 残り余剰から他ランの消費分を除いた残量とする。
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
            // 計算目標のアイテムとイベントが非有効なアイテムは余剰があっても処理対象外
            // （CA・仕様決定 X 準用。非有効アイテムの生産量は供給として認められない）。
            if (!IsDisposalInput(itemId))
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

            runsChanged |= RecomputeDisposalRun(run, Remaining, consumed, IsDisposalInput);
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
        Dictionary<string, double> consumed,
        Func<string, bool> isDisposalInput)
    {
        Recipe recipe = run.Recipe;

        // サイクル数を確定し直すため、このランの既存消費分を先に帳簿から外す
        // （全入力分を戻す。計算目標・非有効イベント所属の入力にも需要を計上するため）。
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
            // 計算目標・非有効イベント所属の入力は処理対象の起点にならないため
            // 上限には数えない（需要は別途全入力に計上する、CA・仕様決定 X 準用）。
            if (!isDisposalInput(input.ItemId))
            {
                continue;
            }

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
                // 消費予定は全入力を計上する。計算目標・非有効イベント所属の入力も
                // 帳簿には載せ、展開で通常需要（非有効は未充足）として扱う。
                consumed[input.ItemId] = consumed.GetValueOrDefault(input.ItemId) + cycles * input.Quantity;

                // 残り余剰がある入力をこのランの処理対象として確定する
                // （他ランの処理対象はそのランに属するため移さない。
                //  計算目標・非有効イベント所属の入力は確定対象にしない）。
                if (!isDisposalInput(input.ItemId)
                    || remaining(input.ItemId) <= ProductionCalculator.Epsilon
                    || _disposalRecipeByItem.ContainsKey(input.ItemId))
                {
                    continue;
                }
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
}
