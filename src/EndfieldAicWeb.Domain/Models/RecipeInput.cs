namespace EndfieldAicWeb.Domain.Models;

/// <summary>
/// レシピの入力素材（ItemId＋個数）。
/// </summary>
public class RecipeInput
{
    public required string ItemId { get; set; }
    public double Quantity { get; set; }
}
