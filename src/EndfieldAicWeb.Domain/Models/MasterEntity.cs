namespace EndfieldAicWeb.Domain.Models;

/// <summary>
/// 全マスタエンティティの共通属性（仕様決定 N）。
/// </summary>
public abstract class MasterEntity
{
    /// <summary>エンティティ種別内で一意の識別子。</summary>
    public required string Id { get; set; }

    /// <summary>表示名。</summary>
    public required string Name { get; set; }

    /// <summary>説明文（空可）。</summary>
    public string Description { get; set; } = "";

    /// <summary>アイコンマニフェストの参照キー。null は未設定（フォールバック表示）。</summary>
    public string? IconKey { get; set; }

    /// <summary>実装されたゲームバージョン（semver 文字列）。</summary>
    public required string VersionAdded { get; set; }

    /// <summary>削除されたゲームバージョン（任意）。</summary>
    public string? VersionRemoved { get; set; }
}
