using System.Text.RegularExpressions;

namespace EndfieldAicWeb.Application;

/// <summary>
/// エクスポート時の DataVersion 次版提案。`major.minor.patch` 形なら patch+1 を提案し、
/// それ以外の形式は提案しない（null）。prerelease・build 付きは機械的に上げられないため提案外。
/// </summary>
public static class DataVersionBumper
{
    private static readonly Regex VersionPattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$",
        RegexOptions.CultureInvariant);

    /// <summary>現行版の patch を 1 上げた提案値を返す。提案できない形式は null。</summary>
    public static string? SuggestNext(string? current)
    {
        if (current is null)
        {
            return null;
        }

        Match match = VersionPattern.Match(current);
        if (!match.Success)
        {
            return null;
        }

        if (!int.TryParse(match.Groups[3].Value, out int patch) || patch == int.MaxValue)
        {
            return null;
        }

        return $"{match.Groups[1].Value}.{match.Groups[2].Value}.{patch + 1}";
    }
}
