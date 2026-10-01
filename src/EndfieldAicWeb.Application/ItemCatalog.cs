using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application;

/// <summary>
/// 管理ツールのアイテム選択補助。カテゴリ列挙とカテゴリ絞り込みを純粋関数で提供する。
/// </summary>
public static class ItemCatalog
{
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
    /// </summary>
    public static IReadOnlyList<Item> OptionsForSelection(
        IReadOnlyList<Item> items,
        string? category,
        string? currentItemId)
    {
        ArgumentNullException.ThrowIfNull(items);

        IReadOnlyList<Item> filtered = FilterByCategory(items, category);
        if (string.IsNullOrEmpty(currentItemId) || filtered.Any(i => i.Id == currentItemId))
        {
            return filtered;
        }

        Item? current = items.FirstOrDefault(i => i.Id == currentItemId);
        return current is null ? filtered : [.. filtered, current];
    }
}
