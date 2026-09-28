namespace EndfieldAicWeb.Infrastructure.Icons;

/// <summary>
/// <see cref="IIconFileProvider"/> のファイルシステム実装。
/// ルートディレクトリからの相対パスでファイルを読む。
/// </summary>
public sealed class FileSystemIconProvider : IIconFileProvider
{
    private readonly string _rootDirectory;

    /// <param name="rootDirectory">マニフェスト相対パスの基点となるディレクトリ（<c>icons/</c> の親）。</param>
    public FileSystemIconProvider(string rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(rootDirectory);
        _rootDirectory = rootDirectory;
    }

    public byte[]? ReadAllBytes(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        // マニフェスト相対パスのみを許容し、ルート外へは出さない（先頭以外の ".." も拒否）。
        string normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.Split('/').Any(segment => segment == ".."))
        {
            return null;
        }

        string rootFull = Path.GetFullPath(_rootDirectory);
        string fullPath = Path.GetFullPath(Path.Combine(rootFull, normalized));
        if (!fullPath.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }
        try
        {
            return File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
