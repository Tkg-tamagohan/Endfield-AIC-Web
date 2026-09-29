using System.IO.Compression;
using System.Security.Cryptography;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;
using EndfieldAicWeb.Infrastructure.Icons;
using EndfieldAicWeb.Infrastructure.Transfer;

namespace EndfieldAicWeb.Infrastructure.Tests;

/// <summary>
/// アイコン取り込み・エクスポートパイプラインの検証テスト。
/// ケース ID は docs/test-specification-phase7.md（IMP・EXP・ZIP）に対応する。
/// </summary>
public class IconPipelineTests
{
    // I-02: 内容は問わない固定バイト列。
    private static readonly byte[] IconA = [0xA0, 0xA1, 0xA2];
    private static readonly byte[] IconB = [0xB0, 0xB1, 0xB2, 0xB3];

    private static IconEntry Entry(string key, byte[] content) => new()
    {
        Key = key,
        File = $"icons/{key}.png",
        Sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
        Bytes = content.LongLength,
    };

    // I-01: アイコン参照を含む最小文書。
    private static MasterDocument I01() => new()
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
            new Facility { Id = "f-asm", Name = "加工機", IconKey = "icon-fac", VersionAdded = "1.0.0" },
        ],
        Environments =
        [
            new Domain.Models.Environment { Id = "env-gas", Name = "ガス環境", VersionAdded = "1.0.0", ProviderFacilityId = "f-asm", ConsumeItemId = "i-ore" },
        ],
        GameEvents =
        [
            new GameEvent { Id = "ev-on", Name = "開催イベント", VersionAdded = "1.0.0" },
        ],
        Recipes =
        [
            new Recipe { Id = "r-part", Name = "汎用部品", VersionAdded = "1.0.0" },
        ],
        Icons = [Entry("icon-ore", IconA), Entry("icon-part", IconB), Entry("icon-fac", IconA)],
    };

    private static InMemoryIconFileProvider Store(params (string Path, byte[] Bytes)[] files)
    {
        var store = new InMemoryIconFileProvider();
        foreach ((string path, byte[] bytes) in files)
        {
            Assert.True(store.Set(path, bytes), $"Set が失敗: {path}");
        }

        return store;
    }

    // IMP: InMemoryIconFileProvider

    [Fact]
    public void IMP01_登録したパスを読み取れる()
    {
        InMemoryIconFileProvider store = Store(("icons/a.png", IconA));

        Assert.Equal(IconA, store.ReadAllBytes("icons/a.png"));
        Assert.Contains("icons/a.png", store.Paths);
    }

    [Theory]
    [InlineData("icons\\a.png")]
    [InlineData("/icons/a.png")]
    [InlineData("\\icons\\a.png")]
    public void IMP02_区切りと先頭スラッシュを正規化して読める(string path)
    {
        InMemoryIconFileProvider store = Store((path, IconA));

        Assert.Equal(IconA, store.ReadAllBytes("icons/a.png"));
    }

    [Theory]
    [InlineData("../x.png")]
    [InlineData("a/../x.png")]
    [InlineData("..\\x.png")]
    [InlineData("")]
    [InlineData("   ")]
    public void IMP03_不正なパスは登録しない(string path)
    {
        var store = new InMemoryIconFileProvider();

        Assert.False(store.Set(path, IconA));
        Assert.Empty(store.Paths);
    }

    [Fact]
    public void IMP04_未登録のパスはnullを返す()
    {
        Assert.Null(new InMemoryIconFileProvider().ReadAllBytes("icons/a.png"));
    }

    [Fact]
    public void IMP05_Clearで全件消える()
    {
        InMemoryIconFileProvider store = Store(("icons/a.png", IconA));
        store.Clear();

        Assert.Empty(store.Paths);
        Assert.Null(store.ReadAllBytes("icons/a.png"));
    }

    // EXP: IconExportPlanner

    [Fact]
    public void EXP01_参照キーを文書の出現順に重複なく列挙する()
    {
        MasterDocument doc = I01();
        // 同じキーを 2 エンティティが参照しても 1 回だけ列挙される。
        doc.Items[2].IconKey = "icon-ore";

        Assert.Equal(["icon-ore", "icon-part", "icon-fac"], IconExportPlanner.ReferencedKeys(doc));
    }

    [Fact]
    public void EXP02_未設定とプレースホルダと無効キーは列挙しない()
    {
        MasterDocument doc = I01();
        doc.Items[0].IconKey = null;
        doc.Items[1].IconKey = IconKeyRules.PlaceholderKey;
        doc.Items[2].IconKey = "bad key!";
        doc.Facilities[0].IconKey = "icon-fac";

        Assert.Equal(["icon-fac"], IconExportPlanner.ReferencedKeys(doc));
    }

    [Fact]
    public void EXP03_バイト列からエントリを生成する()
    {
        IconEntry entry = IconExportPlanner.CreateEntry("icon-a", IconA);

        Assert.Equal("icon-a", entry.Key);
        Assert.Equal("icons/icon-a.png", entry.File);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(IconA)).ToLowerInvariant(), entry.Sha256);
        Assert.Equal(IconA.LongLength, entry.Bytes);
    }

    [Fact]
    public void EXP04_エンティティIdから既定キーを提案する()
    {
        Assert.Equal("icon-i-ore", IconExportPlanner.SuggestKey("i-ore"));
    }

    [Theory]
    [InlineData("a b.c", "icon-a-b-c")]
    [InlineData("a/b\\c", "icon-a-b-c")]
    public void EXP05_無効文字を置き換えて有効なキーを提案する(string entityId, string expected)
    {
        string key = IconExportPlanner.SuggestKey(entityId);

        Assert.Equal(expected, key);
        Assert.True(IconKeyRules.IsValid(key));
    }

    [Fact]
    public void EXP06_長いエンティティIdでも64文字以内の有効なキーを提案する()
    {
        string key = IconExportPlanner.SuggestKey(new string('a', 100));

        Assert.True(key.Length <= 64);
        Assert.True(IconKeyRules.IsValid(key));
    }

    [Fact]
    public void EXP07_参照キーのエントリを実ファイルから再計算して構築する()
    {
        MasterDocument doc = I01();
        // マニフェストの記述値を古い値に変えても、実ファイルから再計算されることを見る。
        doc.Icons[0].Sha256 = new string('0', 64);
        doc.Icons[0].Bytes = 1;
        InMemoryIconFileProvider store = Store(
            ("icons/icon-ore.png", IconA),
            ("icons/icon-part.png", IconB),
            ("icons/icon-fac.png", IconA));
        var errors = new List<MasterValidationError>();

        List<IconEntry> manifest = IconExportPlanner.BuildManifest(doc, store, errors);

        Assert.Empty(errors);
        Assert.Equal(3, manifest.Count);
        IconEntry ore = manifest.Single(e => e.Key == "icon-ore");
        Assert.Equal(IconA.LongLength, ore.Bytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(IconA)).ToLowerInvariant(), ore.Sha256);
    }

    [Fact]
    public void EXP08_マニフェスト未登録の参照キーはエラーになる()
    {
        MasterDocument doc = I01();
        doc.Icons.RemoveAll(e => e.Key == "icon-part");
        InMemoryIconFileProvider store = Store(
            ("icons/icon-ore.png", IconA),
            ("icons/icon-fac.png", IconA));
        var errors = new List<MasterValidationError>();

        List<IconEntry> manifest = IconExportPlanner.BuildManifest(doc, store, errors);

        Assert.Contains(errors, e => e.EntityKind == "Icons" && e.EntityId == "icon-part" && e.Field == "Key");
        Assert.Equal(2, manifest.Count);
    }

    [Fact]
    public void EXP09_ファイル実体がない参照キーはエラーになる()
    {
        MasterDocument doc = I01();
        InMemoryIconFileProvider store = Store(
            ("icons/icon-part.png", IconB),
            ("icons/icon-fac.png", IconA));
        var errors = new List<MasterValidationError>();

        List<IconEntry> manifest = IconExportPlanner.BuildManifest(doc, store, errors);

        Assert.Contains(errors, e => e.EntityKind == "Icons" && e.EntityId == "icon-ore" && e.Field == "File");
        Assert.Equal(2, manifest.Count);
    }

    [Fact]
    public void EXP10_未参照の孤立エントリは出力に含めない()
    {
        MasterDocument doc = I01();
        doc.Icons.Add(Entry("icon-orphan", IconA));
        InMemoryIconFileProvider store = Store(
            ("icons/icon-ore.png", IconA),
            ("icons/icon-part.png", IconB),
            ("icons/icon-fac.png", IconA),
            ("icons/icon-orphan.png", IconA));
        var errors = new List<MasterValidationError>();

        List<IconEntry> manifest = IconExportPlanner.BuildManifest(doc, store, errors);

        Assert.Empty(errors);
        Assert.DoesNotContain(manifest, e => e.Key == "icon-orphan");
    }

    // ZIP: IconArchive

    [Fact]
    public void ZIP01_生成したzipを読み取り往復できる()
    {
        const string json = """{"SchemaVersion":1}""";
        MasterDocument doc = I01();
        InMemoryIconFileProvider store = Store(
            ("icons/icon-ore.png", IconA),
            ("icons/icon-part.png", IconB),
            ("icons/icon-fac.png", IconA));

        byte[] zip = IconArchive.CreateZip(json, doc.Icons, store);

        Assert.True(IconArchive.TryReadZip(zip, out string? jsonOut, out IReadOnlyDictionary<string, byte[]> icons));
        Assert.Equal(json, jsonOut);
        Assert.Equal(3, icons.Count);
        Assert.Equal(IconA, icons["icons/icon-ore.png"]);
        Assert.Equal(IconB, icons["icons/icon-part.png"]);
    }

    [Fact]
    public void ZIP02_zip内の配置はdata配下になる()
    {
        MasterDocument doc = I01();
        InMemoryIconFileProvider store = Store(
            ("icons/icon-ore.png", IconA),
            ("icons/icon-part.png", IconB),
            ("icons/icon-fac.png", IconA));

        byte[] zip = IconArchive.CreateZip("{}", doc.Icons, store);

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        Assert.Equal(
            ["data/master.json", "data/icons/icon-ore.png", "data/icons/icon-part.png", "data/icons/icon-fac.png"],
            archive.Entries.Select(e => e.FullName));
    }

    [Fact]
    public void ZIP03_data前置きのないzipも読み取れる()
    {
        byte[] zip;
        using (var stream = new MemoryStream())
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var writer = new StreamWriter(archive.CreateEntry("master.json").Open());
                writer.Write("{}");
            }

            using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
            {
                ZipArchiveEntry icon = archive.CreateEntry("icons/a.png");
                using Stream entryStream = icon.Open();
                entryStream.Write(IconA, 0, IconA.Length);
            }

            zip = stream.ToArray();
        }

        Assert.True(IconArchive.TryReadZip(zip, out string? json, out IReadOnlyDictionary<string, byte[]> icons));
        Assert.Equal("{}", json);
        Assert.Equal(IconA, icons["icons/a.png"]);
    }

    [Fact]
    public void ZIP04_zipでないバイト列は読み取りに失敗する()
    {
        Assert.False(IconArchive.TryReadZip([0x00, 0x01, 0x02], out _, out _));
    }

    [Fact]
    public void ZIP05_正本JSONのないzipは読み取りに失敗する()
    {
        byte[] zip;
        using (var stream = new MemoryStream())
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                ZipArchiveEntry icon = archive.CreateEntry("data/icons/a.png");
                using Stream entryStream = icon.Open();
                entryStream.Write(IconA, 0, IconA.Length);
            }

            zip = stream.ToArray();
        }

        Assert.False(IconArchive.TryReadZip(zip, out string? json, out _));
        Assert.Null(json);
    }
}
