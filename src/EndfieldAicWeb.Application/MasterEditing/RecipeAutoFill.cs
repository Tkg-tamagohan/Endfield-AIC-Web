using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.MasterEditing;

/// <summary>
/// レシピの Id・名前の自動提案（仕様決定 BU）。直近に適用した提案値を保持し、
/// フィールドの現在値がそれと一致する間だけ主産物の変更へ追随して新しい提案値へ更新する。
/// 手動編集で一致しなくなったフィールドは固定になり、提案値と同じ値を入れ直すと追随へ復帰する。
/// 追随判定は「現在値 == 直近提案値」の比較だけで行い、編集履歴の別フラグは持たない。
/// </summary>
public sealed class RecipeAutoFill
{
    private string? _lastId;
    private string? _lastName;

    /// <summary>
    /// 現在の提案値（名前 = 主産物アイテム名、Id = <c>recipe-&lt;slug&gt;</c>）。
    /// 主産物を解決できないとき（Outputs が空、または ItemId が文書に存在しない）は null。
    /// Id 採番の衝突判定からは編集中レシピ自身を除く。
    /// </summary>
    public static (string Id, string Name)? Suggest(MasterDocument doc, Recipe recipe)
    {
        // 出力なしレシピ（処理レシピ）は自動提案の対象外（仕様決定 CE）。
        if (recipe.Outputs.Count == 0)
        {
            return null;
        }

        Item? main = EntityFactory.MainProductItem(doc, recipe);
        if (main is null)
        {
            return null;
        }

        IEnumerable<string> otherIds = doc.Recipes
            .Where(r => !ReferenceEquals(r, recipe))
            .Select(r => r.Id);
        return (EntityFactory.SuggestRecipeId(otherIds, main.Id), main.Name);
    }

    /// <summary>
    /// 選択中レシピの切替時に呼ぶ初期化。直近提案値を現在の提案値で置き直す（適用はしない）。
    /// 値が提案値と一致するレシピだけが追随対象になり、一致しない値は固定のままになる。
    /// </summary>
    public void Reset(MasterDocument doc, Recipe recipe)
    {
        (string Id, string Name)? suggestion = Suggest(doc, recipe);
        _lastId = suggestion?.Id;
        _lastName = suggestion?.Name;
    }

    /// <summary>
    /// 主産物の再解決後に呼ぶ追随処理。直近提案値と一致するフィールドを新しい提案値へ更新する。
    /// 直近提案値がない（提案を一度も計算できていない）場合は、工場生成時の仮値
    /// （採番された <c>recipe-NNN</c>・「新規レシピ」）のままのフィールドだけを未編集とみなして置き換える。
    /// 仮値と同じ形式でも手入力・インポートの値は置き換えない。
    /// Id を変更したとき true を返す（呼び出し側でペアの RecipeId 伝搬を行う）。
    /// </summary>
    public bool Follow(MasterDocument doc, Recipe recipe)
    {
        (string Id, string Name)? suggestion = Suggest(doc, recipe);
        if (suggestion is null)
        {
            return false;
        }

        bool idChanged = false;
        bool idFollows = recipe.Id == _lastId
            || (_lastId is null && EntityFactory.IsUntouchedPlaceholderRecipeId(recipe));
        if (idFollows && recipe.Id != suggestion.Value.Id)
        {
            recipe.Id = suggestion.Value.Id;
            idChanged = true;
        }

        bool nameFollows = recipe.Name == _lastName
            || (_lastName is null && EntityFactory.IsUntouchedPlaceholderRecipeName(recipe));
        if (nameFollows)
        {
            recipe.Name = suggestion.Value.Name;
        }

        _lastId = suggestion.Value.Id;
        _lastName = suggestion.Value.Name;
        return idChanged;
    }

    /// <summary>
    /// 提案ボタン。手動編集後も Id・名前を現在の提案値へ戻す（適用後は追随状態へ復帰する）。
    /// Id を変更したとき true を返す。
    /// </summary>
    public bool ForceApply(MasterDocument doc, Recipe recipe)
    {
        (string Id, string Name)? suggestion = Suggest(doc, recipe);
        if (suggestion is null)
        {
            return false;
        }

        bool idChanged = recipe.Id != suggestion.Value.Id;
        recipe.Id = suggestion.Value.Id;
        recipe.Name = suggestion.Value.Name;
        _lastId = suggestion.Value.Id;
        _lastName = suggestion.Value.Name;
        return idChanged;
    }
}
