using EndfieldAicWeb.Admin.Services;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Infrastructure.Transfer;

namespace EndfieldAicWeb.Admin.Tests;

/// <summary>
/// 管理ツールの文書読み込み状態（LoadNotes・IsDirty）の検証テスト。
/// ADM-09〜10 は Devin Review 対応で追加した回帰テスト（仕様決定 CH）。
/// </summary>
public class AdminDocumentServiceTests
{
    /// <summary>アイテム i-1・設備 f-1 の最小文書をエクスポート JSON にして返す。</summary>
    private static string MinimalJson()
    {
        MasterDocument doc = new()
        {
            SchemaVersion = 1,
            DataVersion = "1.0.0",
            Items =
            [
                new Item { Id = "i-1", Name = "i1", VersionAdded = "1.0.0", Category = "素材", TransportKind = TransportKind.Belt },
            ],
            Facilities =
            [
                new Facility { Id = "f-1", Name = "f1", VersionAdded = "1.0.0", Width = 1, Height = 1 },
            ],
        };
        return MasterExporter.Export(doc);
    }

    [Fact(DisplayName = "ADM-09: 読み込み時正規化があった文書は IsDirty になる（CH）")]
    public void LoadWithNormalizations_MarksDirty()
    {
        var service = new AdminDocumentService(new HttpClient());

        // エクスポートは Domain 検証を通すため、前後空白入りの Id は JSON 文字列へ直接注入する。
        string json = MinimalJson().Replace("\"f-1\"", "\" f-1 \"");
        Assert.True(service.LoadJson(json, "test"), service.LoadFailure);
        Assert.NotEmpty(service.LoadNotes);
        Assert.True(service.IsDirty);
    }

    [Fact(DisplayName = "ADM-10: 読み込み時正規化がない文書は IsDirty にならない（CH）")]
    public void LoadWithoutNormalizations_StaysClean()
    {
        var service = new AdminDocumentService(new HttpClient());

        Assert.True(service.LoadJson(MinimalJson(), "test"), service.LoadFailure);
        Assert.Empty(service.LoadNotes);
        Assert.False(service.IsDirty);
    }
}
