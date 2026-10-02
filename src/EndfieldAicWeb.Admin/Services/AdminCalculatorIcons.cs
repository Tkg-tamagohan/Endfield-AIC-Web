using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.SharedUi;

namespace EndfieldAicWeb.Admin.Services;

/// <summary>
/// 共有 UI のアイコン解決を AdminDocumentService のアイコンストアへ橋渡しする。
/// 編集中データ由来のアイコン（プレビュー用 data: URI）を返す。
/// </summary>
public sealed class AdminCalculatorIcons(AdminDocumentService store) : ICalculatorIcons
{
    /// <summary>IconKey のプレビュー用 data: URI。未解決は null（プレースホルダ表示）。</summary>
    public string? Url(string? iconKey) => store.IconDataUrl(iconKey);

    /// <summary>レシピの実効 IconKey。未設定時は主出力（SortOrder 最小）アイテムのキーへフォールバックする。</summary>
    public string? RecipeIconKey(string recipeId)
    {
        Recipe? recipe = store.Document?.Recipes.FirstOrDefault(r => r.Id == recipeId);
        return recipe is null ? null : store.EffectiveIconKey(recipe);
    }
}
