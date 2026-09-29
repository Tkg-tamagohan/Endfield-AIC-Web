using EndfieldAicWeb.Application;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;
using EndfieldAicWeb.Infrastructure.Transfer;

namespace EndfieldAicWeb.App.Services;

/// <summary>
/// wwwroot/data/master.json を取得し、計算用スナップショットとして保持する。
/// </summary>
public sealed class MasterDataService
{
    private readonly HttpClient _http;

    public MasterDataService(HttpClient http) => _http = http;

    public MasterDataSnapshot? Snapshot { get; private set; }

    /// <summary>読み込んだマスタ文書（データ版の表示に使う）。</summary>
    public MasterDocument? Document { get; private set; }

    public IReadOnlyList<MasterValidationError> Errors { get; private set; } = [];

    /// <summary>HTTP 取得などの致命的失敗のメッセージ。</summary>
    public string? LoadError { get; private set; }

    public bool IsLoaded => Snapshot is not null;

    public async Task LoadAsync()
    {
        if (IsLoaded || LoadError is not null || Errors.Count > 0)
        {
            return;
        }

        try
        {
            string json = await _http.GetStringAsync("data/master.json");
            MasterJsonLoadResult result = MasterJsonLoader.Load(json);
            Errors = result.Errors;
            if (result.Success && result.Document is not null)
            {
                Document = result.Document;
                Snapshot = MasterSnapshotFactory.Create(result.Document);
            }
        }
        catch (Exception ex)
        {
            LoadError = $"マスタデータの取得に失敗しました: {ex.Message}";
        }
    }
}
