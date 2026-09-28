using System.Globalization;
using System.Text.RegularExpressions;

namespace EndfieldAicWeb.Domain;

/// <summary>
/// semver.org 準拠のバージョン（major.minor.patch[-prerelease][+build]）。
/// System.Version は prerelease ラベルを扱えないため、仕様どおり semver でパース・比較する。
/// 構築メタデータ（+ 以降）は優先度に含めない。
/// </summary>
internal readonly struct SemVersion : IComparable<SemVersion>
{
    private const string Identifier = @"(?:0|[1-9]\d*|\d*[a-zA-Z\-][0-9a-zA-Z\-]*)";

    private static readonly Regex Pattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)" +
        $@"(?:-({Identifier}(?:\.{Identifier})*))?" +
        @"(?:\+([0-9a-zA-Z\-]+(?:\.[0-9a-zA-Z\-]+)*))?$",
        RegexOptions.CultureInvariant);

    private SemVersion(int major, int minor, int patch, string[]? prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>prerelease 識別子の列。null はリリース版。</summary>
    public string[]? Prerelease { get; }

    public static bool TryParse(string? text, out SemVersion version)
    {
        version = default;
        if (text is null)
        {
            return false;
        }

        Match match = Pattern.Match(text);
        if (!match.Success)
        {
            return false;
        }

        // 構文上は有効でも int 範囲を超える要素は扱えないため、パース失敗として返す。
        if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int major)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minor)
            || !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
        {
            return false;
        }

        version = new SemVersion(
            major,
            minor,
            patch,
            match.Groups[4].Success ? match.Groups[4].Value.Split('.') : null);
        return true;
    }

    /// <summary>semver.org §11 の優先度比較。</summary>
    public int CompareTo(SemVersion other)
    {
        int cmp = Major.CompareTo(other.Major);
        if (cmp != 0)
        {
            return cmp;
        }

        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0)
        {
            return cmp;
        }

        cmp = Patch.CompareTo(other.Patch);
        if (cmp != 0)
        {
            return cmp;
        }

        string[] self = Prerelease ?? [];
        string[] theirs = other.Prerelease ?? [];

        // prerelease なしのほうが上位。
        if (self.Length == 0 || theirs.Length == 0)
        {
            return self.Length == theirs.Length ? 0 : (self.Length == 0 ? 1 : -1);
        }

        for (int i = 0; i < Math.Max(self.Length, theirs.Length); i++)
        {
            if (i >= self.Length)
            {
                return -1;
            }

            if (i >= theirs.Length)
            {
                return 1;
            }

            cmp = CompareIdentifier(self[i], theirs[i]);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return 0;
    }

    /// <summary>数値識別子は数値比較かつ英数識別子より下位。同種は ASCII 順。</summary>
    private static int CompareIdentifier(string self, string theirs)
    {
        bool selfNumeric = IsNumeric(self);
        bool theirsNumeric = IsNumeric(theirs);
        if (selfNumeric && theirsNumeric)
        {
            // 先頭ゼロはパースで排除済みなので桁数→辞書順で数値比較できる。
            if (self.Length != theirs.Length)
            {
                return self.Length < theirs.Length ? -1 : 1;
            }

            return string.CompareOrdinal(self, theirs);
        }

        if (selfNumeric != theirsNumeric)
        {
            return selfNumeric ? -1 : 1;
        }

        return string.CompareOrdinal(self, theirs);
    }

    private static bool IsNumeric(string identifier) =>
        identifier.All(c => c is >= '0' and <= '9');
}
