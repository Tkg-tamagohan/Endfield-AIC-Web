using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;

namespace EndfieldAicWeb.Infrastructure.Transfer;

/// <summary>マスタ JSON 読み込みの結果。違反は例外ではなく <see cref="Errors"/> に集約される。</summary>
public sealed class MasterJsonLoadResult
{
    /// <summary>
    /// 実体化したマスタ文書。構文・構造検証を通過した場合にのみ非 null。
    /// 意味検証の違反が残る場合も実体化結果を返すため、利用側は <see cref="Success"/> を確認してから使うこと。
    /// </summary>
    public required MasterDocument? Document { get; init; }

    /// <summary>構文・構造・意味の三段で見つかった違反の一覧。</summary>
    public required IReadOnlyList<MasterValidationError> Errors { get; init; }

    /// <summary>違反がなく正として読めたか。</summary>
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// マスタ JSON の読み込み（マスタ JSON → <see cref="MasterDocument"/>）。
/// 構文解析 → 構造検証 → 意味検証（<see cref="MasterValidator"/> と同一規則）の三段で処理し、
/// 違反はすべてエラー一覧として集約して返す。
/// </summary>
public static class MasterJsonLoader
{
    /// <summary>対応するスキーマ版。</summary>
    public const int SupportedSchemaVersion = MasterJsonReader.SupportedSchemaVersion;

    /// <summary>JSON 文字列を読み込み、検証結果と実体化文書を返す。</summary>
    public static MasterJsonLoadResult Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (string.IsNullOrWhiteSpace(json))
        {
            return new MasterJsonLoadResult
            {
                Document = null,
                Errors =
                [
                    new MasterValidationError("Document", "", "", "JSON が空です。"),
                ],
            };
        }

        MasterJsonDocument? document = MasterJsonReader.Parse(json, out string? syntaxError);
        if (document is null)
        {
            return new MasterJsonLoadResult
            {
                Document = null,
                Errors =
                [
                    new MasterValidationError("Document", "", "", syntaxError ?? "JSON の構文が不正です。"),
                ],
            };
        }

        var structureErrors = new List<MasterValidationError>();
        MasterJsonReader.ValidateStructure(document, structureErrors);
        if (structureErrors.Count > 0)
        {
            return new MasterJsonLoadResult
            {
                Document = null,
                Errors = structureErrors,
            };
        }

        var validationErrors = new List<MasterValidationError>();
        MasterDocument entities = MasterJsonReader.ToEntities(document, validationErrors);
        return new MasterJsonLoadResult
        {
            Document = entities,
            Errors = validationErrors,
        };
    }
}
