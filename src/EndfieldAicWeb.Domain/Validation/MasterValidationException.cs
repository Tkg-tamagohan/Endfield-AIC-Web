namespace EndfieldAicWeb.Domain.Validation;

/// <summary>マスタデータ検証の拒否を表す例外。違反一覧を保持する。</summary>
public class MasterValidationException : Exception
{
    public MasterValidationException(IReadOnlyList<MasterValidationError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    /// <summary>検証違反の一覧。</summary>
    public IReadOnlyList<MasterValidationError> Errors { get; }

    private static string BuildMessage(IReadOnlyList<MasterValidationError> errors)
    {
        if (errors.Count == 0)
        {
            return "マスタデータの検証に失敗しました。";
        }

        return $"マスタデータの検証に失敗しました: {string.Join(" / ", errors.Select(e => e.Message))}";
    }
}
