namespace EndfieldAicWeb.Domain.Models;

/// <summary>
/// アイテムの輸送種別。None は「電力」のような輸送しない仮想アイテム用。
/// </summary>
public enum TransportKind
{
    None,
    Belt,
    Pipe,
}
