using System.Text.RegularExpressions;

namespace EndfieldAicWeb.Domain.Validation;

/// <summary>
/// アイコンキーの文字種制約とプレースホルダの共通規則（仕様決定 AP/AN）。
/// JSON 読み込み・Admin 保存・ファイル解決の各層で同一規則を使う。
/// </summary>
public static partial class IconKeyRules
{
    /// <summary>未設定・未解決時にプレースホルダ表示へ落とすための予約キー。</summary>
    public const string PlaceholderKey = "icon-placeholder";

    /// <summary>許容されるキーの形（英数字・ハイフン・アンダースコアの 1〜64 文字）。</summary>
    public const string Pattern = "^[A-Za-z0-9_-]{1,64}$";

    [GeneratedRegex(Pattern)]
    private static partial Regex KeyRegex();

    /// <summary>キーが文字種制約を満たすか。</summary>
    public static bool IsValid(string? key) =>
        key is not null && KeyRegex().IsMatch(key);

    /// <summary>プレースホルダ扱いか（未設定・空・予約キー）。</summary>
    public static bool IsPlaceholder(string? key) =>
        string.IsNullOrEmpty(key) || key == PlaceholderKey;

    /// <summary>キーに対応する画像ファイル名（<c>&lt;Key&gt;.png</c>）。</summary>
    public static string FileName(string key) => $"{key}.png";

    /// <summary>マニフェストの File 形式（<c>icons/&lt;Key&gt;.png</c>）。</summary>
    public static string ManifestFile(string key) => $"icons/{key}.png";
}
