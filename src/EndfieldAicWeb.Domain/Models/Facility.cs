namespace EndfieldAicWeb.Domain.Models;

/// <summary>
/// 設備（加工機・散布機等）。計算ドメインでは大きさと消費電力のみを持つ（仕様決定 G/W）。
/// 発電識別・保持枠・ポート・衝突クラスは持たない。
/// </summary>
public class Facility : MasterEntity
{
    /// <summary>設備の幅。「設備面積最小」最適化（F）のために保持する。</summary>
    public double Width { get; set; }

    /// <summary>設備の高さ。</summary>
    public double Height { get; set; }

    /// <summary>消費電力。</summary>
    public double PowerConsumption { get; set; }
}
