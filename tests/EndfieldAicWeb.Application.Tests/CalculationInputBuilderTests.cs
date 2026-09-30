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
}

/// <summary>ECN: 散布機台数入力のパース（docs/phases/test-specification-phase4.md MN-04 の台数検証を純粋層で固定）。</summary>
public class EnvironmentCountParseTests
{
    // ECN-01: 有効な台数は EnvironmentCountOverride になり、空欄行は自動値扱いで無視される。
    [Fact]
    public void ValidCountsBecomeOverrides()
    {
        var inputs = new[]
        {
            new EnvCountInput("env-a", "環境A", "3", 5),
            new EnvCountInput("env-b", "環境B", "", 5),
            new EnvCountInput("env-c", "環境C", "0", 5),
        };

        bool ok = CalculationInputBuilder.TryParseEnvironmentCounts(
            inputs, out List<EnvironmentCountOverride> overrides, out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(
            [new EnvironmentCountOverride("env-a", 3), new EnvironmentCountOverride("env-c", 0)],
            overrides);
    }

    // ECN-02: 上限ちょうどは受理する。
    [Fact]
    public void AcceptsUpperBound()
    {
        bool ok = CalculationInputBuilder.TryParseEnvironmentCounts(
            [new EnvCountInput("env-a", "環境A", "5", 5)],
            out List<EnvironmentCountOverride> overrides,
            out _);

        Assert.True(ok);
        Assert.Equal([new EnvironmentCountOverride("env-a", 5)], overrides);
    }

    // ECN-03: 非整数・負・上限超過はエラー（メッセージに環境名と上限を含む）。
    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("6")]
    [InlineData("1.5")]
    public void InvalidCountsReturnError(string text)
    {
        bool ok = CalculationInputBuilder.TryParseEnvironmentCounts(
            [new EnvCountInput("env-a", "環境A", text, 5)],
            out _,
            out string? error);

        Assert.False(ok);
        Assert.Contains("環境A", error);
        Assert.Contains("5", error);
    }
}

/// <summary>FIL: アイテム検索の選択解除判定（docs/phases/test-specification-phase4.md MN-10）。</summary>
public class ItemDeselectTests
{
    // FIL-05: 選択済み名と本文がずれたら解除、一致なら維持、未選択なら無効。
    [Theory]
    [InlineData("item-a", "鉄鉱石", "鉄", true)]
    [InlineData("item-a", "鉄鉱石", "鉄鉱石", false)]
    [InlineData(null, null, "鉄", false)]
    public void DeselectOnlyWhenNameDiverges(string? itemId, string? itemName, string filter, bool expected) =>
        Assert.Equal(expected, CalculationInputBuilder.ShouldDeselectItem(itemId, itemName, filter));
}
