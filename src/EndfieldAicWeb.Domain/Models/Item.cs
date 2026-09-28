namespace EndfieldAicWeb.Domain.Models;

/// <summary>
/// アイテム（素材・中間品・イベント限定品・仮想アイテム「電力」等）。
/// </summary>
public class Item : MasterEntity
{
    /// <summary>
    /// 分類。「基礎素材」は需要展開の終端（外部調達扱い）。
    /// </summary>
    public required string Category { get; set; }

    /// <summary>輸送種別。None は輸送容量対象外の仮想アイテム。</summary>
    public TransportKind TransportKind { get; set; }

    /// <summary>所属イベント。null は常設。非有効時は生産・外部調達とも不可（仕様決定 X）。</summary>
    public string? GameEventId { get; set; }
}
