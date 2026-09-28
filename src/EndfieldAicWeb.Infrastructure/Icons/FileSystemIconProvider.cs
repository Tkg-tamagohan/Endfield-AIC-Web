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

        // マニフェスト相対パスのみを許容する。先頭以外の ".." も拒否。
        string normalized = path.Replace('\\', '/').TrimStart('/');
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment == ".."))
        {
            return null;
        }

        string rootFull = Path.GetFullPath(_rootDirectory);
        // 末尾区切りを正規化したルート接頭辞。ルート "/" 自体は "/" のまま全絶対パスを包含する。
        string rootPrefix = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        try
        {
            // 各階層のシンボリックリンクを解決しつつ、常にルート配下に留まることを確認する。
            string current = rootFull;
            foreach (string segment in segments[..^1])
            {
                var directory = new DirectoryInfo(Path.Combine(current, segment));
                if (!directory.Exists)
                {
                    return null;
                }

                FileSystemInfo? target = directory.ResolveLinkTarget(returnFinalTarget: true);
                current = target?.FullName ?? directory.FullName;
                if (!current.StartsWith(rootPrefix, StringComparison.Ordinal))
                {
                    return null;
                }
            }

            var file = new FileInfo(Path.Combine(current, segments[^1]));
            if (!file.Exists)
            {
                return null;
            }

            FileSystemInfo? fileTarget = file.ResolveLinkTarget(returnFinalTarget: true);
            string finalPath = fileTarget?.FullName ?? file.FullName;
            if (!finalPath.StartsWith(rootPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            return File.ReadAllBytes(finalPath);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
