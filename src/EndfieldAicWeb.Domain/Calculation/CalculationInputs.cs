using System.Collections.ObjectModel;
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
public sealed record ContextFilter
{
    /// <summary>有効な GameEvent の Id 集合。</summary>
    public IReadOnlyCollection<string> ActiveGameEventIds { get; init; } = [];
}

/// <summary>
/// 計算に使うマスタデータのメモリ内スナップショット。
/// 保存形式や UI に依存しない純粋ロジックの入力となる。
/// コレクションは構築時に複製され、索引も構築時点の内容で確定するため、
/// 構築後に元のリストを変更してもスナップショットの内容と索引は影響を受けない。
/// エンティティ自体は共有参照のため変更してはならない。
/// 変更がある場合は新しいスナップショットを作ること。
/// </summary>
public sealed class MasterDataSnapshot
{
    private readonly IReadOnlyList<Item> _items = [];
    private readonly IReadOnlyList<Facility> _facilities = [];
    private readonly IReadOnlyList<Environment> _environments = [];
    private readonly IReadOnlyList<GameEvent> _gameEvents = [];
    private readonly IReadOnlyList<Recipe> _recipes = [];
    private readonly IReadOnlyList<GameMap> _maps = [];

    private readonly IReadOnlyDictionary<string, Item> _itemsById = new Dictionary<string, Item>(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, Facility> _facilitiesById = new Dictionary<string, Facility>(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, Environment> _environmentsById = new Dictionary<string, Environment>(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, Recipe> _recipesById = new Dictionary<string, Recipe>(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, GameMap> _mapsById = new Dictionary<string, GameMap>(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, IReadOnlyList<Recipe>> _recipesByOutputItemId = new Dictionary<string, IReadOnlyList<Recipe>>(StringComparer.Ordinal);

    public required IReadOnlyList<Item> Items
    {
        get => _items;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _items = Array.AsReadOnly(value.ToArray());
            _itemsById = new ReadOnlyDictionary<string, Item>(
                _items.ToDictionary(i => i.Id, StringComparer.Ordinal));
        }
    }

    public required IReadOnlyList<Facility> Facilities
    {
        get => _facilities;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _facilities = Array.AsReadOnly(value.ToArray());
            _facilitiesById = new ReadOnlyDictionary<string, Facility>(
                _facilities.ToDictionary(f => f.Id, StringComparer.Ordinal));
        }
    }

    public required IReadOnlyList<Environment> Environments
    {
        get => _environments;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _environments = Array.AsReadOnly(value.ToArray());
            _environmentsById = new ReadOnlyDictionary<string, Environment>(
                _environments.ToDictionary(e => e.Id, StringComparer.Ordinal));
        }
    }

    public required IReadOnlyList<GameEvent> GameEvents
    {
        get => _gameEvents;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _gameEvents = Array.AsReadOnly(value.ToArray());
        }
    }

    public required IReadOnlyList<Recipe> Recipes
    {
        get => _recipes;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _recipes = Array.AsReadOnly(value.ToArray());
            _recipesById = new ReadOnlyDictionary<string, Recipe>(
                _recipes.ToDictionary(r => r.Id, StringComparer.Ordinal));
            _recipesByOutputItemId = BuildRecipesByOutputItemId(_recipes);
        }
    }

    public required IReadOnlyList<GameMap> Maps
    {
        get => _maps;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _maps = Array.AsReadOnly(value.ToArray());
            _mapsById = new ReadOnlyDictionary<string, GameMap>(
                _maps.ToDictionary(m => m.Id, StringComparer.Ordinal));
        }
    }

    /// <summary>ItemId → Item。</summary>
    public IReadOnlyDictionary<string, Item> ItemsById => _itemsById;

    /// <summary>FacilityId → Facility。</summary>
    public IReadOnlyDictionary<string, Facility> FacilitiesById => _facilitiesById;

    /// <summary>EnvironmentId → Environment。</summary>
    public IReadOnlyDictionary<string, Environment> EnvironmentsById => _environmentsById;

    /// <summary>RecipeId → Recipe。</summary>
    public IReadOnlyDictionary<string, Recipe> RecipesById => _recipesById;

    /// <summary>MapId → GameMap。</summary>
    public IReadOnlyDictionary<string, GameMap> MapsById => _mapsById;

    /// <summary>出力アイテム Id → そのアイテムを出力するレシピ一覧。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Recipe>> RecipesByOutputItemId => _recipesByOutputItemId;

    private static IReadOnlyDictionary<string, IReadOnlyList<Recipe>> BuildRecipesByOutputItemId(IReadOnlyList<Recipe> recipes)
    {
        var byOutput = new Dictionary<string, List<Recipe>>(StringComparer.Ordinal);
        foreach (Recipe recipe in recipes)
        {
            foreach (RecipeOutput output in recipe.Outputs)
            {
                if (!byOutput.TryGetValue(output.ItemId, out List<Recipe>? list))
                {
                    list = [];
                    byOutput[output.ItemId] = list;
                }

                list.Add(recipe);
            }
        }

        return new ReadOnlyDictionary<string, IReadOnlyList<Recipe>>(
            byOutput.ToDictionary(
                p => p.Key,
                p => (IReadOnlyList<Recipe>)p.Value.AsReadOnly(),
                StringComparer.Ordinal));
    }
}
