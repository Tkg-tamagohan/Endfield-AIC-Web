using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;

namespace EndfieldAicWeb.Domain.Calculation;

// 展開・引き戻し・端末処理（Expand、Retract、TrimTerminal、採取優先の端末計上）。
internal sealed partial class CalculationSession
{
    /// <summary>採取素材ごとの有効採取上限（個/分、PositiveInfinity は上限なし）。構築時に一度だけ解決する。</summary>
    private readonly Dictionary<string, double> _gatherCaps;

    /// <summary>
    /// 採取上限を超えて未充足になった採取素材。警告は均衡化後の最終 Unmet で発行するため
    /// ここでは候補のみ記録する（後の供給や引き戻しで不足が解消されることがある）。
    /// </summary>
    private readonly HashSet<string> _gatherCapShortfall = new(StringComparer.Ordinal);

    private readonly List<string> _stack = [];
    private readonly HashSet<string> _stackSet = new(StringComparer.Ordinal);

    /// <summary>採取素材の残り採取可能量（個/分）。採取素材以外や上限の解決漏れは 0。</summary>
    private double GatherRemaining(string itemId) =>
        Math.Max(0.0, _gatherCaps.GetValueOrDefault(itemId) - ProductionCalculator.GetOrZero(Raw, itemId));

    /// <summary>未充足の計上。全原因の寄与を unmetAdded 帳簿へ併記する（BR の比例配分用）。</summary>
    private void AddUnmet(string itemId, double amount)
    {
        Unmet[itemId] = ProductionCalculator.GetOrZero(Unmet, itemId) + amount;
        _unmetAdded[itemId] = _unmetAdded.GetValueOrDefault(itemId) + amount;
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
                    $"アイテム {_display.Item(itemId)} はイベント {_display.GameEvent(_master.ItemsById[itemId].GameEventId!)} が有効でないため生産・調達できません。"));
            }

            return;
        }

        double net = ProductionCalculator.GetOrZero(Demand, itemId) - ProductionCalculator.GetOrZero(Produced, itemId) - ProductionCalculator.GetOrZero(Raw, itemId) - ProductionCalculator.GetOrZero(Unmet, itemId);
        if (net <= ProductionCalculator.Epsilon)
        {
            return;
        }

        // 基礎素材の指定アイテムは需要展開の終端とし、正味需要の全量を外部調達（Raw）へ計上する
        // （仕様決定 CZ）。採取分岐より先に打ち切るため、採取素材が指定集合へ混入した場合も
        // 全量を外部調達として扱う（暫定解釈 1）。循環検出・レシピ選択・ペア上書きは行わない。
        if (SpecifiedBaseItemIds.Contains(itemId))
        {
            Raw[itemId] = ProductionCalculator.GetOrZero(Raw, itemId) + net;
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
                        $"アイテム {_display.Item(itemId)} のレシピ {_display.Recipe(selection.Recipe.Id)} には既に別のペアが稼働中のため、先に確定したペア（{_display.Facility(running.Pair.FacilityId)}）を採用します。"));
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
                    $"アイテム {_display.Item(itemId)} を生産できるレシピがありません。"));
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
                $"アイテム {_display.Item(itemId)} のレシピ {_display.Recipe(recipe.Id)} の出力数量が 0 以下のため生産できません。"));
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

    private bool IsGatherable(string itemId) =>
        _master.ItemsById.TryGetValue(itemId, out Item? item) && item.IsGatherable;
}
