namespace EndfieldAicWeb.Application;

/// <summary>
/// 表示単位（仕様決定 M）。内部保持・計算はすべて 個/分。
/// 消費電力と推奨流量制限は換算対象外。
/// </summary>
public enum AmountUnit
{
    PerMinute,
    PerSecond,
    Period,
}

/// <summary>期間入力（日・時・分）。各成分は 0 以上の有限値。</summary>
public readonly record struct PeriodAmount
{
    public double Days { get; init; }
    public double Hours { get; init; }
    public double Minutes { get; init; }

    public double TotalMinutes => Days * 24 * 60 + Hours * 60 + Minutes;

    public static bool TryCreate(double days, double hours, double minutes, out PeriodAmount period)
    {
        bool valid = double.IsFinite(days) && days >= 0
            && double.IsFinite(hours) && hours >= 0
            && double.IsFinite(minutes) && minutes >= 0;
        period = valid ? new PeriodAmount { Days = days, Hours = hours, Minutes = minutes } : default;
        return valid;
    }
}

public static class AmountConverter
{
    /// <summary>個/分の値を表示単位へ換算する。</summary>
    public static double Convert(double perMinute, AmountUnit unit, PeriodAmount period) => unit switch
    {
        AmountUnit.PerMinute => perMinute,
        AmountUnit.PerSecond => perMinute / 60.0,
        AmountUnit.Period => perMinute * period.TotalMinutes,
        _ => perMinute,
    };

    /// <summary>表示単位の接尾辞。</summary>
    public static string Suffix(AmountUnit unit) => unit switch
    {
        AmountUnit.PerMinute => "/分",
        AmountUnit.PerSecond => "/秒",
        AmountUnit.Period => "/期間",
        _ => "",
    };
}
