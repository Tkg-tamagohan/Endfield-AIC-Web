using EndfieldAicWeb.Application;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;
using EndfieldAicWeb.Infrastructure.Transfer;

namespace EndfieldAicWeb.Admin.Services;

/// <summary>エクスポート結果。違反時は Json なしで Errors が入る。</summary>
public sealed record ExportOutcome(
    string? Json,
    IReadOnlyList<MasterValidationError> Errors)
{
    public bool Success => Json is not null;
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

    /// <summary>赤枠表示のまま確定されていない不正な入力値を持つエディタがあるか。</summary>
    public bool HasInvalidInput => _invalidEditors.Count > 0;

    private readonly HashSet<object> _invalidEditors = [];

    /// <summary>エディタの不正入力状態を登録・解除する。キーはエディタコンポーネント自身。</summary>
    public void SetEditorInvalid(object editor, bool invalid)
    {
        bool changed = invalid ? _invalidEditors.Add(editor) : _invalidEditors.Remove(editor);
        if (changed)
        {
            Changed?.Invoke();
        }
    }

    public bool IsLoaded => Document is not null;

    /// <summary>計算プレビュー用スナップショット（未読み込み時は null）。</summary>
    public MasterDataSnapshot? Snapshot =>
        Document is null ? null : MasterSnapshotFactory.Create(Document);

    /// <summary>URL（相対パスまたは絶対 URL）から正本 JSON を取得して読み込む。</summary>
    public async Task<bool> LoadFromUrlAsync(string url)
    {
        string json;
        try
        {
            json = await _http.GetStringAsync(url);
        }
        catch (Exception ex)
        {
            LoadFailure = $"取得に失敗しました: {ex.Message}";
            LoadErrors = [];
            return false;
        }

        return LoadJson(json, url);
    }

    /// <summary>JSON 文字列を読み込む。違反があれば一覧を保持して編集状態へ入らない。</summary>
    public bool LoadJson(string json, string sourceLabel)
    {
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
            IsDirty = false;
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

    public event Action? Changed;
}
