using EndfieldAicWeb.Domain.Calculation;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>ProductionPlan の検索・判定ヘルパー。</summary>
internal static class PlanAssert
{
    /// <summary>数値比較の精度（docs/phases/test-specification-phase2.md §1）。</summary>
    public const int Precision = 6;

    public static ItemRequirement Req(ProductionPlan plan, string itemId) =>
        plan.ItemRequirements.Single(r => r.ItemId == itemId);

    public static bool HasReq(ProductionPlan plan, string itemId) =>
        plan.ItemRequirements.Any(r => r.ItemId == itemId);

    public static FacilityRequirement Fac(ProductionPlan plan, string facilityId) =>
        plan.FacilityRequirements.Single(f => f.FacilityId == facilityId);

    public static bool HasFac(ProductionPlan plan, string facilityId) =>
        plan.FacilityRequirements.Any(f => f.FacilityId == facilityId);

    public static bool HasWarning(ProductionPlan plan, WarningCode code) =>
        plan.Warnings.Any(w => w.Code == code);

    public static double Supplied(ProductionPlan plan, string itemId, SupplyKind kind) =>
        Req(plan, itemId).Supplies.Where(s => s.Kind == kind).Sum(s => s.AmountPerMinute);

    public static RecipeRun? RunOf(ProductionPlan plan, string recipeId) =>
        plan.RecipeRuns.SingleOrDefault(r => r.RecipeId == recipeId);
}
