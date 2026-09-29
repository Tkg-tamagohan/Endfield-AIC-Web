using EndfieldAicWeb.Admin.Services;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Infrastructure.Icons;
using EndfieldAicWeb.Infrastructure.Transfer;
using DomainEnvironment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Admin.Tests;

/// <summary>
/// 管理ツールのアイコン編集・zip エクスポートの検証テスト。
/// ケース ID は docs/test-specification-phase7.md（ADM）に対応する。
/// </summary>
public class AdminIconTests
{
    private static readonly byte[] IconA = [0xA0, 0xA1, 0xA2];
    private static readonly byte[] IconB = [0xB0, 0xB1, 0xB2, 0xB3];

    /// <summary>I-01: アイコン参照を含む最小文書（icon-ore・icon-part・icon-fac を登録済み）。</summary>
    private static AdminDocumentService I01Loaded()
    {
        MasterDocument doc = new()
        {
            SchemaVersion = 1,
            DataVersion = "1.0.0",
            Items =
            [
                new Item { Id = "i-ore", Name = "原鉱石", IconKey = "icon-ore", VersionAdded = "1.0.0", Category = "素材", TransportKind = TransportKind.Belt },
                new Item { Id = "i-part", Name = "汎用部品", IconKey = "icon-part", VersionAdded = "1.0.0", Category = "素材", TransportKind = TransportKind.Belt },
                new Item { Id = "i-none", Name = "未設定品", VersionAdded = "1.0.0", Category = "素材", TransportKind = TransportKind.Belt },
            ],
            Facilities =
            [
                new Facility { Id = "f-asm", Name = "加工機", IconKey = "icon-fac", VersionAdded = "1.0.0", Width = 3, Height = 3 },
            ],
            Environments =
            [
                new DomainEnvironment
                {
                    Id = "env-gas", Name = "ガス環境", ProviderFacilityId = "f-asm",
                    ConsumeItemId = "i-ore", ConsumeRatePerSecond = 1, VersionAdded = "1.0.0",
                },
            ],
            GameEvents =
            [
                new GameEvent { Id = "ev-on", Name = "開催イベント", VersionAdded = "1.0.0" },
            ],
            Recipes =
            [
                new Recipe
                {
                    Id = "r-part", Name = "汎用部品", VersionAdded = "1.0.0",
                    Inputs = [new RecipeInput { ItemId = "i-ore", Quantity = 2 }],
                    Outputs = [new RecipeOutput { ItemId = "i-part", Quantity = 1, SortOrder = 0 }],
                    Facilities = [new RecipeFacility { RecipeId = "r-part", FacilityId = "f-asm", CycleTime = 4 }],
                },
            ],
        };
        var service = new AdminDocumentService(new HttpClient());
        string json = MasterExporter.Export(doc);
        Assert.True(service.LoadJson(json, "test"), service.LoadFailure);
        // I-01 のマニフェスト相当をストアへ登録する（HTTP 取得を経ない直接読み込みのため）。
        service.RegisterIcon(doc.Items[0], IconA);
        service.RegisterIcon(doc.Items[1], IconB);
        service.RegisterIcon(doc.Facilities[0], IconA);
        return service;
    }

    [Fact]
    public void ADM01_キー未設定のエンティティへ登録するとicon_Id形に補完される()
    {
        AdminDocumentService service = I01Loaded();
        Item target = service.Document!.Items.Single(i => i.Id == "i-none");

        service.RegisterIcon(target, IconA);

        Assert.Equal("icon-i-none", target.IconKey);
        Assert.Contains(service.Document.Icons, e => e.Key == "icon-i-none");
        Assert.NotNull(service.IconDataUrl("icon-i-none"));
    }

    [Fact]
    public void ADM02_既存キーの取り込みはキーを維持してエントリを更新する()
    {
        AdminDocumentService service = I01Loaded();
        Item target = service.Document!.Items.Single(i => i.Id == "i-ore");
        int before = service.Document.Icons.Count;

        service.RegisterIcon(target, IconB);

        Assert.Equal("icon-ore", target.IconKey);
        Assert.Equal(before, service.Document.Icons.Count);
        IconEntry entry = service.Document.Icons.Single(e => e.Key == "icon-ore");
        Assert.Equal(IconB.LongLength, entry.Bytes);
    }

    [Fact]
    public void ADM03_クリアするとキーが外れマニフェストエントリは残る()
    {
        AdminDocumentService service = I01Loaded();
        Item target = service.Document!.Items.Single(i => i.Id == "i-ore");

        service.ClearIcon(target);

        Assert.Null(target.IconKey);
        Assert.Null(service.IconDataUrl(target.IconKey));
        Assert.Contains(service.Document.Icons, e => e.Key == "icon-ore");
    }

    [Fact]
    public void ADM04_キー未設定のレシピは主出力アイテムへフォールバックする()
    {
        AdminDocumentService service = I01Loaded();
        Recipe recipe = service.Document!.Recipes.Single(r => r.Id == "r-part");

        Assert.Equal("icon-part", service.EffectiveIconKey(recipe));
    }

    [Fact]
    public void ADM05_自身のキーを持つレシピはそのキーを返す()
    {
        AdminDocumentService service = I01Loaded();
        Recipe recipe = service.Document!.Recipes.Single(r => r.Id == "r-part");
        recipe.IconKey = "icon-own";

        Assert.Equal("icon-own", service.EffectiveIconKey(recipe));
    }

    [Fact]
    public void ADM06_正常なzipエクスポートは参照分のアイコンを含み孤立エントリを落とす()
    {
        AdminDocumentService service = I01Loaded();
        // どこからも参照されない孤立エントリを追加する。
        service.Document!.Icons.Add(IconExportPlanner.CreateEntry("icon-orphan", IconA));

        ExportZipOutcome outcome = service.ExportZip("1.0.1");

        Assert.True(outcome.Success);
        Assert.NotNull(outcome.ZipBytes);
        Assert.True(IconArchive.TryReadZip(outcome.ZipBytes, out string? json, out IReadOnlyDictionary<string, byte[]> icons));
        Assert.NotNull(json);
        Assert.Equal(3, icons.Count);
        Assert.DoesNotContain(service.Document.Icons, e => e.Key == "icon-orphan");
        Assert.Equal("1.0.1", service.Document.DataVersion);
    }

    [Fact]
    public void ADM07_参照キーのファイルが未取得ならエクスポートを拒否する()
    {
        I01Loaded();
        // ストアを空にして「取得できなかった」状態を作る（エントリは残す）。
        var fresh = new AdminDocumentService(new HttpClient());
        MasterDocument doc = new()
        {
            SchemaVersion = 1,
            DataVersion = "1.0.0",
            Items = [new Item { Id = "i-ore", Name = "原鉱石", IconKey = "icon-ore", VersionAdded = "1.0.0", Category = "素材", TransportKind = TransportKind.Belt }],
            Facilities = [],
            Environments = [],
            GameEvents = [],
            Recipes = [],
            Icons = [IconExportPlanner.CreateEntry("icon-ore", IconA)],
        };
        Assert.True(fresh.LoadJson(MasterExporter.Export(doc), "test"));

        ExportZipOutcome outcome = fresh.ExportZip("1.0.1");

        Assert.False(outcome.Success);
        Assert.Null(outcome.ZipBytes);
        Assert.Contains(outcome.Errors, e => e.EntityKind == "Icons" && e.EntityId == "icon-ore" && e.Field == "File");
    }

    [Fact]
    public void ADM09_種別をまたいで同じ提案キーになる場合は一意のキーを割り当てる()
    {
        AdminDocumentService service = I01Loaded();
        // item と facility の Id は種別内でしか一意でないため、同一 Id が共存し得る。
        var item = new Item { Id = "shared", Name = "共用", VersionAdded = "1.0.0", Category = "素材", TransportKind = TransportKind.Belt };
        var facility = new Facility { Id = "shared", Name = "共用", VersionAdded = "1.0.0", Width = 1, Height = 1 };
        service.Document!.Items.Add(item);
        service.Document.Facilities.Add(facility);

        service.RegisterIcon(item, IconA);
        service.RegisterIcon(facility, IconB);

        Assert.Equal("icon-shared", item.IconKey);
        Assert.NotEqual(item.IconKey, facility.IconKey);
        Assert.Equal("icon-shared-2", facility.IconKey);
        // それぞれ別の画像を保持しており、先に登録した画像が上書きされていない。
        Assert.Equal(IconA.LongLength, service.Document.Icons.Single(e => e.Key == "icon-shared").Bytes);
        Assert.Equal(IconB.LongLength, service.Document.Icons.Single(e => e.Key == "icon-shared-2").Bytes);
    }

    [Fact]
    public void ADM10_マニフェストと一致しないzip内画像は未取得扱いでエクスポートを拒否する()
    {
        AdminDocumentService service = I01Loaded();
        ExportZipOutcome exported = service.ExportZip("1.0.1");
        Assert.True(exported.Success);

        // zip 内の icon-ore.png を別内容に差し替える（マニフェスト Sha256/Bytes と一致しない）。
        byte[] tampered;
        Assert.True(IconArchive.TryReadZip(exported.ZipBytes!, out string? json, out IReadOnlyDictionary<string, byte[]> icons));
        var rebuilt = new Dictionary<string, byte[]>(icons);
        rebuilt["icons/icon-ore.png"] = [0xFF, 0xFF];
        using (var stream = new MemoryStream())
        {
            using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                using var writer = new StreamWriter(archive.CreateEntry(IconArchive.JsonEntryName).Open());
                writer.Write(json);
            }

            using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
            {
                foreach ((string path, byte[] bytes) in rebuilt)
                {
                    System.IO.Compression.ZipArchiveEntry entry = archive.CreateEntry(IconArchive.IconsPrefix + Path.GetFileName(path));
                    using Stream entryStream = entry.Open();
                    entryStream.Write(bytes, 0, bytes.Length);
                }
            }

            tampered = stream.ToArray();
        }

        var reloaded = new AdminDocumentService(new HttpClient());
        Assert.True(reloaded.LoadZip(tampered, "master-export.zip"));
        Assert.Equal(3, reloaded.IconFilesExpected);
        Assert.Equal(2, reloaded.IconFilesLoaded);
        Assert.NotNull(reloaded.IconDataUrl("icon-part"));
        // 不一致ファイルはストアに入らないため、プレビューもエクスポートも欠落扱いになる。
        Assert.Null(reloaded.IconDataUrl("icon-ore"));
        Assert.False(reloaded.ExportZip("1.0.2").Success);
    }

    [Fact]
    public void ADM11_JSON読み込みでは温存ストアを新マニフェストで再照合する()
    {
        // zip 読み込みで検証済みの画像をストアに持った状態で、
        // 同じ File に異なる Sha256 を書いた .json を読み込むと未取得として数える。
        AdminDocumentService service = I01Loaded();
        ExportZipOutcome exported = service.ExportZip("1.0.1");
        Assert.True(exported.Success);
        var reloaded = new AdminDocumentService(new HttpClient());
        Assert.True(reloaded.LoadZip(exported.ZipBytes!, "master-export.zip"));
        Assert.Equal(3, reloaded.IconFilesLoaded);

        Assert.True(IconArchive.TryReadZip(exported.ZipBytes!, out string? json, out _));
        // icon-part は IconB 単独（icon-ore/icon-fac は IconA 共有で Sha256 も同じ）なので
        // この Sha256 だけを潰せば該当エントリのみが照合落ちする。
        string tamperedJson = json!.Replace(
            service.Document!.Icons.Single(e => e.Key == "icon-part").Sha256,
            new string('0', 64));
        Assert.True(reloaded.LoadJson(tamperedJson, "tampered.json"));
        Assert.Equal(3, reloaded.IconFilesExpected);
        Assert.Equal(2, reloaded.IconFilesLoaded);
        Assert.Null(reloaded.IconDataUrl("icon-part"));
        Assert.NotNull(reloaded.IconDataUrl("icon-ore"));
    }

    [Fact]
    public void ADM12_nullバージョンのエクスポートは必須違反で拒否され文書を汚さない()
    {
        AdminDocumentService service = I01Loaded();

        ExportOutcome json = service.Export(null!);
        ExportZipOutcome zip = service.ExportZip(null!);

        Assert.False(json.Success);
        Assert.False(zip.Success);
        Assert.Contains(json.Errors, e => e.Field == "DataVersion");
        Assert.Contains(zip.Errors, e => e.Field == "DataVersion");
        Assert.Equal("1.0.0", service.Document!.DataVersion);
    }

    [Fact]
    public void ADM08_エクスポートしたzipを読み込み直せる()
    {
        AdminDocumentService service = I01Loaded();
        ExportZipOutcome outcome = service.ExportZip("1.0.1");
        Assert.True(outcome.Success);

        var reloaded = new AdminDocumentService(new HttpClient());
        Assert.True(reloaded.LoadZip(outcome.ZipBytes!, "master-export.zip"));

        Assert.Equal(3, reloaded.IconFilesExpected);
        Assert.Equal(3, reloaded.IconFilesLoaded);
        Assert.NotNull(reloaded.IconDataUrl("icon-ore"));
    }
}
