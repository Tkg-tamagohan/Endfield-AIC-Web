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

        // マニフェスト相対パスのみを許容し、ルート外へは出さない。
        string normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("../", StringComparison.Ordinal) || normalized == "..")
        {
            return null;
        }

        string fullPath = Path.Combine(_rootDirectory, normalized);
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
