using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;
using EndfieldAicWeb.Infrastructure.Icons;
using EndfieldAicWeb.Infrastructure.Transfer;

namespace EndfieldAicWeb.Infrastructure.Tests;

/// <summary>
/// リポジトリ同梱の data/master.json と data/icons/ を対象にしたデータ検証テスト。
/// 読み込み（構文・構造・意味）と Icons マニフェスト↔実ファイルの一致を C# 側の規則で担保する。
/// ケース ID は docs/phases/test-specification-phase7.md（CIV）に対応する。
/// </summary>
public class BundledMasterDataTests
{
    /// <summary>data/master.json を持つリポジトリルートを、実行ディレクトリから遡って探す。</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "master.json")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("data/master.json を持つリポジトリルートが見つかりません。");
    }

    private static MasterDocument LoadBundledMaster()
    {
        string root = FindRepoRoot();
        string json = File.ReadAllText(Path.Combine(root, "data", "master.json"));

        MasterJsonLoadResult result = MasterJsonLoader.Load(json);
        Assert.True(
            result.Success && result.Document is not null,
            string.Join("\n", result.Errors.Select(e => $"{e.EntityKind}/{e.EntityId} {e.Field}: {e.Message}")));
        return result.Document!;
    }

    [Fact(DisplayName = "CIV-05: data/master.json は読み込み検証（構文・構造・意味）を通過する")]
    public void MasterJson_Loads()
    {
        MasterDocument document = LoadBundledMaster();
        Assert.NotEmpty(document.Items);
        Assert.NotEmpty(document.Recipes);
    }

    [Fact(DisplayName = "CIV-06: Icons マニフェストは data/icons/ の実体と Bytes/Sha256 が一致する")]
    public void IconManifest_MatchesFiles()
    {
        string root = FindRepoRoot();
        MasterDocument document = LoadBundledMaster();

        IReadOnlyList<MasterValidationError> errors = IconManifestVerifier.Verify(
            document.Icons,
            new FileSystemIconProvider(Path.Combine(root, "data")));

        Assert.Empty(errors);
    }
}
