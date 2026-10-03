namespace EndfieldAicWeb.Domain.Calculation;

/// <summary>
/// 計算中に発生した警告（例外化せず ProductionPlan.Warnings に収集する）。
/// </summary>
public sealed record CalculationWarning(WarningCode Code, string Message);

/// <summary>
/// 警告コード。Message は日本語で利用者向けの説明を持つ。
/// </summary>
public enum WarningCode
{
    /// <summary>需要展開で循環依存を検出し、解放反復でも解けず未充足が残った（仕様決定 AQ）。</summary>
    CycleDetected,

    /// <summary>選択可能なレシピが存在しないアイテム（採取素材以外）。</summary>
    NoRecipeAvailable,

    /// <summary>PairOverride が不適格でデフォルト選択へフォールバックした。</summary>
    InvalidPairOverride,

    /// <summary>同一レシピに別ペアが選ばれたため、先に確定したペアを採用した（旧 BN）。</summary>
    PairConflict,

    /// <summary>アイテムの所属イベントが非有効で生産・外部調達とも不能（仕様決定 X）。</summary>
    EventItemUnavailable,

    /// <summary>semver としてパース不能なバージョン文字列があった。</summary>
    InvalidVersionString,

    /// <summary>EnvironmentCountOverride が存在しない環境を指した。</summary>
    InvalidEnvironmentOverride,

    /// <summary>追加需要の反復が上限回数内に収束しなかった（docs/implementation-plan.md §3-8）。</summary>
    ConvergenceNotReached,

    /// <summary>採取上限を超える需要があり、代替レシピもなかった（仕様決定 AD）。</summary>
    GatherCapExceeded,

    /// <summary>選択マップの所属イベントが非有効で、採取素材がすべて採取不可（仕様決定 AD、X と同型）。</summary>
    GatherMapUnavailable,

    /// <summary>MapId が存在しないマップを指した。</summary>
    InvalidGatherMap,

    /// <summary>GatherRateOverride が採取素材でないアイテムを指す、または値が不正で無視した。</summary>
    InvalidGatherRateOverride,

    /// <summary>散布機台数の上書きがカバーできる機械数を下回り、環境を要する生産が削られた（仕様決定 BR）。</summary>
    EnvironmentCoverageExceeded,
}

/// <summary>
/// 同一内容の警告を重複登録しない収集器。
/// 複数アイテム・複数反復で同一警告が発生し得るため重複を抑止する。
/// </summary>
internal sealed class WarningBag : ICollection<CalculationWarning>
{
    private readonly List<CalculationWarning> _list = [];
    private readonly HashSet<CalculationWarning> _seen = [];

    public int Count => _list.Count;
    public bool IsReadOnly => false;

    public void Add(CalculationWarning item)
    {
        if (_seen.Add(item))
        {
            _list.Add(item);
        }
    }

    public IReadOnlyList<CalculationWarning> AsList() => _list;

    public void Clear() { _list.Clear(); _seen.Clear(); }
    public bool Contains(CalculationWarning item) => _seen.Contains(item);
    public void CopyTo(CalculationWarning[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);
    public IEnumerator<CalculationWarning> GetEnumerator() => _list.GetEnumerator();
    public bool Remove(CalculationWarning item) { _seen.Remove(item); return _list.Remove(item); }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
