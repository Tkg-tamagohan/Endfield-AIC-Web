namespace EndfieldAicWeb.Domain.Models;

/// <summary>
/// 採取上限を持つマップ。マップごとに採取可能な素材と上限を定義する。
/// </summary>
public class GameMap : MasterEntity
{
    /// <summary>採取レート。</summary>
    public List<GatherRate> GatherRates { get; set; } = [];

    /// <summary>所属イベント。null は常設マップ。</summary>
    public string? GameEventId { get; set; }
}
