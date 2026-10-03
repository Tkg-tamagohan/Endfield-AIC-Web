namespace EndfieldAicWeb.Domain.Models;

/// <summary>
/// 環境（仕様決定 H）。消費アイテムを継続消費する供給設備（散布機）が範囲内の
/// 設備へ専用レシピを開放する。カバー範囲は持たない（W）が、1 台が供給できる
/// 機械台数の上限は <see cref="CoverableMachines"/> で持つ（BP）。
/// </summary>
public class Environment : MasterEntity
{
    /// <summary>環境を供給する設備（ガス散布機等）。</summary>
    public required string ProviderFacilityId { get; set; }

    /// <summary>継続消費するアイテム（ガス等）。</summary>
    public required string ConsumeItemId { get; set; }

    /// <summary>消費速度（個/分）。</summary>
    public double ConsumeRatePerMinute { get; set; }

    /// <summary>
    /// 供給設備 1 台が環境を供給できる機械台数（正の整数、仕様決定 BP）。
    /// 指定台数との積がその環境を要する機械数の上限になる（BR）。
    /// </summary>
    public required int CoverableMachines { get; set; }

    /// <summary>所属イベント。null は常設。</summary>
    public string? GameEventId { get; set; }
}
