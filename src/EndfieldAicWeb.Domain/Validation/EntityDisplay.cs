using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Domain.Validation;

/// <summary>
/// ページに表示する警告・エラー系の文面でエンティティ参照を `名前（Id）` へ整形する
/// 共有ヘルパー（仕様決定 CF・CG）。参照先が存在しない、または Name が空のときは
/// Id のみを返す（<c>SnapshotLookup</c> の `Name : id` と同型のフォールバック）。
/// 重複名の識別は Id 部に委ねる（CG）。
/// </summary>
public sealed class EntityDisplay
{
    /// <summary>索引を持たない共有の空解決器。すべての参照を Id のまま返す。</summary>
    public static EntityDisplay Empty { get; } = new();

    private readonly IReadOnlyDictionary<string, Item> _items;
    private readonly IReadOnlyDictionary<string, Facility> _facilities;
    private readonly IReadOnlyDictionary<string, Environment> _environments;
    private readonly IReadOnlyDictionary<string, Recipe> _recipes;
    private readonly IReadOnlyDictionary<string, GameMap> _maps;
    private readonly IReadOnlyDictionary<string, GameEvent> _gameEvents;

    /// <summary>スナップショットの索引を共有して構築する（計算経路）。</summary>
    public EntityDisplay(MasterDataSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _items = snapshot.ItemsById;
        _facilities = snapshot.FacilitiesById;
        _environments = snapshot.EnvironmentsById;
        _recipes = snapshot.RecipesById;
        _maps = snapshot.MapsById;
        _gameEvents = snapshot.GameEventsById;
    }

    /// <summary>マスタ文書のエンティティ一覧から構築する（検証・管理ツール経路）。</summary>
    public EntityDisplay(MasterDocument document)
        : this(document.Items, document.Facilities, document.Environments, document.Recipes, document.Maps, document.GameEvents)
    {
        ArgumentNullException.ThrowIfNull(document);
    }

    /// <summary>
    /// エンティティ一覧から構築する。Id の重複（検証対象の文書で起こりうる）は先頭優先で許容する。
    /// </summary>
    public EntityDisplay(
        IEnumerable<Item>? items = null,
        IEnumerable<Facility>? facilities = null,
        IEnumerable<Environment>? environments = null,
        IEnumerable<Recipe>? recipes = null,
        IEnumerable<GameMap>? maps = null,
        IEnumerable<GameEvent>? gameEvents = null)
    {
        _items = Index(items);
        _facilities = Index(facilities);
        _environments = Index(environments);
        _recipes = Index(recipes);
        _maps = Index(maps);
        _gameEvents = Index(gameEvents);
    }

    /// <summary>名前が取れるとき `名前（Id）`、名前が null・空・空白のみのとき Id のみを返す。</summary>
    public static string Format(string? name, string id) =>
        string.IsNullOrWhiteSpace(name) ? id : $"{name}（{id}）";

    public string Item(string id) => Format(NameOf(_items, id), id);

    public string Facility(string id) => Format(NameOf(_facilities, id), id);

    public string Environment(string id) => Format(NameOf(_environments, id), id);

    public string Recipe(string id) => Format(NameOf(_recipes, id), id);

    public string GameMap(string id) => Format(NameOf(_maps, id), id);

    public string GameEvent(string id) => Format(NameOf(_gameEvents, id), id);

    /// <summary>
    /// 検証エラー行の EntityKind（単数名・コレクション名の両方）と EntityId から表示名を解決する。
    /// 未解決の Id や種別外の EntityKind は入力値のまま返す。
    /// </summary>
    public string For(string entityKind, string entityId) => entityKind switch
    {
        "Item" or "Items" => Item(entityId),
        "Facility" or "Facilities" => Facility(entityId),
        "Environment" or "Environments" => Environment(entityId),
        "GameEvent" or "GameEvents" => GameEvent(entityId),
        "Recipe" or "Recipes" => Recipe(entityId),
        "GameMap" or "Maps" => GameMap(entityId),
        _ => entityId,
    };

    private static string? NameOf<T>(IReadOnlyDictionary<string, T> map, string id)
        where T : MasterEntity =>
        map.TryGetValue(id, out T? entity) ? entity.Name : null;

    private static IReadOnlyDictionary<string, T> Index<T>(IEnumerable<T>? entities)
        where T : MasterEntity
    {
        var index = new Dictionary<string, T>(StringComparer.Ordinal);
        if (entities is not null)
        {
            foreach (T entity in entities)
            {
                if (!string.IsNullOrEmpty(entity.Id))
                {
                    index.TryAdd(entity.Id, entity);
                }
            }
        }

        return index;
    }
}
