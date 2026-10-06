using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;

namespace EndfieldAicWeb.Domain.Calculation;

// 循環検出と残差解放・ロールバック（RecordCycle、IsReleasable、ReleaseCycleResiduals、ReleaseSnapshot）。
internal sealed partial class CalculationSession
{
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

        string path = string.Join(" → ", _stack.Skip(cycleStart).Append(itemId).Select(_display.Item));
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
}
