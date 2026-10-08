using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Domain.Validation;

public static partial class MasterValidator
{
    /// <summary>
    /// 単一 Environment のフィールド内規則を検査する（参照整合性は含まない）。
    /// display は文面のエンティティ参照を `名前（Id）` へ整形する解決器（任意、省略時は Id のみ出力）。
    /// </summary>
    public static void ValidateEnvironment(Environment environment, ICollection<MasterValidationError> errors, EntityDisplay? display = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(errors);

        CheckCommonFields(environment, "Environment", errors);
        RequireNonEmpty(environment.ProviderFacilityId, "Environment", environment.Id, "ProviderFacilityId", errors);
        RequireNonEmpty(environment.ConsumeItemId, "Environment", environment.Id, "ConsumeItemId", errors);
        RequireNoWhitespace(environment.ProviderFacilityId, "Environment", environment.Id, "ProviderFacilityId", errors);
        RequireNoWhitespace(environment.ConsumeItemId, "Environment", environment.Id, "ConsumeItemId", errors);

        if (!double.IsFinite(environment.ConsumeRatePerMinute) || environment.ConsumeRatePerMinute <= 0)
        {
            errors.Add(new MasterValidationError(
                "Environment", environment.Id, "ConsumeRatePerMinute",
                $"ConsumeRatePerMinute は 0 より大きい有限値である必要があります: {environment.ConsumeRatePerMinute}"));
        }

        if (environment.CoverableMachines <= 0)
        {
            errors.Add(new MasterValidationError(
                "Environment", environment.Id, "CoverableMachines",
                $"CoverableMachines は 0 より大きい整数である必要があります: {environment.CoverableMachines}"));
        }
    }
}
