namespace EndfieldAicWeb.Domain.Models;

/// <summary>
/// アイコンマニフェストの 1 エントリ（仕様決定 R）。ファイルの整合は Sha256/Bytes で照合する。
/// </summary>
public class IconEntry
{
    public required string Key { get; set; }
    public required string File { get; set; }
    public required string Sha256 { get; set; }
    public long Bytes { get; set; }
}
