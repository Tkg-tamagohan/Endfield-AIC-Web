using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Domain.Validation;

public static partial class MasterValidator
{
    /// <summary>
    /// 単一 Facility のフィールド内規則を検査する（参照整合性は含まない）。
    /// display は文面のエンティティ参照を `名前（Id）` へ整形する解決器（任意、省略時は Id のみ出力）。
    /// </summary>
    public static void ValidateFacility(Facility facility, ICollection<MasterValidationError> errors, EntityDisplay? display = null)
    {
        ArgumentNullException.ThrowIfNull(facility);
        ArgumentNullException.ThrowIfNull(errors);

        CheckCommonFields(facility, "Facility", errors);

        if (!double.IsFinite(facility.Width) || facility.Width <= 0)
        {
            errors.Add(new MasterValidationError(
                "Facility", facility.Id, "Width",
                $"Width は 0 より大きい有限値である必要があります: {facility.Width}"));
        }

        if (!double.IsFinite(facility.Height) || facility.Height <= 0)
        {
            errors.Add(new MasterValidationError(
                "Facility", facility.Id, "Height",
                $"Height は 0 より大きい有限値である必要があります: {facility.Height}"));
        }

        if (!double.IsFinite(facility.PowerConsumption) || facility.PowerConsumption < 0)
        {
            errors.Add(new MasterValidationError(
                "Facility", facility.Id, "PowerConsumption",
                $"PowerConsumption は 0 以上の有限値である必要があります: {facility.PowerConsumption}"));
        }
    }
}
