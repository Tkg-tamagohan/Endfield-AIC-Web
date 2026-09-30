namespace EndfieldAicWeb.Domain.Models;

/// <summary>
/// アイテム（素材・中間品・イベント限定品・仮想アイテム「電力」等）。
/// </summary>
public class Item : MasterEntity
{
    /// <summary>
    /// 分類。表示用タグであり、計算の判定には使わない。
    /// </summary>
    public required string Category { get; set; }

    /// <summary>需要展開の終端となる採取素材か。true なら採取（外部調達）扱い。</summary>
    public bool IsGatherable { get; set; }

    /// <summary>輸送種別。None は輸送容量対象外の仮想アイテム。</summary>
    public TransportKind TransportKind { get; set; }

    /// <summary>所属イベント。null は常設。非有効時は生産・外部調達とも不可（仕様決定 X）。</summary>
    public string? GameEventId { get; set; }
}
