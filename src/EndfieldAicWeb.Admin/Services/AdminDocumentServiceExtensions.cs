using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace EndfieldAicWeb.Admin.Services;

/// <summary>エンティティ編集ページの「代入＋変更通知」定型ヘルパー。</summary>
public static class AdminDocumentServiceExtensions
{
    /// <summary>数値入力の値を代入して変更を通知する。</summary>
    public static void ApplyNum(this AdminDocumentService store, Action<double> apply, double value)
    {
        apply(value);
        store.NotifyChanged();
    }

    /// <summary>テキスト入力の値（null は空文字）を代入して変更を通知する。</summary>
    public static void ApplyText(this AdminDocumentService store, Action<string> apply, ChangeEventArgs e)
    {
        apply(e.Value?.ToString() ?? "");
        store.NotifyChanged();
    }

    /// <summary>チェックボックスの値を代入して変更を通知する。</summary>
    public static void ApplyBool(this AdminDocumentService store, Action<bool> apply, ChangeEventArgs e)
    {
        apply(e.Value is true);
        store.NotifyChanged();
    }

    /// <summary>参照 Id の値を代入して変更を通知する。</summary>
    public static void ApplyRef(this AdminDocumentService store, Action<string?> apply, string? value)
    {
        apply(value);
        store.NotifyChanged();
    }

    /// <summary>datetime-local の入力を UTC として確定して変更を通知する（マスタ JSON は Z 付き UTC 表記）。空欄は null。</summary>
    public static void ApplyDate(this AdminDocumentService store, Action<DateTime?> apply, ChangeEventArgs e)
    {
        string text = e.Value?.ToString() ?? "";
        apply(
            string.IsNullOrWhiteSpace(text)
                ? null
                : DateTime.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime parsed)
                    ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                    : null);
        store.NotifyChanged();
    }
}
