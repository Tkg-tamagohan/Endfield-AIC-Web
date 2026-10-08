using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Calculation;

/// <summary>
/// イベントの自動有効化判定。開催期間の前後に猶予日数を設け、
/// 今日がその範囲内なら有効イベントの既定とする（仕様決定 T の UI 既定）。
/// ActiveFrom/ActiveTo は時刻の瞬間として読み、閲覧者のローカル暦日へ換算して比較する（仕様決定 Z）。
/// </summary>
public static class EventAutoActivation
{
    /// <summary>期間の前後に足す猶予日数。</summary>
    public const int MarginDays = 1;

    /// <summary>今日の日付に対してイベントを既定で有効とするか。</summary>
    public static bool IsActiveByDefault(GameEvent gameEvent, DateOnly today)
    {
        // 期間未設定の常設イベントは常に有効扱い。
        if (gameEvent.ActiveFrom is null && gameEvent.ActiveTo is null)
        {
            return true;
        }

        DateOnly from = gameEvent.ActiveFrom is { } f
            ? DateOnly.FromDateTime(f.ToLocalTime()).AddDays(-MarginDays)
            : DateOnly.MinValue;
        DateOnly to = gameEvent.ActiveTo is { } t
            ? DateOnly.FromDateTime(t.ToLocalTime()).AddDays(MarginDays)
            : DateOnly.MaxValue;

        return from <= today && today <= to;
    }
}
