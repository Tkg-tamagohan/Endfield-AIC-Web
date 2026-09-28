namespace EndfieldAicWeb.Domain.Models;

/// <summary>
/// RecipeFacility（ペア）。レシピと設備の紐付けで、環境条件・固定消費を含む。
/// 一意性は全要素の組（RecipeId・FacilityId・CycleTime・EnvironmentId・FixedConsumption）で判定する（仕様決定 P）。
/// </summary>
public class RecipeFacility
{
    /// <summary>対象レシピ。JSON では所属レシピから与えられる。</summary>
    public required string RecipeId { get; set; }

    /// <summary>対象設備。</summary>
    public required string FacilityId { get; set; }

    /// <summary>このペアでの 1 サイクル秒数。</summary>
    public double CycleTime { get; set; }

    /// <summary>稼働に必要な環境。null は環境不要。</summary>
    public string? EnvironmentId { get; set; }

    /// <summary>稼働中の継続消費素材（任意、仕様決定 J）。</summary>
    public FixedConsumption? FixedConsumption { get; set; }
}
