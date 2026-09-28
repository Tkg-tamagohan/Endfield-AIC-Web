namespace EndfieldAicWeb.Domain.Models;

/// <summary>
/// 固定消費（仕様決定 J）。設備が稼働するだけで消費される動力素材の継続消費。
/// </summary>
public class FixedConsumption
{
    public required string ItemId { get; set; }

    /// <summary>消費速度（個/s）。台数に比例して需要へ追加する。</summary>
    public double RatePerSecond { get; set; }
}
