namespace EndfieldAicWeb.Domain.Models;

/// <summary>
/// レシピ。複数入力・複数出力（副産物）を持ち、実行可能な設備とのペアを複数持つ。
/// </summary>
public class Recipe : MasterEntity
{
    /// <summary>所属イベント。null は常設レシピ。</summary>
    public string? GameEventId { get; set; }

    /// <summary>入力素材。</summary>
    public List<RecipeInput> Inputs { get; set; } = [];

    /// <summary>成果物。SortOrder=0 が主産物。</summary>
    public List<RecipeOutput> Outputs { get; set; } = [];

    /// <summary>実行可能な設備とのペア一覧。</summary>
    public List<RecipeFacility> Facilities { get; set; } = [];
}
