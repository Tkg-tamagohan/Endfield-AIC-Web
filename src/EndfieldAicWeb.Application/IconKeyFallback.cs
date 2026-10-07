using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application;

/// <summary>
/// IconKey 未設定時のフォールバック規則。
/// レシピは主出力（SortOrder 最小）アイテムの IconKey を実効キーとする。
/// </summary>
public static class IconKeyFallback
{
    /// <summary>
    /// エンティティの実効 IconKey。自身に設定があればそれを返し、
    /// 未設定のレシピは主出力アイテムのキーへフォールバックする。
    /// アイテムの参照は <paramref name="findItem"/> で呼び出し側の索引を使う。
    /// </summary>
    public static string? EffectiveIconKey(MasterEntity entity, Func<string, Item?> findItem)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(findItem);

        if (!string.IsNullOrEmpty(entity.IconKey))
        {
            return entity.IconKey;
        }

        if (entity is not Recipe recipe)
        {
            return null;
        }

        RecipeOutput? main = recipe.Outputs.OrderBy(o => o.SortOrder).FirstOrDefault();
        return main is null ? null : findItem(main.ItemId)?.IconKey;
    }
}
