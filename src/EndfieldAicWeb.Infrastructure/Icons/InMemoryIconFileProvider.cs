namespace EndfieldAicWeb.Infrastructure.Icons;

/// <summary>
/// <see cref="IIconFileProvider"/> のインメモリ実装。
/// Blazor WASM 側で取得したアイコン画像を保持し、読み書き両用で使う。
/// </summary>
public sealed class InMemoryIconFileProvider : IIconFileProvider
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    /// <summary>保持中のマニフェスト相対パス一覧。</summary>
    public IReadOnlyCollection<string> Paths => _files.Keys;

    public byte[]? ReadAllBytes(string path)
    {
        string? normalized = Normalize(path);
        return normalized is not null && _files.TryGetValue(normalized, out byte[]? bytes)
            ? bytes
            : null;
    }

    /// <summary>マニフェスト相対パスに内容を登録する。パスが不正なら登録しない。</summary>
    public bool Set(string path, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        string? normalized = Normalize(path);
        if (normalized is null)
        {
            return false;
        }

        _files[normalized] = bytes;
        return true;
    }

    public void Clear() => _files.Clear();

    /// <summary>
    /// パスを <c>dir/name.png</c> 形式に正規化する。ルート脱出・絶対パスは拒否して null を返す。
    /// </summary>
    private static string? Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string normalized = path.Replace('\\', '/').TrimStart('/');
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment == ".."))
        {
            return null;
        }

        return string.Join('/', segments);
    }
}
