using EndfieldAicWeb.Admin.Services;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;
using EndfieldAicWeb.Infrastructure.Transfer;

namespace EndfieldAicWeb.Admin.Tests;

/// <summary>
/// 管理ツールの文書読み込み状態（LoadNotes・IsDirty）の検証テスト。
/// ADM-09〜11 は仕様決定 CH 自体（読み込み時正規化と解除経路）の検証、ADM-12・13 は Devin Review 対応で追加した回帰テスト。
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

    [Fact(DisplayName = "ADM-11: 同一文書のエクスポート完了でダーティが落ちる（CH の解除経路）")]
    public void MarkExported_SameDocument_ClearsDirty()
    {
        var service = new AdminDocumentService(new HttpClient());
        string json = MinimalJson().Replace("\"f-1\"", "\" f-1 \"");
        Assert.True(service.LoadJson(json, "test"), service.LoadFailure);
        Assert.True(service.IsDirty);

        ExportOutcome outcome = service.Export("1.0.0");
        Assert.NotNull(outcome.Json);
        service.MarkExported();

        Assert.False(service.IsDirty);
    }

    [Fact(DisplayName = "ADM-12: エクスポート開始後に別文書を読み込むと MarkExported は新文書のダーティを上書きしない（Devin Review 回帰）")]
    public void MarkExported_AfterReload_KeepsNewDocumentDirty()
    {
        var service = new AdminDocumentService(new HttpClient());
        Assert.True(service.LoadJson(MinimalJson(), "a"), service.LoadFailure);
        ExportOutcome outcome = service.Export("1.0.0");
        Assert.NotNull(outcome.Json);

        // A のダウンロード完了待ちの間に、正規化を伴う別文書 B を読み込む。
        string json = MinimalJson().Replace("\"f-1\"", "\" f-1 \"");
        Assert.True(service.LoadJson(json, "b"), service.LoadFailure);
        Assert.True(service.IsDirty);

        // A の完了記録が B のダーティを消さない。
        service.MarkExported();
        Assert.True(service.IsDirty);
    }

    [Fact(DisplayName = "ADM-13: エクスポート開始後に失敗した読み込みを挟んでも完了記録はダーティを落とす（Devin Review 回帰）")]
    public void MarkExported_AfterFailedReload_StillClearsDirty()
    {
        var service = new AdminDocumentService(new HttpClient());
        string json = MinimalJson().Replace("\"f-1\"", "\" f-1 \"");
        Assert.True(service.LoadJson(json, "a"), service.LoadFailure);
        Assert.True(service.IsDirty);
        Assert.NotNull(service.Export("1.0.0").Json);

        // ダウンロード完了待ちの間に失敗する読み込みを挟む。文書は差し替わらないため完了記録は有効。
        Assert.False(service.LoadJson("{ invalid", "bad"));
        service.MarkExported();

        Assert.False(service.IsDirty);
    }

    [Fact(DisplayName = "ADM-14: 未編集の間は Validate と Snapshot が前回結果・同一インスタンスを再利用する（Phase 38）")]
    public void Memoization_ReusesResultsUntilEdited()
    {
        var service = new AdminDocumentService(new HttpClient());
        Assert.True(service.LoadJson(MinimalJson(), "test"), service.LoadFailure);

        IReadOnlyList<MasterValidationError> firstErrors = service.Validate();
        MasterDataSnapshot? firstSnapshot = service.Snapshot;

        Assert.Same(firstErrors, service.Validate());
        Assert.Same(firstErrors, service.ValidationErrors);
        Assert.NotNull(firstSnapshot);
        Assert.Same(firstSnapshot, service.Snapshot);
        Assert.True(service.ValidationRan);
        Assert.False(service.ValidationStale);
    }

    [Fact(DisplayName = "ADM-15: NotifyChanged を挟むと検証・スナップショットは再構築される（Phase 38）")]
    public void NotifyChanged_RebuildsSnapshotAndValidation()
    {
        var service = new AdminDocumentService(new HttpClient());
        Assert.True(service.LoadJson(MinimalJson(), "test"), service.LoadFailure);
        IReadOnlyList<MasterValidationError> firstErrors = service.Validate();
        MasterDataSnapshot? firstSnapshot = service.Snapshot;

        service.NotifyChanged();
        Assert.True(service.ValidationStale);

        Assert.NotSame(firstSnapshot, service.Snapshot);
        Assert.NotSame(firstErrors, service.Validate());
        Assert.False(service.ValidationStale);
        Assert.True(service.ValidationRan);
    }

    [Fact(DisplayName = "ADM-16: 文書の読み替えで検証・スナップショットは新文書へ切替わる（Phase 38）")]
    public void Reload_SwitchesSnapshotAndValidation()
    {
        var service = new AdminDocumentService(new HttpClient());
        Assert.True(service.LoadJson(MinimalJson(), "a"), service.LoadFailure);
        IReadOnlyList<MasterValidationError> firstErrors = service.Validate();
        MasterDataSnapshot? firstSnapshot = service.Snapshot;

        // 同じ構造でも文書インスタンスが違えばキャッシュは効かない。
        Assert.True(service.LoadJson(MinimalJson(), "b"), service.LoadFailure);

        Assert.NotSame(firstSnapshot, service.Snapshot);
        Assert.NotSame(firstErrors, service.Validate());
    }

    [Fact(DisplayName = "ADM-17: 未確定の不正入力の追加・解消で検証結果が追従し、スナップショットは再利用される（Phase 38）")]
    public void InvalidInput_AffectsValidationWithoutRebuildingSnapshot()
    {
        var service = new AdminDocumentService(new HttpClient());
        Assert.True(service.LoadJson(MinimalJson(), "test"), service.LoadFailure);

        int baseErrorCount = service.Validate().Count;
        MasterDataSnapshot? snapshot = service.Snapshot;
        var owner = new object();

        // 不正入力の登録は _editCounter を進めないが、検証結果には反映される。
        service.SetEditorInvalid(owner, "field", "abc");
        IReadOnlyList<MasterValidationError> invalidErrors = service.Validate();
        Assert.Equal(baseErrorCount + 1, invalidErrors.Count);
        Assert.Contains(invalidErrors, e => e.EntityKind == "入力");
        Assert.False(service.ValidationStale);

        // 登録状態のまま再検証しても結果は安定する。
        Assert.Equal(baseErrorCount + 1, service.Validate().Count);

        // 解消すると元の結果へ戻る。どの経路でもスナップショットは同一インスタンス。
        service.SetEditorInvalid(owner, "field", null);
        Assert.Equal(baseErrorCount, service.Validate().Count);
        Assert.Same(snapshot, service.Snapshot);
    }
}
