using System.Security.Cryptography;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Infrastructure.Icons;

namespace EndfieldAicWeb.Infrastructure.Tests;

/// <summary>
/// Icons マニフェスト照合・IconKey 解決の検証テスト。ケース ID は docs/test-specification-phase3.md に対応する。
/// </summary>
public class IconManifestTests
{
    private const string IconOrePath = "icons/icon-ore.png";

    private static IconEntry ValidEntry() => new()
    {
        Key = "icon-ore",
        File = IconOrePath,
        Sha256 = TestJson.IconOreSha256,
        Bytes = TestJson.IconOreContent.LongLength,
    };

    private sealed class InMemoryIconProvider : IIconFileProvider
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public InMemoryIconProvider Add(string path, byte[] bytes)
        {
            _files[path] = bytes;
            return this;
        }

        public byte[]? ReadAllBytes(string path) =>
            _files.TryGetValue(path, out byte[]? bytes) ? bytes : null;
    }

    [Fact(DisplayName = "ICO-01: 正当なマニフェストと一致する実ファイルは Verify エラーなし")]
    public void Verify_ValidManifest_NoErrors()
    {
        var provider = new InMemoryIconProvider().Add(IconOrePath, TestJson.IconOreContent);

        var errors = IconManifestVerifier.Verify([ValidEntry()], provider);

        Assert.Empty(errors);
    }

    [Fact(DisplayName = "ICO-02: Bytes が実ファイルと一致しないと Verify エラー")]
    public void Verify_BytesMismatch_Error()
    {
        IconEntry entry = ValidEntry();
        entry.Bytes += 1;
        var provider = new InMemoryIconProvider().Add(IconOrePath, TestJson.IconOreContent);

        var errors = IconManifestVerifier.Verify([entry], provider);

        Assert.Contains(errors, e => e.Field == "Bytes");
    }

    [Fact(DisplayName = "ICO-03: Sha256 が実ファイルと一致しないと Verify エラー")]
    public void Verify_ShaMismatch_Error()
    {
        IconEntry entry = ValidEntry();
        entry.Sha256 = Convert.ToHexString(SHA256.HashData([0x00])).ToLowerInvariant();
        var provider = new InMemoryIconProvider().Add(IconOrePath, TestJson.IconOreContent);

        var errors = IconManifestVerifier.Verify([entry], provider);

        Assert.Contains(errors, e => e.Field == "Sha256");
    }

    [Fact(DisplayName = "ICO-04: ファイル欠落は Verify エラー")]
    public void Verify_MissingFile_Error()
    {
        var provider = new InMemoryIconProvider();

        var errors = IconManifestVerifier.Verify([ValidEntry()], provider);

        Assert.Contains(errors, e => e.Field == "File");
    }

    [Fact(DisplayName = "ICO-05: 収録キーは照合一致で icons/icon-ore.png を返す")]
    public void Resolve_ManifestKey_ReturnsPath()
    {
        var provider = new InMemoryIconProvider().Add(IconOrePath, TestJson.IconOreContent);
        var resolver = new IconResolver([ValidEntry()], provider);

        Assert.Equal(IconOrePath, resolver.Resolve("icon-ore"));
    }

    [Fact(DisplayName = "ICO-06: 収録キーでも不一致・欠落なら null（フォールバック）")]
    public void Resolve_ManifestKeyMismatch_ReturnsNull()
    {
        IconEntry tampered = ValidEntry();
        tampered.Sha256 = Convert.ToHexString(SHA256.HashData([0x00])).ToLowerInvariant();

        // 実ファイルは正しいがマニフェスト記述が合わない。
        var hashMismatch = new IconResolver(
            [tampered],
            new InMemoryIconProvider().Add(IconOrePath, TestJson.IconOreContent));

        // マニフェストは正しいが実ファイルがない。
        var missing = new IconResolver([ValidEntry()], new InMemoryIconProvider());

        Assert.Null(hashMismatch.Resolve("icon-ore"));
        Assert.Null(missing.Resolve("icon-ore"));
    }

    [Fact(DisplayName = "ICO-07: 収録外キーは icons/<Key>.png の存在で解決する")]
    public void Resolve_UnlistedKey_FallsBackToFileName()
    {
        var provider = new InMemoryIconProvider().Add("icons/icon-extra.png", [0x01, 0x02]);
        var resolver = new IconResolver([ValidEntry()], provider);

        Assert.Equal("icons/icon-extra.png", resolver.Resolve("icon-extra"));
    }

    [Theory(DisplayName = "ICO-08: 解決不可のキーはすべて null へフォールバック")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("icon-placeholder")]
    [InlineData("icon invalid!")]
    [InlineData("icon-absent")]
    public void Resolve_UnresolvableKey_ReturnsNull(string? key)
    {
        var provider = new InMemoryIconProvider().Add(IconOrePath, TestJson.IconOreContent);
        var resolver = new IconResolver([ValidEntry()], provider);

        Assert.Null(resolver.Resolve(key));
    }

    [Fact(DisplayName = "ICO-09: FileSystemIconProvider 経由で解決・欠落時は null")]
    public void Resolve_FileSystemProvider()
    {
        string root = Path.Combine(Path.GetTempPath(), $"aic-icons-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "icons"));
            File.WriteAllBytes(Path.Combine(root, "icons", "icon-ore.png"), TestJson.IconOreContent);

            var provider = new FileSystemIconProvider(root);
            var resolver = new IconResolver([ValidEntry()], provider);

            Assert.Equal(IconOrePath, resolver.Resolve("icon-ore"));
            Assert.Null(resolver.Resolve("icon-missing"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "ICO-11: ルートディレクトリの末尾に区切り文字があっても解決できる")]
    public void FileSystemProvider_TrailingSeparatorRoot_Resolves()
    {
        string root = Path.Combine(Path.GetTempPath(), $"aic-icons-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "icons"));
            File.WriteAllBytes(Path.Combine(root, "icons", "icon-ore.png"), TestJson.IconOreContent);

            var provider = new FileSystemIconProvider(root + Path.DirectorySeparatorChar);

            Assert.NotNull(provider.ReadAllBytes(IconOrePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "ICO-12: シンボリックリンクによるルート外参照は拒否される")]
    public void FileSystemProvider_SymlinkEscape_ReturnsNull()
    {
        string root = Path.Combine(Path.GetTempPath(), $"aic-icons-{Guid.NewGuid():N}");
        string outside = Path.Combine(Path.GetTempPath(), $"aic-secret-{Guid.NewGuid():N}.png");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "icons"));
            File.WriteAllBytes(outside, [0x0B, 0xAD]);
            File.CreateSymbolicLink(Path.Combine(root, "icons", "link.png"), outside);

            var provider = new FileSystemIconProvider(root);

            Assert.Null(provider.ReadAllBytes("icons/link.png"));
        }
        finally
        {
            File.Delete(outside);
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory(DisplayName = "ICO-10: ルート外へ出る相対パスは拒否される")]
    [InlineData("../secret.txt")]
    [InlineData("icons/../../secret.txt")]
    [InlineData("..\\secret.txt")]
    public void FileSystemProvider_Traversal_ReturnsNull(string path)
    {
        string root = Path.Combine(Path.GetTempPath(), $"aic-icons-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "icons"));
            // ルートの親に秘密ファイルを配置して、到達できることを試行する。
            string parentSecret = Path.Combine(root, "icons", "..", "..", "secret.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(parentSecret)!);
            File.WriteAllText(parentSecret, "secret");

            var provider = new FileSystemIconProvider(root);

            Assert.Null(provider.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(Path.Combine(root, "icons", "..", "..", "secret.txt"));
            Directory.Delete(root, recursive: true);
        }
    }
}
