using EndfieldAicWeb.Application;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;
using EndfieldAicWeb.Infrastructure.Icons;
using EndfieldAicWeb.Infrastructure.Transfer;

namespace EndfieldAicWeb.Admin.Services;

/// <summary>JSON エクスポート結果。違反時は Json なしで Errors が入る。</summary>
public sealed record ExportOutcome(
    string? Json,
    IReadOnlyList<MasterValidationError> Errors)
{
    public bool Success => Json is not null;
}

/// <summary>zip エクスポート結果。違反時は ZipBytes なしで Errors が入る。</summary>
public sealed record ExportZipOutcome(
    byte[]? ZipBytes,
    IReadOnlyList<MasterValidationError> Errors)
{
    public bool Success => ZipBytes is not null;
}

/// <summary>
/// 管理ツールの編集セッション。読み込んだ <see cref="MasterDocument"/> を保持し、
/// 検証・エクスポート・計算プレビュー用スナップショットの入口をまとめる。
/// </summary>
public sealed class AdminDocumentService
{
    private readonly HttpClient _http;

    public AdminDocumentService(HttpClient http) => _http = http;

    /// <summary>編集中のマスタ文書。未読み込み時は null。</summary>
    public MasterDocument? Document { get; private set; }

    /// <summary>読み込み元の表示名（同梱 / URL / ファイル名）。</summary>
    public string? SourceLabel { get; private set; }

    /// <summary>エクスポート未実行の変更が残っているか。読み込み直しで消える。</summary>
    public bool IsDirty { get; private set; }

    /// <summary>読み込み時の違反一覧（構文・構造・意味）。</summary>
    public IReadOnlyList<MasterValidationError> LoadErrors { get; private set; } = [];

    /// <summary>「検証を実行」の結果一覧。以後の編集でクリアされる。</summary>
    public IReadOnlyList<MasterValidationError> ValidationErrors { get; private set; } = [];

    /// <summary>読み込み以降に検証（またはエクスポート内の検証）を実行したか。</summary>
    public bool ValidationRan { get; private set; }

    /// <summary>検証済みだが、以後の編集で結果が古くなっているか。</summary>
    public bool ValidationStale { get; private set; }

    /// <summary>HTTP 取得・ファイル読取の致命的失敗メッセージ。</summary>
    public string? LoadFailure { get; private set; }

    /// <summary>マニフェスト記載ファイルのうち、ハッシュ一致で取得できた件数。</summary>
    public int IconFilesLoaded { get; private set; }

    /// <summary>マニフェスト記載ファイルの総数（IconFilesLoaded の分母）。</summary>
    public int IconFilesExpected { get; private set; }

    /// <summary>取得・取り込み済みアイコンの実体（マニフェスト相対パス → PNG バイト列）。</summary>
    private readonly InMemoryIconFileProvider _iconStore = new();

    /// <summary>アイコンプレビュー用の data: URI キャッシュ。変更・読み込み直しでクリアする。</summary>
    private readonly Dictionary<string, string> _iconDataUrls = new(StringComparer.Ordinal);

    /// <summary>赤枠表示のまま確定されていない不正な入力値を持つエディタがあるか。</summary>
    public bool HasInvalidInput => _invalidEditors.Count > 0;

    /// <summary>未確定の不正入力。キーは（対象オブジェクト, フィールド名）、値は拒否された入力文字列。
    /// エディタの破棄（ページ遷移・エンティティ切替）では消さず、修正・対象削除・読み込み直しでのみ消える。</summary>
    private readonly Dictionary<(object Owner, string Field), string> _invalidEditors = [];

    /// <summary>不正入力を登録する。text に null を渡すと解除する。
    /// 登録された時点で前回の検証結果は当てにならないため stale にする。</summary>
    public void SetEditorInvalid(object owner, string field, string? text)
    {
        if (text is null)
        {
            _invalidEditors.Remove((owner, field));
        }
        else
        {
            _invalidEditors[(owner, field)] = text;
            if (ValidationRan)
            {
                ValidationStale = true;
            }

            ValidationErrors = [];
        }
    }

    /// <summary>対象に未確定の不正入力が残っているか。残っていれば拒否された文字列を返す。</summary>
    public bool TryGetInvalidText(object owner, string field, out string? text) =>
        _invalidEditors.TryGetValue((owner, field), out text);

    /// <summary>対象オブジェクト（削除されたエンティティ・行など）に紐づく不正入力をすべて破棄する。</summary>
    public void ClearInvalidOwner(object owner)
    {
        foreach ((object Owner, string Field) key in _invalidEditors.Keys.Where(k => ReferenceEquals(k.Owner, owner)).ToList())
        {
            _invalidEditors.Remove(key);
        }
    }

    /// <summary>複数の対象オブジェクトに紐づく不正入力をまとめて破棄する。</summary>
    public void ClearInvalidOwners(IEnumerable<object> owners)
    {
        foreach (object owner in owners)
        {
            ClearInvalidOwner(owner);
        }
    }

    public bool IsLoaded => Document is not null;

    /// <summary>計算プレビュー用スナップショット（未読み込み時は null）。</summary>
    public MasterDataSnapshot? Snapshot =>
        Document is null ? null : MasterSnapshotFactory.Create(Document);

    /// <summary>読み込みの世代番号。並走した取得では最後に始まった要求だけが文書を置き換える。</summary>
    private int _loadGeneration;

    /// <summary>文書の編集回数。エクスポート後に編集が入ったかの判定に使う。</summary>
    private int _editCounter;

    /// <summary>最後に書き出しを成功させた時点の編集回数。</summary>
    private int _counterAtExport = -1;

    /// <summary>URL（相対パスまたは絶対 URL）から正本 JSON を取得して読み込む。
    /// 待機中に別の読み込みが始まった場合は結果を捨てる（新しいほうが優先）。</summary>
    public async Task<bool> LoadFromUrlAsync(string url)
    {
        int generation = ++_loadGeneration;
        string json;
        try
        {
            json = await _http.GetStringAsync(url);
        }
        catch (Exception ex)
        {
            if (generation != _loadGeneration)
            {
                return false;
            }

            LoadFailure = $"取得に失敗しました: {ex.Message}";
            LoadErrors = [];
            return false;
        }

        if (generation != _loadGeneration)
        {
            return false;
        }

        if (!LoadJson(json, url))
        {
            return false;
        }

        await FetchIconsAsync(url);
        return true;
    }

    /// <summary>エクスポート物の zip（data/master.json＋data/icons/）を読み込む。
    /// JSON とアイコンの両方を置き換える。</summary>
    public bool LoadZip(byte[] zipBytes, string sourceLabel)
    {
        if (!IconArchive.TryReadZip(zipBytes, out string? json, out IReadOnlyDictionary<string, byte[]> icons)
            || json is null)
        {
            LoadFailure = "zip の読み取りに失敗しました（data/master.json が見つかりません）";
            LoadErrors = [];
            return false;
        }

        if (!LoadJson(json, sourceLabel))
        {
            return false;
        }

        _iconStore.Clear();
        _iconDataUrls.Clear();
        var manifestByFile = Document!.Icons
            .GroupBy(e => e.File, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach ((string path, byte[] bytes) in icons)
        {
            // マニフェスト記載ファイルは照合一致のみ取り込む（不一致は未取得＝エクスポートで拒否）。
            // 収録外ファイルはファイル名解決の差し込みとして保持する。
            if (manifestByFile.TryGetValue(path, out IconEntry? entry))
            {
                if (IconFiles.Matches(bytes, entry))
                {
                    _iconStore.Set(path, bytes);
                }
            }
            else
            {
                _iconStore.Set(path, bytes);
            }
        }

        IconFilesExpected = Document.Icons.Count;
        IconFilesLoaded = Document.Icons.Count(e => _iconStore.ReadAllBytes(e.File) is not null);
        return true;
    }

    /// <summary>マニフェスト記載のアイコンファイルを JSON と同じディレクトリから取得する。
    /// ハッシュ一致のみストアへ入れ、失敗・不一致はプレースホルダ扱い（読み込み自体は妨げない）。</summary>
    private async Task FetchIconsAsync(string jsonUrl)
    {
        _iconStore.Clear();
        _iconDataUrls.Clear();
        if (Document is null)
        {
            IconFilesExpected = 0;
            IconFilesLoaded = 0;
            return;
        }

        int generation = _loadGeneration;
        var baseUri = new Uri(_http.BaseAddress!, jsonUrl);
        var fetched = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (IconEntry entry in Document.Icons)
        {
            try
            {
                byte[] content = await _http.GetByteArrayAsync(new Uri(baseUri, entry.File));
                if (IconFiles.Matches(content, entry))
                {
                    fetched[entry.File] = content;
                }
            }
            catch (Exception)
            {
                // 取得失敗はそのアイコンだけプレースホルダへ落とす。
            }
        }

        // 待機中に別の読み込みが始まっていたら結果を捨てる。
        if (generation != _loadGeneration)
        {
            return;
        }

        foreach ((string path, byte[] bytes) in fetched)
        {
            _iconStore.Set(path, bytes);
        }

        IconFilesExpected = Document.Icons.Count;
        IconFilesLoaded = fetched.Count;
    }

    /// <summary>JSON 文字列を読み込む。違反があれば一覧を保持して編集状態へ入らない。</summary>
    public bool LoadJson(string json, string sourceLabel)
    {
        _loadGeneration++;
        LoadFailure = null;
        try
        {
            MasterJsonLoadResult result = MasterJsonLoader.Load(json);
            LoadErrors = result.Errors;
            if (!result.Success || result.Document is null)
            {
                return false;
            }

            Document = result.Document;
            SourceLabel = sourceLabel;
            IsDirty = false;
            ValidationErrors = [];
            ValidationRan = false;
            ValidationStale = false;
            _invalidEditors.Clear();
            // .json 単体の読み込みではアイコンストアは温存する（作業フォルダの合意）。
            // 温存分も新マニフェストの Sha256/Bytes で再照合し、一致分だけを取得済みに数える。
            IconFilesExpected = result.Document.Icons.Count;
            IconFilesLoaded = result.Document.Icons
                .Count(e => _iconStore.ReadAllBytes(e.File) is byte[] bytes && IconFiles.Matches(bytes, e));
            return true;
        }
        catch (Exception ex)
        {
            LoadFailure = $"読み込みに失敗しました: {ex.Message}";
            LoadErrors = [];
            return false;
        }
    }

    /// <summary>編集ページからの変更通知。直前の検証結果は破棄し、要再検証の状態にする。</summary>
    public void NotifyChanged()
    {
        _editCounter++;
        IsDirty = true;
        ValidationErrors = [];
        if (ValidationRan)
        {
            ValidationStale = true;
        }

        Changed?.Invoke();
    }

    /// <summary>文書全体を MasterValidator と同一規則で検証し、結果を保持して返す。</summary>
    public IReadOnlyList<MasterValidationError> Validate()
    {
        if (Document is null)
        {
            ValidationErrors = [];
            return ValidationErrors;
        }

        var errors = new List<MasterValidationError>();
        if (HasInvalidInput)
        {
            errors.Add(new MasterValidationError(
                "入力", "", "", "確定されていない不正な入力値（赤枠）があります。修正してから検証・書き出ししてください。"));
        }

        MasterValidator.ValidateAll(
            Document.Items,
            Document.Facilities,
            Document.Environments,
            Document.GameEvents,
            Document.Recipes,
            errors);
        ValidationErrors = errors;
        ValidationRan = true;
        ValidationStale = false;
        return ValidationErrors;
    }

    /// <summary>DataVersion を載せて全置換 JSON を書き出す。違反時は Errors を返して書き出さない。</summary>
    public ExportOutcome Export(string dataVersion)
    {
        if (Document is null)
        {
            return new ExportOutcome(null, []);
        }

        if (HasInvalidInput)
        {
            var blocked = new List<MasterValidationError>
            {
                new("入力", "", "", "確定されていない不正な入力値（赤枠）があります。修正してから書き出してください。"),
            };
            ValidationErrors = blocked;
            ValidationRan = true;
            ValidationStale = false;
            return new ExportOutcome(null, blocked);
        }

        // 書き出しに失敗したときは文書側の DataVersion を元に戻す（変更は確定させない）。
        string previousVersion = Document.DataVersion;
        Document.DataVersion = dataVersion;
        try
        {
            string json = MasterExporter.Export(Document);
            _counterAtExport = _editCounter;
            ValidationErrors = [];
            ValidationRan = true;
            ValidationStale = false;
            return new ExportOutcome(json, []);
        }
        catch (MasterValidationException ex)
        {
            Document.DataVersion = previousVersion;
            ValidationErrors = ex.Errors;
            ValidationRan = true;
            ValidationStale = false;
            return new ExportOutcome(null, ex.Errors);
        }
    }

    /// <summary>IconKey の解決結果（マニフェスト相対パス）。未解決は null。</summary>
    public string? ResolveIconPath(string? iconKey)
    {
        if (Document is null)
        {
            return null;
        }

        return new IconResolver(Document.Icons, _iconStore).Resolve(iconKey);
    }

    /// <summary>IconKey のプレビュー用 data: URI。未解決は null（プレースホルダ表示）。</summary>
    public string? IconDataUrl(string? iconKey)
    {
        string? path = ResolveIconPath(iconKey);
        if (path is null)
        {
            return null;
        }

        if (_iconDataUrls.TryGetValue(path, out string? cached))
        {
            return cached;
        }

        byte[]? bytes = _iconStore.ReadAllBytes(path);
        if (bytes is null)
        {
            return null;
        }

        string url = $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
        _iconDataUrls[path] = url;
        return url;
    }

    /// <summary>レシピの実効 IconKey。未設定時は主出力（SortOrder 最小）アイテムのキーへフォールバックする。</summary>
    public string? EffectiveIconKey(MasterEntity entity)
    {
        if (entity.IconKey is not null)
        {
            return entity.IconKey;
        }

        if (entity is Recipe recipe && Document is not null)
        {
            RecipeOutput? main = recipe.Outputs.OrderBy(o => o.SortOrder).FirstOrDefault();
            return main is null ? null : Document.Items.FirstOrDefault(i => i.Id == main.ItemId)?.IconKey;
        }

        return null;
    }

    /// <summary>128×128 PNG バイト列をエンティティのアイコンとして登録する。
    /// IconKey が未設定・文字種に合わない場合は <c>icon-&lt;Id&gt;</c> 形を補完する。</summary>
    public void RegisterIcon(MasterEntity entity, byte[] pngBytes)
    {
        if (Document is null)
        {
            return;
        }

        // 明示した有効キーはそのまま使い、自動補完時だけ他エンティティとの衝突を避ける。
        string key = IconKeyRules.IsValid(entity.IconKey)
            ? entity.IconKey!
            : UniqueSuggestedKey(entity);
        IconEntry entry = IconExportPlanner.CreateEntry(key, pngBytes);

        entity.IconKey = key;
        int index = Document.Icons.FindIndex(e => e.Key == key);
        if (index >= 0)
        {
            Document.Icons[index] = entry;
        }
        else
        {
            Document.Icons.Add(entry);
        }

        _iconStore.Set(entry.File, pngBytes);
        _iconDataUrls.Remove(entry.File);
        IconFilesExpected = Document.Icons.Count;
        IconFilesLoaded = Document.Icons.Count(e => _iconStore.ReadAllBytes(e.File) is not null);
        NotifyChanged();
    }

    /// <summary>エンティティのアイコン割り当てを解除する（マニフェストエントリは残り、孤立分はエクスポートで落ちる）。</summary>
    public void ClearIcon(MasterEntity entity)
    {
        entity.IconKey = null;
        NotifyChanged();
    }

    /// <summary><c>icon-&lt;Id&gt;</c> の提案キーを、他エンティティが使用中なら連番を付けて一意にする。
    /// Id はエンティティ種別をまたぐと一意でなく、置換・切詰めでも衝突し得るため。</summary>
    private string UniqueSuggestedKey(MasterEntity entity)
    {
        string baseKey = IconExportPlanner.SuggestKey(entity.Id);
        var used = new HashSet<string>(
            EnumerateEntities()
                .Where(e => !ReferenceEquals(e, entity))
                .Select(e => e.IconKey)
                .Where(k => k is not null)!,
            StringComparer.Ordinal);
        string key = baseKey;
        for (int n = 2; used.Contains(key); n++)
        {
            string suffix = $"-{n}";
            key = baseKey.Length + suffix.Length <= 64
                ? baseKey + suffix
                : baseKey[..(64 - suffix.Length)] + suffix;
        }

        return key;
    }

    private IEnumerable<MasterEntity> EnumerateEntities()
    {
        if (Document is null)
        {
            yield break;
        }

        foreach (Item e in Document.Items) yield return e;
        foreach (Facility e in Document.Facilities) yield return e;
        foreach (Domain.Models.Environment e in Document.Environments) yield return e;
        foreach (GameEvent e in Document.GameEvents) yield return e;
        foreach (Recipe e in Document.Recipes) yield return e;
    }

    /// <summary>DataVersion を載せて全置換 JSON＋アイコンを zip で書き出す。違反時は Errors を返して書き出さない。</summary>
    public ExportZipOutcome ExportZip(string dataVersion)
    {
        if (Document is null)
        {
            return new ExportZipOutcome(null, []);
        }

        if (HasInvalidInput)
        {
            var blocked = new List<MasterValidationError>
            {
                new("入力", "", "", "確定されていない不正な入力値（赤枠）があります。修正してから書き出してください。"),
            };
            ValidationErrors = blocked;
            ValidationRan = true;
            ValidationStale = false;
            return new ExportZipOutcome(null, blocked);
        }

        // アイコン整合（参照→マニフェスト→実体）はエクスポート経路でのみ必須（旧 AP）。
        // 文書側の違反と併記できるよう、文書検証とマニフェスト構築をまとめて行い、
        // いずれかの違反があれば書き出さない。
        var errors = new List<MasterValidationError>();
        MasterValidator.ValidateAll(
            Document.Items,
            Document.Facilities,
            Document.Environments,
            Document.GameEvents,
            Document.Recipes,
            errors);
        List<IconEntry> manifest = IconExportPlanner.BuildManifest(Document, _iconStore, errors);
        if (errors.Count > 0)
        {
            ValidationErrors = errors;
            ValidationRan = true;
            ValidationStale = false;
            return new ExportZipOutcome(null, errors);
        }

        // 書き出しに失敗したときは文書側の変更を元に戻す（変更は確定させない）。
        string previousVersion = Document.DataVersion;
        List<IconEntry> previousIcons = Document.Icons;
        Document.DataVersion = dataVersion;
        Document.Icons = manifest;
        try
        {
            string json = MasterExporter.Export(Document);
            byte[] zip = IconArchive.CreateZip(json, manifest, _iconStore);
            _counterAtExport = _editCounter;
            ValidationErrors = [];
            ValidationRan = true;
            ValidationStale = false;
            return new ExportZipOutcome(zip, []);
        }
        catch (MasterValidationException ex)
        {
            Document.DataVersion = previousVersion;
            Document.Icons = previousIcons;
            ValidationErrors = ex.Errors;
            ValidationRan = true;
            ValidationStale = false;
            return new ExportZipOutcome(null, ex.Errors);
        }
    }

    /// <summary>JSON ファイルのダウンロードが完了したことを記録し、ダーティフラグを落とす。
    /// 書き出し成功から完了までの間に編集が入っていた場合はダーティを維持する。</summary>
    public void MarkExported()
    {
        IsDirty = _editCounter != _counterAtExport;
    }

    public event Action? Changed;
}
