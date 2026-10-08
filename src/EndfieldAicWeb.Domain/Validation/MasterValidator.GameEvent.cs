using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Domain.Validation;

public static partial class MasterValidator
{
    /// <summary>
    /// 単一 GameEvent のフィールド内規則を検査する。
    /// display は文面のエンティティ参照を `名前（Id）` へ整形する解決器（任意、省略時は Id のみ出力）。
    /// </summary>
    public static void ValidateGameEvent(GameEvent gameEvent, ICollection<MasterValidationError> errors, EntityDisplay? display = null)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);
        ArgumentNullException.ThrowIfNull(errors);

        CheckCommonFields(gameEvent, "GameEvent", errors);

        if (gameEvent.ActiveFrom is { Kind: DateTimeKind.Unspecified }
            || gameEvent.ActiveTo is { Kind: DateTimeKind.Unspecified })
        {
            errors.Add(new MasterValidationError(
                "GameEvent", gameEvent.Id, "ActiveFrom/ActiveTo",
                "ActiveFrom/ActiveTo はタイムゾーン（Z またはオフセット）付きの日時で指定する必要があります（仕様決定 Z）"));
        }
        else if (gameEvent.ActiveFrom is { } from && gameEvent.ActiveTo is { } to
            && from.ToUniversalTime() >= to.ToUniversalTime())
        {
            errors.Add(new MasterValidationError(
                "GameEvent", gameEvent.Id, "ActiveFrom/ActiveTo",
                $"ActiveFrom は ActiveTo より前である必要があります: {gameEvent.ActiveFrom} >= {gameEvent.ActiveTo}"));
        }
    }
}
