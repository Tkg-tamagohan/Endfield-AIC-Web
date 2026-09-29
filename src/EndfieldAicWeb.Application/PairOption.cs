using System.Globalization;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application;

/// <summary>
/// ペア代替選択の候補 1 件。Key はペア行の一意キー（全要素の組、仕様決定 P）の文字列表現で、
/// ドロップダウンの値と現在値の照合に使う。
/// </summary>
public sealed record PairOption(
    string Key,
    string RecipeId,
    string FacilityId,
    double CycleTime,
    string? EnvironmentId,
    FixedConsumption? FixedConsumption,
    bool IsDefault)
{
    /// <summary>この候補を選ぶための上書き指定を作る。</summary>
    public PairOverride ToOverride(string itemId) =>
        new(itemId, RecipeId, FacilityId, CycleTime, EnvironmentId, FixedConsumption);
}

/// <summary>
/// ペア行の一意キーを文字列化する。Id に区切り文字を含まれても衝突しないよう
/// 制御文字（US）で連結する。
/// </summary>
public static class PairOptionKey
{
    private const char Separator = '';

    public static string Create(string recipeId, RecipeFacility pair) =>
        string.Join(Separator,
            recipeId,
            pair.FacilityId,
            pair.CycleTime.ToString("G17", CultureInfo.InvariantCulture),
            pair.EnvironmentId ?? "",
            pair.FixedConsumption?.ItemId ?? "",
            (pair.FixedConsumption?.RatePerSecond ?? 0).ToString("G17", CultureInfo.InvariantCulture));

    public static string Create(Recipe recipe, RecipeFacility pair) => Create(recipe.Id, pair);

    public static string Create(PairSelection selection) => Create(selection.RecipeId, selection.Pair);
}
