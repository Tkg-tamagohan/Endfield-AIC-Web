using EndfieldAicWeb.Domain.Models;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Domain.Calculation;

/// <summary>
/// 生産目標（アイテムと毎分の必要生産数）。複数指定できる。
/// </summary>
public sealed record ProductionTarget(string ItemId, double RatePerMinute);

/// <summary>
/// ユーザーによるペア選択の上書き指定（仕様決定 U）。
/// ペア行は全要素の組（一意キー、仕様決定 P）で照合する。
/// </summary>
public sealed record PairOverride(
    string ItemId,
    string RecipeId,
    string FacilityId,
    double CycleTime,
    string? EnvironmentId,
    FixedConsumption? FixedConsumption);

/// <summary>
/// 環境ごとの散布機台数の上書き指定（仕様決定 I）。
/// </summary>
public sealed record EnvironmentCountOverride(string EnvironmentId, int Count);

/// <summary>
/// 計算時のコンテキスト（有効イベントの集合）。
/// </summary>
public sealed class ContextFilter
{
    /// <summary>有効な GameEvent の Id 集合。</summary>
    public IReadOnlyCollection<string> ActiveGameEventIds { get; init; } = [];
}

/// <summary>
/// 計算に使うマスタデータのメモリ内スナップショット。
/// 保存形式や UI に依存しない純粋ロジックの入力となる。
/// </summary>
public sealed class MasterDataSnapshot
{
    public required IReadOnlyList<Item> Items { get; init; }
    public required IReadOnlyList<Facility> Facilities { get; init; }
    public required IReadOnlyList<Environment> Environments { get; init; }
    public required IReadOnlyList<GameEvent> GameEvents { get; init; }
    public required IReadOnlyList<Recipe> Recipes { get; init; }

    private Dictionary<string, Item>? _itemsById;
    private Dictionary<string, Facility>? _facilitiesById;
    private Dictionary<string, Environment>? _environmentsById;
    private Dictionary<string, Recipe>? _recipesById;
    private Dictionary<string, List<Recipe>>? _recipesByOutputItemId;

    /// <summary>ItemId → Item。</summary>
    public IReadOnlyDictionary<string, Item> ItemsById =>
        _itemsById ??= Items.ToDictionary(i => i.Id, StringComparer.Ordinal);

    /// <summary>FacilityId → Facility。</summary>
    public IReadOnlyDictionary<string, Facility> FacilitiesById =>
        _facilitiesById ??= Facilities.ToDictionary(f => f.Id, StringComparer.Ordinal);

    /// <summary>EnvironmentId → Environment。</summary>
    public IReadOnlyDictionary<string, Environment> EnvironmentsById =>
        _environmentsById ??= Environments.ToDictionary(e => e.Id, StringComparer.Ordinal);

    /// <summary>RecipeId → Recipe。</summary>
    public IReadOnlyDictionary<string, Recipe> RecipesById =>
        _recipesById ??= Recipes.ToDictionary(r => r.Id, StringComparer.Ordinal);

    /// <summary>出力アイテム Id → そのアイテムを出力するレシピ一覧。</summary>
    public IReadOnlyDictionary<string, List<Recipe>> RecipesByOutputItemId
    {
        get
        {
            if (_recipesByOutputItemId is null)
            {
                _recipesByOutputItemId = new Dictionary<string, List<Recipe>>(StringComparer.Ordinal);
                foreach (Recipe recipe in Recipes)
                {
                    foreach (RecipeOutput output in recipe.Outputs)
                    {
                        if (!_recipesByOutputItemId.TryGetValue(output.ItemId, out List<Recipe>? list))
                        {
                            list = [];
                            _recipesByOutputItemId[output.ItemId] = list;
                        }

                        list.Add(recipe);
                    }
                }
            }

            return _recipesByOutputItemId;
        }
    }
}
