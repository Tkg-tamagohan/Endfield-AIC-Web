using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;

namespace EndfieldAicWeb.Infrastructure.Icons;

/// <summary>
/// IconKey からアイコンファイルのパスを解決する。
/// マニフェスト収録キーは Bytes/Sha256 一致ファイルのみ採用し、
/// 収録外キーは <c>icons/&lt;Key&gt;.png</c> のファイル名一致で解決する（旧 AO の差し込み運用を継承）。
/// 未設定・欠落・不一致はすべてフォールバック（null）とする。
/// </summary>
public sealed class IconResolver
{
    private readonly IIconFileProvider _files;
    private readonly IReadOnlyDictionary<string, IconEntry> _manifest;

    /// <param name="manifest">マスタ文書の Icons 節。キー重複は先勝ちで許容する。</param>
    /// <param name="files">アイコンファイルの取得元。</param>
    public IconResolver(IReadOnlyList<IconEntry> manifest, IIconFileProvider files)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(files);
        _files = files;
        _manifest = manifest
            .DistinctBy(e => e.Key)
            .ToDictionary(e => e.Key, StringComparer.Ordinal);
    }

    /// <summary>
    /// IconKey をマニフェスト相対パス（<c>icons/&lt;Key&gt;.png</c>）へ解決する。
    /// 解決できない場合は null（フォールバック表示）。
    /// </summary>
    public string? Resolve(string? iconKey)
    {
        if (iconKey is null || !IconKeyRules.IsValid(iconKey) || iconKey == IconKeyRules.PlaceholderKey)
        {
            return null;
        }

        if (_manifest.TryGetValue(iconKey, out IconEntry? entry))
        {
            // 収録キーはハッシュ一致のみ採用（取得破損・改ざんへの防御）。
            byte[]? content = _files.ReadAllBytes(entry.File);
            return content is not null && IconFiles.Matches(content, entry) ? entry.File : null;
        }

        // 収録外キーはファイル名一致だけで採用。
        string path = IconKeyRules.ManifestFile(iconKey);
        return _files.ReadAllBytes(path) is not null ? path : null;
    }
}
