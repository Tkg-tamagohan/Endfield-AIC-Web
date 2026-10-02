using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application;

/// <summary>
/// 生産リストのアイテム選択用の前方一致・部分一致検索。
/// 件数が多いため（仕様上 200 種程度）テキストで絞り込む。
/// </summary>
public static class ItemSearch
{
    /// <summary>クエリでアイテムを絞り込む。空クエリは全件、名前と Id の部分一致（大小無視）。カテゴリ指定時はカテゴリ一致と AND で合成する。</summary>
    public static IReadOnlyList<Item> Filter(IReadOnlyList<Item> items, string? query, string? category = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        IEnumerable<Item> result = ItemCatalog.FilterByCategory(items, category);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result.ToList();
        }

        string q = query.Trim();
        return result
            .Where(i =>
                i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Id.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
