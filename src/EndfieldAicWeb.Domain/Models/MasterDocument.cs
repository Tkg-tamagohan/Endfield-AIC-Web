namespace EndfieldAicWeb.Domain.Models;

/// <summary>
/// マスタ文書（requirements §5.9）。正本 JSON のルート構造に対応する。
/// </summary>
public class MasterDocument
{
    /// <summary>スキーマ版。新系統の v1 = 1。</summary>
    public int SchemaVersion { get; set; }

    /// <summary>データ版。データ更新で上げる。</summary>
    public required string DataVersion { get; set; }

    public List<Item> Items { get; set; } = [];
    public List<Facility> Facilities { get; set; } = [];
    public List<Environment> Environments { get; set; } = [];
    public List<GameEvent> GameEvents { get; set; } = [];
    public List<Recipe> Recipes { get; set; } = [];
    public List<IconEntry> Icons { get; set; } = [];
}
