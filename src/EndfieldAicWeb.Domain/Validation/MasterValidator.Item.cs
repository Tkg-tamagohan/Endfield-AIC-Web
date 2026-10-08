using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Domain.Validation;

public static partial class MasterValidator
{
    /// <summary>
    /// 単一 Item のフィールド内規則を検査する（参照整合性は含まない）。
    /// display は文面のエンティティ参照を `名前（Id）` へ整形する解決器（任意、省略時は Id のみ出力）。
    /// </summary>
    public static void ValidateItem(Item item, ICollection<MasterValidationError> errors, EntityDisplay? display = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(errors);

        CheckCommonFields(item, "Item", errors);
        RequireNonEmpty(item.Category, "Item", item.Id, "Category", errors);
        RequireEnum(item.TransportKind, "Item", item.Id, "TransportKind", errors);
    }
}
