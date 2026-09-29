using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;
using EndfieldAicWeb.Infrastructure.Icons;

namespace EndfieldAicWeb.App.Services;

/// <summary>
/// 正本に記載されたアイコンを取得・解決するカタログ。
/// マニフェスト記載キーは Sha256/Bytes 一致でのみ採用し、記載外の参照キーは
/// ファイル名規約 <c>data/icons/&lt;Key&gt;.png</c> で拾う（旧 AP の挙動、仕様決定 R）。
/// 取得できないキーは解決結果 null＝プレースホルダ表示に落ちる。
/// </summary>
public sealed class IconCatalog
{
    private readonly HttpClient _http;
    private readonly InMemoryIconFileProvider _files = new();
    private readonly Dictionary<string, string?> _dataUrls = new(StringComparer.Ordinal);
    private IconResolver? _resolver;
    private Dictionary<string, Item> _itemsById = new(StringComparer.Ordinal);
    private Dictionary<string, Recipe> _recipesById = new(StringComparer.Ordinal);

    public IconCatalog(HttpClient http) => _http = http;

    /// <summary>
    /// 文書の Icons マニフェストとエンティティ参照キーに沿って画像を取得する。
    /// 個別の取得失敗・ハズレは握りつぶし、そのアイコンだけ未解決にする。
    /// </summary>
    public async Task LoadAsync(MasterDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _itemsById = document.Items.Where(i => i.Id is not null).ToDictionary(i => i.Id, StringComparer.Ordinal);
        _recipesById = document.Recipes.Where(r => r.Id is not null).ToDictionary(r => r.Id, StringComparer.Ordinal);
        _resolver = new IconResolver(document.Icons, _files);

        var manifestKeys = document.Icons.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var tasks = document.Icons.Select(FetchManifestEntry).ToList();
        // マニフェストに乗らない参照キーはファイル名規約で拾う（ローカル差し込みの合意）。
        foreach (string key in IconExportPlanner.ReferencedKeys(document))
        {
            if (!manifestKeys.Contains(key))
            {
                tasks.Add(FetchByName(key));
            }
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>IconKey を解決して img 用の data: URI を返す。未解決・未取得は null。</summary>
    public string? Url(string? iconKey)
    {
        if (_resolver?.Resolve(iconKey) is not { } path)
        {
            return null;
        }

        if (_dataUrls.TryGetValue(path, out string? cached))
        {
            return cached;
        }

        string? url = _files.ReadAllBytes(path) is { } bytes
            ? $"data:image/png;base64,{Convert.ToBase64String(bytes)}"
            : null;
        _dataUrls[path] = url;
        return url;
    }

    /// <summary>レシピの実効 IconKey。未設定時は主出力（SortOrder 最小）アイテムのキー。</summary>
    public string? RecipeIconKey(string recipeId)
    {
        if (!_recipesById.TryGetValue(recipeId, out Recipe? recipe))
        {
            return null;
        }

        if (recipe.IconKey is not null)
        {
            return recipe.IconKey;
        }

        RecipeOutput? main = recipe.Outputs.OrderBy(o => o.SortOrder).FirstOrDefault();
        return main is not null && _itemsById.TryGetValue(main.ItemId, out Item? item)
            ? item.IconKey
            : null;
    }

    private async Task FetchManifestEntry(IconEntry entry)
    {
        try
        {
            byte[] content = await _http.GetByteArrayAsync($"data/{entry.File}");
            if (IconFiles.Matches(content, entry))
            {
                _files.Set(entry.File, content);
            }
        }
        catch (Exception)
        {
            // そのアイコンだけ未解決（プレースホルダ）にする。
        }
    }

    private async Task FetchByName(string key)
    {
        try
        {
            byte[] content = await _http.GetByteArrayAsync($"data/icons/{key}.png");
            _files.Set($"icons/{key}.png", content);
        }
        catch (Exception)
        {
            // 同上。
        }
    }
}
