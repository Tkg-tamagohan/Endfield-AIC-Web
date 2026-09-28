using EndfieldAicWeb.Domain.Models;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Domain.Calculation;

/// <summary>
/// 需要アイテムに対して使用する（レシピ, ペア）の組を 1 件選択する。
/// レシピはコンテキスト適格候補から VersionAdded 最新（同率は Id 昇順）を先に選び、
/// そのレシピの適格ペアから CycleTime 最小を既定とする（仕様決定 F）。
/// 同 CycleTime は EnvironmentId=null → FixedConsumption なし/小 の順（仕様決定 U）。
/// レシピの適格ペアが 0 件なら次点のレシピへ進む（implementation-plan-phase2 §3）。
/// </summary>
public static class PairSelector
{
    private const double Epsilon = 1e-9;

    /// <summary>選択結果。稼働するレシピとその中のペア行。</summary>
    public sealed record Selection(Recipe Recipe, RecipeFacility Pair);

    /// <summary>
    /// itemId を出力するレシピのうちコンテキスト上 eligible なものから 1 組を選ぶ。
    /// 候補が 0 件なら null を返す（呼び出し側で終端処理する）。
    /// </summary>
    public static Selection? Select(
        string itemId,
        MasterDataSnapshot master,
        ContextFilter context,
        IReadOnlyList<PairOverride> overrides,
        ICollection<CalculationWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(warnings);

        IEnumerable<Recipe> candidates = master.RecipesByOutputItemId.TryGetValue(itemId, out List<Recipe>? list)
            ? list.Where(r => IsEventEligible(r.GameEventId, context))
            : [];

        List<Recipe> ordered = OrderCandidates(candidates, warnings);

        PairOverride? pairOverride = overrides.FirstOrDefault(o => o.ItemId == itemId);
        if (pairOverride is not null)
        {
            Selection? overridden = FindOverride(ordered, master, context, pairOverride);
            if (overridden is not null)
            {
                return overridden;
            }

            warnings.Add(new CalculationWarning(
                WarningCode.InvalidPairOverride,
                $"アイテム {itemId} に指定されたペア（{pairOverride.RecipeId} / {pairOverride.FacilityId}）は選択できないため、デフォルト選択へフォールバックします。"));
        }

        foreach (Recipe recipe in ordered)
        {
            List<RecipeFacility> eligiblePairs = EligiblePairs(recipe, master, context);
            if (eligiblePairs.Count > 0)
            {
                return new Selection(recipe, ChooseDefaultPair(eligiblePairs));
            }
        }

        return null;
    }

    /// <summary>適格なペア行（EnvironmentId が null、または環境が存在しイベントが有効）を返す。</summary>
    internal static List<RecipeFacility> EligiblePairs(
        Recipe recipe,
        MasterDataSnapshot master,
        ContextFilter context)
    {
        return recipe.Facilities
            .Where(p => IsPairEligible(p, master, context))
            .ToList();
    }

    /// <summary>
    /// 既定ペア。CycleTime 最小、同率は EnvironmentId=null → FixedConsumption なし/小 → FacilityId 昇順。
    /// </summary>
    internal static RecipeFacility ChooseDefaultPair(IEnumerable<RecipeFacility> eligiblePairs)
    {
        return eligiblePairs
            .OrderBy(p => p.CycleTime)
            .ThenBy(p => p.EnvironmentId is null ? 0 : 1)
            .ThenBy(p => p.FixedConsumption is null ? 0 : 1)
            .ThenBy(p => p.FixedConsumption?.RatePerSecond ?? 0)
            .ThenBy(p => p.FacilityId, StringComparer.Ordinal)
            .First();
    }

    /// <summary>
    /// レシピを VersionAdded 降順（パース不能は最古）・Id 昇順に並べる。
    /// </summary>
    private static List<Recipe> OrderCandidates(
        IEnumerable<Recipe> candidates,
        ICollection<CalculationWarning> warnings)
    {
        return candidates
            .OrderByDescending(r => ParseVersionOrOldest(r.Id, r.VersionAdded, warnings))
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static Version ParseVersionOrOldest(
        string recipeId,
        string? versionText,
        ICollection<CalculationWarning> warnings)
    {
        if (Version.TryParse(versionText, out Version? parsed))
        {
            return parsed;
        }

        warnings.Add(new CalculationWarning(
            WarningCode.InvalidVersionString,
            $"レシピ {recipeId} の VersionAdded {versionText} は semver としてパースできないため、最古として扱います。"));
        return new Version(0, 0, 0, 0);
    }

    /// <summary>
    /// 上書き指定のペア行を適格レシピの中から一意キーで照合する。
    /// レシピのイベント適格・ペアの環境適格をどちらも満たす場合のみ有効。
    /// </summary>
    private static Selection? FindOverride(
        IReadOnlyList<Recipe> orderedCandidates,
        MasterDataSnapshot master,
        ContextFilter context,
        PairOverride pairOverride)
    {
        foreach (Recipe recipe in orderedCandidates)
        {
            if (recipe.Id != pairOverride.RecipeId)
            {
                continue;
            }

            foreach (RecipeFacility pair in recipe.Facilities)
            {
                if (!Matches(pair, pairOverride))
                {
                    continue;
                }

                if (!IsPairEligible(pair, master, context))
                {
                    return null;
                }

                return new Selection(recipe, pair);
            }
        }

        return null;
    }

    /// <summary>ペア行と上書き指定の全要素一致判定（仕様決定 P の一意キー）。</summary>
    private static bool Matches(RecipeFacility pair, PairOverride pairOverride)
    {
        return pair.FacilityId == pairOverride.FacilityId
            && Math.Abs(pair.CycleTime - pairOverride.CycleTime) <= Epsilon
            && pair.EnvironmentId == pairOverride.EnvironmentId
            && FixedConsumptionEquals(pair.FixedConsumption, pairOverride.FixedConsumption);
    }

    private static bool FixedConsumptionEquals(FixedConsumption? a, FixedConsumption? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return a.ItemId == b.ItemId && Math.Abs(a.RatePerSecond - b.RatePerSecond) <= Epsilon;
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

        if (!master.EnvironmentsById.TryGetValue(pair.EnvironmentId, out Environment? environment))
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
