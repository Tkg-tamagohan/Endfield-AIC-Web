namespace EndfieldAicWeb.Domain.Models;

/// <summary>
/// 期間限定イベント（仕様決定 T）。ActiveFrom/ActiveTo が両方 null なら常設。
/// </summary>
public class GameEvent : MasterEntity
{
    public DateTime? ActiveFrom { get; set; }
    public DateTime? ActiveTo { get; set; }
}
