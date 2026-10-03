using EndfieldAicWeb.Domain.Models;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Domain.Calculation;

/// <summary>
/// 需要アイテムに対して使用する（レシピ, ペア）の組を 1 件選択する。
/// レシピはコンテキスト適格候補から VersionAdded 最新・実効出力レート降順（同率は Id 昇順）を先に選び、
/// そのレシピの適格ペアから CycleTime 最小を既定とする（仕様決定 F・BA）。
/// 実効出力レートは対象アイテムの 1 サイクル出力量 ÷ 適格ペアの最小 CycleTime × 60 個/分。
/// 同 CycleTime は FixedConsumption なし/小 → EnvironmentId=null の順（仕様決定 U・BT）。
/// レシピの適格ペアが 0 件なら次点のレシピへ進む（docs/phases/implementation-plan-phase2.md §3）。
/// </summary>
public static class PairSelector
{
    /// <summary>選択結果。稼働するレシピとその中のペア行。</summary>
    public sealed record Selection(Recipe Recipe, RecipeFacility Pair);

    /// <summary>選択の戻り値。選択結果（候補 0 件は null）と、選択中に発生した警告をまとめて返す。</summary>
    public sealed record Result(Selection? Selection, IReadOnlyList<CalculationWarning> Warnings);

    /// <summary>
    /// itemId を出力するレシピのうちコンテキスト上 eligible なものから 1 組を選ぶ。
    /// 候補が 0 件なら Selection は null（呼び出し側で終端処理する）。
    /// </summary>
    public static Result Select(
        string itemId,
        MasterDataSnapshot master,
        ContextFilter context,
        IReadOnlyList<PairOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(overrides);

        var warnings = new List<CalculationWarning>();

        IEnumerable<Recipe> candidates = master.RecipesByOutputItemId.TryGetValue(itemId, out IReadOnlyList<Recipe>? list)
            ? list.Where(r => IsEventEligible(r.GameEventId, context))
            : [];

        List<Recipe> ordered = OrderCandidates(candidates, itemId, master, context, warnings);

        PairOverride? pairOverride = overrides.FirstOrDefault(o => o.ItemId == itemId);
        if (pairOverride is not null)
        {
            Selection? overridden = FindOverride(ordered, master, context, pairOverride);
            if (overridden is not null)
            {
                return new Result(overridden, warnings);
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
                return new Result(new Selection(recipe, ChooseDefaultPair(eligiblePairs)), warnings);
            }
        }

        return new Result(null, warnings);
    }

    /// <summary>候補列挙の1要素。IsDefault は選択規則の既定ペアを示す。</summary>
    public sealed record CandidatePair(Recipe Recipe, RecipeFacility Pair, bool IsDefault);

    /// <summary>
    /// itemId を出力する適格レシピ × 適格ペアの全候補を列挙する。
    /// レシピは Select と同じ順序（VersionAdded 降順・実効出力レート降順・Id 昇順）、ペアは既定選択規則の順序（U・BT）。
    /// 既定ペアには IsDefault を立てる。UI のペア代替選択の候補表示に使う。
    /// バージョン文字列の警告は破棄する（計算実行時に Warnings として報告済みのため）。
    /// </summary>
    public static IReadOnlyList<CandidatePair> ListCandidates(
        string itemId,
        MasterDataSnapshot master,
        ContextFilter context)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(context);

        IEnumerable<Recipe> candidates = master.RecipesByOutputItemId.TryGetValue(itemId, out IReadOnlyList<Recipe>? list)
            ? list.Where(r => IsEventEligible(r.GameEventId, context))
            : [];

        // バージョン文字列の警告はここでは捨てる（計算実行時に Warnings として報告済みのため）。
        List<Recipe> ordered = OrderCandidates(candidates, itemId, master, context, new List<CalculationWarning>());

        List<(Recipe Recipe, List<RecipeFacility> Pairs)> eligible = ordered
            .Select(r => (Recipe: r, Pairs: OrderPairs(EligiblePairs(r, master, context))))
            .Where(t => t.Pairs.Count > 0)
            .ToList();

        RecipeFacility? defaultPair = eligible.Count > 0 ? eligible[0].Pairs[0] : null;

        return eligible
            .SelectMany(t => t.Pairs.Select(p => new CandidatePair(t.Recipe, p, ReferenceEquals(p, defaultPair))))
            .ToList();
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
    /// 既定ペア。CycleTime 最小、同率は FixedConsumption なし/小 → EnvironmentId=null → FacilityId 昇順。
    /// </summary>
    internal static RecipeFacility ChooseDefaultPair(IEnumerable<RecipeFacility> eligiblePairs)
    {
        return OrderPairs(eligiblePairs).First();
    }

    /// <summary>既定選択規則（U・BT）の順序: CycleTime 昇順 → 固定消費なし/小 → 環境なし優先 → FacilityId 昇順。</summary>
    private static List<RecipeFacility> OrderPairs(IEnumerable<RecipeFacility> eligiblePairs)
    {
        return eligiblePairs
            .OrderBy(p => p.CycleTime)
            .ThenBy(p => p.FixedConsumption is null ? 0 : 1)
            .ThenBy(p => p.FixedConsumption?.RatePerMinute ?? 0)
            .ThenBy(p => p.EnvironmentId is null ? 0 : 1)
            .ThenBy(p => p.FacilityId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// レシピを VersionAdded 降順（パース不能は最古）・実効出力レート降順・Id 昇順に並べる（仕様決定 BA）。
    /// </summary>
    private static List<Recipe> OrderCandidates(
        IEnumerable<Recipe> candidates,
        string itemId,
        MasterDataSnapshot master,
        ContextFilter context,
        ICollection<CalculationWarning> warnings)
    {
        return candidates
            .Select(r => (
                Recipe: r,
                Version: ParseVersionOrOldest(r.Id, r.VersionAdded, warnings),
                Rate: EffectiveRate(r, itemId, master, context)))
            .OrderByDescending(t => t.Version is not null)
            .ThenByDescending(t => t.Version)
            .ThenByDescending(t => t.Rate)
            .ThenBy(t => t.Recipe.Id, StringComparer.Ordinal)
            .Select(t => t.Recipe)
            .ToList();
    }

    /// <summary>
    /// 需要対象アイテムの実効出力レート（個/分）。
    /// 1 サイクル出力量合計 ÷ 適格ペアの最小 CycleTime × 60。適格ペア 0 件・正の CycleTime なしは 0。
    /// </summary>
    private static double EffectiveRate(
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

        double outputQty = recipe.Outputs
            .Where(o => o.ItemId == itemId)
            .Sum(o => o.Quantity);
        return outputQty / minCycle * 60;
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
            && pair.CycleTime == pairOverride.CycleTime
            && pair.EnvironmentId == pairOverride.EnvironmentId
            && FixedConsumptionEquals(pair.FixedConsumption, pairOverride.FixedConsumption);
    }

    private static bool FixedConsumptionEquals(FixedConsumption? a, FixedConsumption? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return a.ItemId == b.ItemId && a.RatePerMinute == b.RatePerMinute;
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
