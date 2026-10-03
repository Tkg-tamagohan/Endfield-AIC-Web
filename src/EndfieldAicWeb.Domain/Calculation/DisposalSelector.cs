using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Domain.Calculation;

/// <summary>
/// 処理対象アイテムに対して使用する処理レシピ（出力なしレシピ）とペアの組を 1 件選択する
/// （仕様決定 CB）。<see cref="PairSelector"/> と同じ適格判定を使うが、選択キーは
/// 対象アイテムの実効処理レート（1 サイクルの対象入力量 ÷ 適格ペアの最小 CycleTime ×60）。
/// 選択は需要アイテムの産出レシピ選択（Selection）とは別系で行い、そちらには登録しない。
/// </summary>
internal static class DisposalSelector
{
    /// <summary>
    /// itemId を入力に持つ出力なしレシピのうちコンテキスト上 eligible なものから 1 組を選ぶ。
    /// レシピ順位は VersionAdded 最新 → 実効処理レート最大 → Id 昇順。
    /// ペア順位は既定選択規則（U・BT）に従う。候補が 0 件なら null。
    /// </summary>
    internal static PairSelector.Selection? Select(
        string itemId,
        MasterDataSnapshot master,
        ContextFilter context,
        ICollection<CalculationWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(warnings);

        IEnumerable<Recipe> candidates = master.DisposalRecipesByInputItemId.TryGetValue(itemId, out IReadOnlyList<Recipe>? list)
            ? list.Where(r => IsEventEligible(r.GameEventId, context))
            : [];

        List<Recipe> ordered = candidates
            .Select(r => (
                Recipe: r,
                Version: ParseVersionOrOldest(r.Id, r.VersionAdded, warnings),
                Rate: EffectiveDisposalRate(r, itemId, master, context)))
            .OrderByDescending(t => t.Version is not null)
            .ThenByDescending(t => t.Version)
            .ThenByDescending(t => t.Rate)
            .ThenBy(t => t.Recipe.Id, StringComparer.Ordinal)
            .Select(t => t.Recipe)
            .ToList();

        foreach (Recipe recipe in ordered)
        {
            List<RecipeFacility> eligiblePairs = PairSelector.EligiblePairs(recipe, master, context);
            if (eligiblePairs.Count > 0)
            {
                return new PairSelector.Selection(recipe, PairSelector.ChooseDefaultPair(eligiblePairs));
            }
        }

        return null;
    }

    /// <summary>
    /// 対象アイテムの実効処理レート（個/分）。
    /// 1 サイクルの対象入力量合計 ÷ 適格ペアの最小 CycleTime ×60。適格ペア 0 件は 0。
    /// </summary>
    private static double EffectiveDisposalRate(
        Recipe recipe,
        string itemId,
        MasterDataSnapshot master,
        ContextFilter context)
    {
        double minCycle = double.PositiveInfinity;
        foreach (RecipeFacility pair in recipe.Facilities)
        {
            if (pair.CycleTime > 0 && pair.CycleTime < minCycle && IsPairEligible(pair, master, context))
            {
                minCycle = pair.CycleTime;
            }
        }

        if (!double.IsFinite(minCycle))
        {
            return 0;
        }

        double inputQty = recipe.Inputs
            .Where(i => i.ItemId == itemId)
            .Sum(i => i.Quantity);
        return inputQty / minCycle * 60;
    }

    /// <summary>semver としてパースできた場合のみ値を返す。パース不能は警告を出して null（最古扱い）。</summary>
    private static SemVersion? ParseVersionOrOldest(
        string recipeId,
        string? versionText,
        ICollection<CalculationWarning> warnings)
    {
        if (SemVersion.TryParse(versionText, out SemVersion parsed))
        {
            return parsed;
        }

        warnings.Add(new CalculationWarning(
            WarningCode.InvalidVersionString,
            $"レシピ {recipeId} の VersionAdded {versionText} は semver としてパースできないため、最古として扱います。"));
        return null;
    }

    private static bool IsPairEligible(
        RecipeFacility pair,
        MasterDataSnapshot master,
        ContextFilter context)
    {
        if (pair.EnvironmentId is null)
        {
            return true;
        }

        if (!master.EnvironmentsById.TryGetValue(pair.EnvironmentId, out Models.Environment? environment))
        {
            return false;
        }

        return IsEventEligible(environment.GameEventId, context);
    }

    private static bool IsEventEligible(string? gameEventId, ContextFilter context)
    {
        return gameEventId is null || context.ActiveGameEventIds.Contains(gameEventId);
    }
}
