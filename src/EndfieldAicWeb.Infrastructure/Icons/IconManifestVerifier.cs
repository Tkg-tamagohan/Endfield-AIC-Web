using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;

namespace EndfieldAicWeb.Infrastructure.Icons;

/// <summary>
/// Icons マニフェスト各エントリと実ファイルの照合検証（Key/File/Sha256/Bytes、仕様決定 R）。
/// 構造規則（形式）は読み込み時の構造検証が担い、ここでは実ファイルとの一致を検査する。
/// 違反は例外ではなくエラー一覧として集約して返す。
/// </summary>
public static class IconManifestVerifier
{
    /// <summary>
    /// 各エントリの File 実体が存在し、Bytes・Sha256 が一致するかを検査する。
    /// </summary>
    public static IReadOnlyList<MasterValidationError> Verify(
        IReadOnlyList<IconEntry> manifest,
        IIconFileProvider files)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(files);

        var errors = new List<MasterValidationError>();
        foreach (IconEntry entry in manifest)
        {
            byte[]? content = files.ReadAllBytes(entry.File);
            if (content is null)
            {
                errors.Add(new MasterValidationError(
                    "Icons", entry.Key, "File",
                    $"アイコンファイルが見つかりません: {entry.File}"));
                continue;
            }

            if (content.LongLength != entry.Bytes)
            {
                errors.Add(new MasterValidationError(
                    "Icons", entry.Key, "Bytes",
                    $"アイコンファイルのサイズがマニフェストと一致しません: {entry.File}（{content.LongLength} != {entry.Bytes}）"));
            }

            if (IconFiles.Sha256Hex(content) != entry.Sha256)
            {
                errors.Add(new MasterValidationError(
                    "Icons", entry.Key, "Sha256",
                    $"アイコンファイルのハッシュがマニフェストと一致しません: {entry.File}"));
            }
        }

        return errors;
    }

    /// <summary>
    /// マニフェスト各エントリについて、プロバイダ内の実体が Bytes・Sha256 一致するか数える。
    /// .json 読み込みで温存したアイコンストアを新マニフェストへ再照合し、
    /// 一致分だけを取得済みとして数える用途（不一致・欠落は未取得扱い）。
    /// </summary>
    public static int CountMatching(IReadOnlyList<IconEntry> manifest, IIconFileProvider files)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(files);

        int count = 0;
        foreach (IconEntry entry in manifest)
        {
            if (files.ReadAllBytes(entry.File) is { } content && IconFiles.Matches(content, entry))
            {
                count++;
            }
        }

        return count;
    }
}
