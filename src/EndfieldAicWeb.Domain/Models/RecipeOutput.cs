namespace EndfieldAicWeb.Domain.Models;

/// <summary>
/// レシピの出力（ItemId＋個数＋SortOrder）。複数出力・副産物に対応。
/// </summary>
public class RecipeOutput
{
    public required string ItemId { get; set; }
    public double Quantity { get; set; }

    /// <summary>出力の並び順。0 が主産物（レシピアイコンのフォールバック先）。</summary>
    public int SortOrder { get; set; }
}
