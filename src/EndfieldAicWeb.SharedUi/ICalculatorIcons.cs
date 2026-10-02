namespace EndfieldAicWeb.SharedUi;

/// <summary>
/// 共有 UI が必要とするアイコン解決の抽象。アイコンの取得元はアプリごとに異なるため、
/// 公開版は <c>IconCatalog</c>、管理ツールは <c>AdminDocumentService</c> を包む実装を DI で注入する。
/// </summary>
public interface ICalculatorIcons
{
    /// <summary>IconKey を img 用の URL（data: URI 等）へ解決する。未解決は null。</summary>
    string? Url(string? iconKey);

    /// <summary>レシピの実効 IconKey。未設定時は主出力アイテムのキーへフォールバックする。</summary>
    string? RecipeIconKey(string recipeId);
}
