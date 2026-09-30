using System.Text;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;

namespace EndfieldAicWeb.Infrastructure.Icons;

/// <summary>
/// エクスポート用の Icons マニフェストを組み立てる（旧 AP 由来、仕様決定 R）。
/// エンティティから参照されるキーのみを対象に、実ファイルから Sha256/Bytes を再計算した
/// <see cref="IconEntry"/> を返す。未登録キー・ファイル欠落はエラーとして集約し、
/// どのエンティティからも参照されない孤立エントリは出力に含めない。
/// </summary>
public static class IconExportPlanner
{
    /// <summary>
    /// 参照されている IconKey を文書内の出現順に列挙する（プレースホルダ・無効キーは除外）。
    /// 無効キーは <see cref="MasterValidator"/> 側の違反で報告されるためここでは触れない。
    /// </summary>
    public static IReadOnlyList<string> ReferencedKeys(MasterDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var keys = new List<string>();
        foreach (MasterEntity entity in EnumerateEntities(document))
        {
            string? key = entity.IconKey;
            if (key is not null && IconKeyRules.IsValid(key) && key != IconKeyRules.PlaceholderKey && seen.Add(key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    /// <summary>
    /// 取り込んだ PNG バイト列からマニフェストエントリを作る。File は <c>icons/&lt;Key&gt;.png</c> 固定。
    /// </summary>
    public static IconEntry CreateEntry(string key, byte[] pngBytes)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        return new IconEntry
        {
            Key = key,
            File = IconKeyRules.ManifestFile(key),
            Sha256 = IconFiles.Sha256Hex(pngBytes),
            Bytes = pngBytes.LongLength,
        };
    }

    /// <summary>
    /// <c>icon-&lt;Id&gt;</c> の提案キーを、文書内の他エンティティが使用中なら連番を付けて一意にする。
    /// Id はエンティティ種別をまたぐと一意でなく、置換・切詰めでも衝突し得るため。
    /// </summary>
    public static string UniqueSuggestedKey(MasterEntity entity, MasterDocument document)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(document);

        string baseKey = SuggestKey(entity.Id);
        var used = new HashSet<string>(
            EnumerateEntities(document)
                .Where(e => !ReferenceEquals(e, entity))
                .Select(e => e.IconKey)
                .Where(k => k is not null)!,
            StringComparer.Ordinal);
        string key = baseKey;
        for (int n = 2; used.Contains(key); n++)
        {
            string suffix = $"-{n}";
            key = baseKey.Length + suffix.Length <= 64
                ? baseKey + suffix
                : baseKey[..(64 - suffix.Length)] + suffix;
        }

        return key;
    }

    /// <summary>
    /// <c>icon-&lt;entityId&gt;</c> 形の既定キーを提案する。無効文字は <c>-</c> へ置き、
    /// 64 文字に収まるよう切り詰める（IconKeyRules の文字種制約、旧版の補完規則と同じ）。
    /// </summary>
    public static string SuggestKey(string entityId)
    {
        var builder = new StringBuilder("icon-");
        foreach (char c in entityId ?? "")
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-');
            if (builder.Length >= 64)
            {
                break;
            }
        }

        string key = builder.ToString();
        return IconKeyRules.IsValid(key) ? key : "icon-image";
    }

    /// <summary>
    /// 参照キーに対応するマニフェストを構築する。
    /// 各キーについて「マニフェストに登録がある」「ファイル実体が取得できる」を要求し、
    /// 違反は <paramref name="errors"/> へ集約する。Sha256/Bytes は取得した実ファイルから再計算する。
    /// </summary>
    public static List<IconEntry> BuildManifest(
        MasterDocument document,
        IIconFileProvider files,
        ICollection<MasterValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(errors);

        var manifestByKey = document.Icons
            .Where(e => e is not null)
            .GroupBy(e => e.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var manifest = new List<IconEntry>();
        foreach (string key in ReferencedKeys(document))
        {
            if (!manifestByKey.TryGetValue(key, out IconEntry? entry))
            {
                errors.Add(new MasterValidationError(
                    "Icons", key, "Key",
                    $"IconKey {key} のアイコンがマニフェストに登録されていません。編集画面で画像を取り込んでください。"));
                continue;
            }

            byte[]? content = files.ReadAllBytes(entry.File);
            if (content is null)
            {
                errors.Add(new MasterValidationError(
                    "Icons", key, "File",
                    $"IconKey {key} のアイコンファイルを取得できていません: {entry.File}。取り込み直すか、読み込み元を見直してください。"));
                continue;
            }

            manifest.Add(new IconEntry
            {
                Key = key,
                File = entry.File,
                Sha256 = IconFiles.Sha256Hex(content),
                Bytes = content.LongLength,
            });
        }

        return manifest;
    }

    private static IEnumerable<MasterEntity> EnumerateEntities(MasterDocument document)
    {
        foreach (Item e in document.Items) yield return e;
        foreach (Facility e in document.Facilities) yield return e;
        foreach (Domain.Models.Environment e in document.Environments) yield return e;
        foreach (GameEvent e in document.GameEvents) yield return e;
        foreach (Recipe e in document.Recipes) yield return e;
        foreach (GameMap e in document.Maps) yield return e;
    }
}
