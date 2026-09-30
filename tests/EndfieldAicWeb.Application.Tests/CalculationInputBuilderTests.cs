using EndfieldAicWeb.Application;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

/// <summary>TIN: 生産リスト行のパース（docs/requirements.md 目標設定・docs/phases/test-specification-phase4.md MN-09）。</summary>
public class CalculationInputBuilderTests
{
    private static readonly MasterDataSnapshot Snapshot = ApplicationFixtures.A01();

    // TIN-01: 有効行は ProductionTarget になり、全空行は無視される。
    [Fact]
    public void ParsesValidRowsAndSkipsEmptyRows()
    {
        var rows = new List<TargetRowInput>
        {
            new("i-part", "60"),
            new(null, ""),
            new("i-ore", " 30.5 "),
        };

        bool ok = CalculationInputBuilder.TryParseTargets(rows, Snapshot, out List<ProductionTarget> targets, out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(
            [new ProductionTarget("i-part", 60), new ProductionTarget("i-ore", 30.5)],
            targets);
    }

    // TIN-02: アイテム未選択で数量入りの行はエラー（アイテムを選んでください）。
    [Fact]
    public void RejectsRowWithoutItem()
    {
        var rows = new List<TargetRowInput> { new(null, "60") };

        bool ok = CalculationInputBuilder.TryParseTargets(rows, Snapshot, out _, out string? error);

        Assert.False(ok);
        Assert.Equal("アイテムを選んでください。", error);
    }

    // TIN-03: スナップショットに存在しないアイテム Id はエラー。
    [Fact]
    public void RejectsUnknownItem()
    {
        var rows = new List<TargetRowInput> { new("i-missing", "60") };

        bool ok = CalculationInputBuilder.TryParseTargets(rows, Snapshot, out _, out string? error);

        Assert.False(ok);
        Assert.Equal("アイテムを選んでください。", error);
    }

    // TIN-04: 数量が非数値・0・負のいずれかならエラー（メッセージにアイテム名を含む）。
    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("NaN")]
    public void RejectsInvalidRate(string rateText)
    {
        var rows = new List<TargetRowInput> { new("i-part", rateText) };

        bool ok = CalculationInputBuilder.TryParseTargets(rows, Snapshot, out _, out string? error);

        Assert.False(ok);
        Assert.Equal("汎用部品 の数量を 0 より大きい数値で入力してください。", error);
    }

    // TIN-05: 有効行が 0 件（全行空）ならエラー。
    [Fact]
    public void RejectsAllEmptyRows()
    {
        var rows = new List<TargetRowInput> { new(null, " "), new(null, null) };

        bool ok = CalculationInputBuilder.TryParseTargets(rows, Snapshot, out _, out string? error);

        Assert.False(ok);
        Assert.Equal("生産リストにアイテムを追加してください。", error);
    }
}

/// <summary>EVI: イベントチェックの初期化（仕様決定 T の UI 既定・docs/phases/test-specification-phase4.md EVT）。</summary>
public class EventCheckInitializationTests
{
    private static readonly DateOnly Today = new(2026, 9, 28);

    // EVI-01: 期間内のイベントは Checked・IsActiveByDefault が true で初期化される。
    [Fact]
    public void ActiveEventStartsChecked()
    {
        GameEvent active = ApplicationFixtures.Event("ev-on", "開催中",
            new DateTime(2026, 9, 20), new DateTime(2026, 10, 5));

        EventCheck check = Assert.Single(CalculationInputBuilder.InitializeEventChecks([active], Today));

        Assert.True(check.IsActiveByDefault);
        Assert.True(check.Checked);
        Assert.Same(active, check.Event);
    }

    // EVI-02: 期間外のイベントは Checked=false・IsActiveByDefault=false（折りたたみ側）。
    [Fact]
    public void InactiveEventStartsUnchecked()
    {
        GameEvent inactive = ApplicationFixtures.Event("ev-off", "終了済み",
            new DateTime(2026, 9, 1), new DateTime(2026, 9, 10));

        EventCheck check = Assert.Single(CalculationInputBuilder.InitializeEventChecks([inactive], Today));

        Assert.False(check.IsActiveByDefault);
        Assert.False(check.Checked);
    }

    // EVI-03: 複数イベントは入力順のまま、自動有効判定を保持して返る。
    [Fact]
    public void PreservesEventOrder()
    {
        GameEvent first = ApplicationFixtures.Event("ev-a", "常設");
        GameEvent second = ApplicationFixtures.Event("ev-b", "終了済み",
            new DateTime(2026, 9, 1), new DateTime(2026, 9, 10));

        List<EventCheck> checks = CalculationInputBuilder.InitializeEventChecks([first, second], Today);

        Assert.Equal(["ev-a", "ev-b"], checks.Select(c => c.Event.Id));
        Assert.Equal([true, false], checks.Select(c => c.IsActiveByDefault));
    }

    // EVI-04: 既定の再評価は未操作チェックのみ新しい既定へ追従させ、操作済みは保持する。
    [Fact]
    public void RefreshDefaultsRespectsTouched()
    {
        GameEvent ev = ApplicationFixtures.Event("ev", "期間イベント",
            new DateTime(2026, 9, 20), new DateTime(2026, 9, 25));
        DateOnly inPeriod = new(2026, 9, 22);
        DateOnly afterPeriod = new(2026, 9, 30);

        List<EventCheck> checks = CalculationInputBuilder.InitializeEventChecks([ev], inPeriod);
        EventCheck untouched = checks[0];
        var touched = new EventCheck(ev, true) { Checked = true, Touched = true };

        CalculationInputBuilder.RefreshEventCheckDefaults([untouched, touched], afterPeriod);

        Assert.False(untouched.IsActiveByDefault);
        Assert.False(untouched.Checked);
        Assert.False(touched.IsActiveByDefault);
        Assert.True(touched.Checked);
    }
}
