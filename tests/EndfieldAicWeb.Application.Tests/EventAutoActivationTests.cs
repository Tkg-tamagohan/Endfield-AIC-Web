using EndfieldAicWeb.Application;

namespace EndfieldAicWeb.Application.Tests;

/// <summary>EVT: イベント自動有効化（開催期間±1 日）。</summary>
public class EventAutoActivationTests
{
    private static readonly DateOnly Today = new(2026, 9, 28);

    // EVT-01: 期間内のイベントは既定で有効。
    [Fact]
    public void ActiveDuringPeriod()
    {
        var gameEvent = ApplicationFixtures.Event("ev", "期間イベント",
            new DateTime(2026, 9, 20), new DateTime(2026, 10, 5));

        Assert.True(EventAutoActivation.IsActiveByDefault(gameEvent, Today));
    }

    // EVT-02: 期間±1 日の猶予の内側は有効、外側は無効。
    [Fact]
    public void MarginOneDayAroundPeriod()
    {
        var ended = ApplicationFixtures.Event("ev", "終了直後",
            new DateTime(2026, 9, 20), new DateTime(2026, 9, 27));
        var upcoming = ApplicationFixtures.Event("ev", "開催直前",
            new DateTime(2026, 9, 29), new DateTime(2026, 10, 5));
        var endedTooLong = ApplicationFixtures.Event("ev", "終了済み",
            new DateTime(2026, 9, 20), new DateTime(2026, 9, 26));
        var upcomingTooFar = ApplicationFixtures.Event("ev", "未開催",
            new DateTime(2026, 9, 30), new DateTime(2026, 10, 5));

        Assert.True(EventAutoActivation.IsActiveByDefault(ended, Today));
        Assert.True(EventAutoActivation.IsActiveByDefault(upcoming, Today));
        Assert.False(EventAutoActivation.IsActiveByDefault(endedTooLong, Today));
        Assert.False(EventAutoActivation.IsActiveByDefault(upcomingTooFar, Today));
    }

    // EVT-03: ActiveFrom/ActiveTo 未設定（常設）は常に有効。
    [Fact]
    public void PermanentEventAlwaysActive()
    {
        var gameEvent = ApplicationFixtures.Event("ev", "常設イベント");

        Assert.True(EventAutoActivation.IsActiveByDefault(gameEvent, Today));
        Assert.True(EventAutoActivation.IsActiveByDefault(gameEvent, new DateOnly(2020, 1, 1)));
    }

    // EVT-04: 片端のみ設定のイベント（開始日のみ・終了日のみ）。
    [Fact]
    public void OpenEndedPeriods()
    {
        var startedLongAgo = ApplicationFixtures.Event("ev", "終了日なし",
            new DateTime(2020, 1, 1), null);
        var notStarted = ApplicationFixtures.Event("ev", "開始日のみ未来",
            new DateTime(2026, 10, 10), null);
        var endsToday = ApplicationFixtures.Event("ev", "終了日のみ",
            null, new DateTime(2026, 9, 28));

        Assert.True(EventAutoActivation.IsActiveByDefault(startedLongAgo, Today));
        Assert.False(EventAutoActivation.IsActiveByDefault(notStarted, Today));
        Assert.True(EventAutoActivation.IsActiveByDefault(endsToday, Today));
    }
}
