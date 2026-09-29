using System.IO.Compression;
using System.Text;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Infrastructure.Icons;

namespace EndfieldAicWeb.Infrastructure.Transfer;

/// <summary>
/// エクスポート物 <c>master-export.zip</c> の生成と読み取り。
/// 内部構造は <c>data/master.json</c>＋<c>data/icons/&lt;Key&gt;.png</c> で、
/// リポジトリルートでの展開がそのまま data/ への反映になる。
/// </summary>
public static class IconArchive
{
    /// <summary>zip 内の正本 JSON パス。</summary>
    public const string JsonEntryName = "data/master.json";

    /// <summary>zip 内でアイコンを収めるディレクトリ。</summary>
    public const string IconsPrefix = "data/icons/";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// 正本 JSON とアイコンファイル群を zip へ詰める。
    /// マニフェスト記載のうち実体が取得できないファイルはエントリを作らない
    /// （その状態は <see cref="IconExportPlanner"/> 側で先に拒否される想定）。
    /// </summary>
    public static byte[] CreateZip(
        string masterJson,
        IReadOnlyList<IconEntry> manifest,
        IIconFileProvider files)
    {
        ArgumentNullException.ThrowIfNull(masterJson);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(files);

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry jsonEntry = archive.CreateEntry(JsonEntryName);
            using (var writer = new StreamWriter(jsonEntry.Open(), Utf8NoBom))
            {
                writer.Write(masterJson);
            }

            foreach (IconEntry entry in manifest)
            {
                byte[]? content = files.ReadAllBytes(entry.File);
                if (content is null)
                {
                    continue;
                }

                ZipArchiveEntry iconEntry = archive.CreateEntry(IconsPrefix + Path.GetFileName(entry.File));
                using Stream entryStream = iconEntry.Open();
                entryStream.Write(content, 0, content.Length);
            }
        }

        return stream.ToArray();
    }

    /// <summary>
    /// zip から正本 JSON とアイコンファイル群を取り出す。
    /// エントリ名は <c>data/…</c> 前置きあり・なしの両形を受け付ける
    /// （icons 側のキーはマニフェスト相対パス <c>icons/&lt;name&gt;.png</c> に正規化して返す）。
    /// </summary>
    public static bool TryReadZip(
        byte[] zipBytes,
        out string? json,
        out IReadOnlyDictionary<string, byte[]> icons)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);
        json = null;
        icons = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        try
        {
            using var stream = new MemoryStream(zipBytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string name = entry.FullName.Replace('\\', '/').TrimStart('/');
                if (name.EndsWith('/'))
                {
                    continue;
                }

                if (name is JsonEntryName or "master.json" && json is null)
                {
                    using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                    json = reader.ReadToEnd();
                }
                else if (name.StartsWith(IconsPrefix, StringComparison.Ordinal)
                    || name.StartsWith("icons/", StringComparison.Ordinal))
                {
                    string relative = name.StartsWith(IconsPrefix, StringComparison.Ordinal)
                        ? name[5..]
                        : name;
                    if (!relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using Stream entryStream = entry.Open();
                    using var buffer = new MemoryStream();
                    entryStream.CopyTo(buffer);
                    files[relative] = buffer.ToArray();
                }
            }

            icons = files;
            return json is not null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
