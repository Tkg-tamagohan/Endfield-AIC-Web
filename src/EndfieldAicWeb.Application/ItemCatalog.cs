using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application;

/// <summary>
/// アイテム選択補助。カテゴリ列挙・カテゴリ絞り込み・候補の母集団制限を純粋関数で提供する。
/// </summary>
public static class ItemCatalog
{
    /// <summary>継続消費系の選択肢とするカテゴリ（環境消費・固定消費、仕様決定 AT）。</summary>
    public static readonly IReadOnlyList<string> FuelCategories = ["気体", "液体"];

    /// <summary>レシピの成果物として登場するアイテムだけを返す（生産リストの候補、仕様決定 AT）。</summary>
    public static IReadOnlyList<Item> WithRecipeOutput(IReadOnlyList<Item> items, IReadOnlyList<Recipe> recipes)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(recipes);

        var outputIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (Recipe recipe in recipes)
        {
            foreach (RecipeOutput output in recipe.Outputs)
            {
                outputIds.Add(output.ItemId);
            }
        }

        return items.Where(i => outputIds.Contains(i.Id)).ToList();
    }

    /// <summary>継続消費系の母集団（「気体」「液体」カテゴリのアイテム、仕様決定 AT）を返す。</summary>
    public static IReadOnlyList<Item> FuelItems(IReadOnlyList<Item> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var fuel = new HashSet<string>(FuelCategories, StringComparer.Ordinal);
        return items.Where(i => i.Category is not null && fuel.Contains(i.Category)).ToList();
    }

    /// <summary>文書内のカテゴリを、空を除き初出順に重複除去して返す。</summary>
    public static IReadOnlyList<string> Categories(IReadOnlyList<Item> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var categories = new List<string>();
        foreach (Item item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.Category) && seen.Add(item.Category))
            {
                categories.Add(item.Category);
            }
        }

        return categories;
    }

    /// <summary>カテゴリでアイテムを絞り込む。null または空なら全件を返す。</summary>
    public static IReadOnlyList<Item> FilterByCategory(IReadOnlyList<Item> items, string? category)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (string.IsNullOrEmpty(category))
        {
            return items;
        }

        return items.Where(i => i.Category == category).ToList();
    }

    /// <summary>
    /// 絞り込み結果の選択肢を返す。現在値のアイテムが結果に無ければ末尾へ追加する。
    /// ネイティブ select は値が選択肢にないと先頭候補を表示するため、既存行が別アイテム表示に化けないよう残す。
    /// 現在値の検索先は <paramref name="lookupItems"/>（省略時は <paramref name="items"/>）。
    /// 母集団を制限した欄でも母集団外の既存値を残せるよう、呼び出し側は文書の全アイテムを渡す。
    /// </summary>
    public static IReadOnlyList<Item> OptionsForSelection(
        IReadOnlyList<Item> items,
        string? category,
        string? currentItemId,
        IReadOnlyList<Item>? lookupItems = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        IReadOnlyList<Item> filtered = FilterByCategory(items, category);
        if (string.IsNullOrEmpty(currentItemId) || filtered.Any(i => i.Id == currentItemId))
        {
            return filtered;
        }

        Item? current = (lookupItems ?? items).FirstOrDefault(i => i.Id == currentItemId);
        return current is null ? filtered : [.. filtered, current];
    }
}
