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
        Assert.Equal("汎用部品（i-part） の数量を 0 より大きい数値で入力してください。", error);
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

/// <summary>ECN: 散布機台数入力のパース（docs/phases/test-specification-phase27.md。下限〜上限の範囲検証は仕様決定 BS）。</summary>
public class EnvironmentCountParseTests
{
    // ECN-01: 範囲内の台数は EnvironmentCountOverride になり、空欄行は自動値扱いで無視される。
    [Fact]
    public void ValidCountsBecomeOverrides()
    {
        var inputs = new[]
        {
            new EnvCountInput("env-a", "環境A", "3", 2, 5),
            new EnvCountInput("env-b", "環境B", "", 2, 5),
            new EnvCountInput("env-c", "環境C", "4", 2, 5),
        };

        bool ok = CalculationInputBuilder.TryParseEnvironmentCounts(
            inputs, out List<EnvironmentCountOverride> overrides, out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(
            [new EnvironmentCountOverride("env-a", 3), new EnvironmentCountOverride("env-c", 4)],
            overrides);
    }

    // ECN-02: 上限ちょうどは受理する。
    [Fact]
    public void AcceptsUpperBound()
    {
        bool ok = CalculationInputBuilder.TryParseEnvironmentCounts(
            [new EnvCountInput("env-a", "環境A", "5", 2, 5)],
            out List<EnvironmentCountOverride> overrides,
            out _);

        Assert.True(ok);
        Assert.Equal([new EnvironmentCountOverride("env-a", 5)], overrides);
    }

    // ECN-03: 非整数・負・上限超過はエラー（メッセージに環境名と下限〜上限の範囲を含む）。
    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("6")]
    [InlineData("1.5")]
    public void InvalidCountsReturnError(string text)
    {
        bool ok = CalculationInputBuilder.TryParseEnvironmentCounts(
            [new EnvCountInput("env-a", "環境A", text, 2, 5)],
            out _,
            out string? error);

        Assert.False(ok);
        Assert.Contains("環境A", error);
        Assert.Contains("2〜5", error);
    }

    // ECN-04: 下限ちょうどは受理する。
    [Fact]
    public void AcceptsLowerBound()
    {
        bool ok = CalculationInputBuilder.TryParseEnvironmentCounts(
            [new EnvCountInput("env-a", "環境A", "2", 2, 5)],
            out List<EnvironmentCountOverride> overrides,
            out _);

        Assert.True(ok);
        Assert.Equal([new EnvironmentCountOverride("env-a", 2)], overrides);
    }

    // ECN-05: 下限未満（0 台を含む）は上限超過と同じメッセージでエラー（仕様決定 BS）。
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public void BelowMinCountsReturnError(string text)
    {
        bool ok = CalculationInputBuilder.TryParseEnvironmentCounts(
            [new EnvCountInput("env-a", "環境A", text, 2, 5)],
            out _,
            out string? error);

        Assert.False(ok);
        Assert.Contains("環境A", error);
        Assert.Contains("2〜5", error);
    }
}

/// <summary>ERC: 保持された散布機台数の入力範囲との整合（仕様決定 AH・BS・docs/remaining-issues.md）。</summary>
public class EnvCountReconcileTests
{
    // ERC-01: 下限〜上限の整数文字列はそのまま保持する（下限ちょうど・上限ちょうどを含む）。
    [Theory]
    [InlineData("3", 2, 5, "3")]
    [InlineData("2", 2, 5, "2")]
    [InlineData("5", 2, 5, "5")]
    public void KeepsTextWithinLimit(string text, int min, int max, string expected) =>
        Assert.Equal(expected, CalculationInputBuilder.ReconcileEnvCountText(text, min, max));

    // ERC-02: 新しい上限を超えた保持値は空欄へ戻す（クランプしない）。
    [Theory]
    [InlineData("6", 2, 5)]
    [InlineData("3", 1, 2)]
    public void RevertsTextAboveLimitToAuto(string text, int min, int max) =>
        Assert.Equal("", CalculationInputBuilder.ReconcileEnvCountText(text, min, max));

    // ERC-03: 非整数・負数・空欄・空白のみは空欄（自動値）のまま/へ戻す。
    [Theory]
    [InlineData("abc", 2, 5)]
    [InlineData("1.5", 2, 5)]
    [InlineData("-1", 2, 5)]
    [InlineData("", 2, 5)]
    [InlineData("  ", 2, 5)]
    [InlineData(null, 2, 5)]
    public void RevertsInvalidTextToAuto(string? text, int min, int max) =>
        Assert.Equal("", CalculationInputBuilder.ReconcileEnvCountText(text, min, max));

    // ERC-04: 下限未満の保持値は空欄へ戻す（AH の範囲読み替え。0 台も範囲外）。
    [Theory]
    [InlineData("1", 2, 5)]
    [InlineData("0", 2, 5)]
    public void RevertsTextBelowMinToAuto(string text, int min, int max) =>
        Assert.Equal("", CalculationInputBuilder.ReconcileEnvCountText(text, min, max));
}

/// <summary>GRI: 採取レート入力行のパース（docs/phases/test-specification-phase11.md §3）。</summary>
public class GatherRateParseTests
{
    private static readonly MasterDataSnapshot Snapshot = ApplicationFixtures.A01();

    // GRI-01: 有効行は GatherRateOverride になり、空欄行は無視される。
    [Fact]
    public void ValidRatesBecomeOverrides()
    {
        var inputs = new[]
        {
            new GatherRateInput("i-ore", "鉄鉱石", "30"),
            new GatherRateInput("i-gas", "活性ガス", ""),
            new GatherRateInput("i-ore", "鉄鉱石", "0"),
        };

        bool ok = CalculationInputBuilder.TryParseGatherRates(
            inputs, Snapshot, out List<GatherRateOverride> overrides, out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(
            [new GatherRateOverride("i-ore", 30), new GatherRateOverride("i-ore", 0)],
            overrides);
    }

    // GRI-02: 非数値・負・非有限はエラー（メッセージにアイテム名を含む）。
    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void InvalidRatesReturnError(string text)
    {
        bool ok = CalculationInputBuilder.TryParseGatherRates(
            [new GatherRateInput("i-ore", "鉄鉱石", text)],
            Snapshot,
            out _,
            out string? error);

        Assert.False(ok);
        Assert.Equal("鉄鉱石（i-ore） の利用可能レートは 0 以上の数値で入力してください。", error);
    }

    // GRI-03: マップ値を超える値も受理する（上限との比較は行わない）。
    [Fact]
    public void RatesAboveMapValueAreAccepted()
    {
        bool ok = CalculationInputBuilder.TryParseGatherRates(
            [new GatherRateInput("i-ore", "鉄鉱石", "9999")],
            Snapshot,
            out List<GatherRateOverride> overrides,
            out _);

        Assert.True(ok);
        Assert.Equal([new GatherRateOverride("i-ore", 9999)], overrides);
    }

    // GRI-04: 存在しない・採取素材でないアイテムはエラー。
    // 実在アイテムはスナップショットの名前で 名前（Id）、存在しないものは Id のみ（CF フォールバック）。
    [Theory]
    [InlineData("i-ghost", "i-ghost は採取素材ではありません。")]
    [InlineData("i-part", "汎用部品（i-part） は採取素材ではありません。")]
    public void UnknownOrNonGatherableItemsReturnError(string itemId, string expected)
    {
        bool ok = CalculationInputBuilder.TryParseGatherRates(
            [new GatherRateInput(itemId, "対象アイテム", "10")],
            Snapshot,
            out _,
            out string? error);

        Assert.False(ok);
        Assert.Equal(expected, error);
    }

    // GRI-05: 全行空欄は空の上書き列で成功。
    [Fact]
    public void AllBlankRowsSucceedWithEmptyOverrides()
    {
        bool ok = CalculationInputBuilder.TryParseGatherRates(
            [new GatherRateInput("i-ore", "鉄鉱石", ""), new GatherRateInput("i-gas", "活性ガス", " ")],
            Snapshot,
            out List<GatherRateOverride> overrides,
            out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Empty(overrides);
    }
}

/// <summary>BAS-16: 基礎素材指定の保持値整合（仕様決定 DA、CZ の母集団判定）。</summary>
public class SpecifiedBaseItemReconcileTests
{
    // i-mid・i-absent は母集団（レシピ成果物かつ非採取・非仮想・非目標）。
    // i-gath は採取素材、i-virt は仮想アイテム、i-noout は成果物なし、i-ghost は未登録、i-target は目標。
    private static readonly MasterDataSnapshot Snapshot = ApplicationFixtures.Snapshot(
        [
            ApplicationFixtures.Item("i-ore", "鉱石", gatherable: true),
            ApplicationFixtures.Item("i-mid", "中間素材"),
            ApplicationFixtures.Item("i-absent", "計画外の素材"),
            ApplicationFixtures.Item("i-gath", "採取化した素材", gatherable: true),
            ApplicationFixtures.Item("i-virt", "仮想素材", TransportKind.None),
            ApplicationFixtures.Item("i-noout", "成果物でない素材"),
            ApplicationFixtures.Item("i-target", "目標素材"),
        ],
        [ApplicationFixtures.Facility("f-a", "機A", 10)],
        [], [],
        [
            ApplicationFixtures.Recipe("r-mid", "中間素材", [("i-ore", 1)], [("i-mid", 1)],
                [ApplicationFixtures.Pair("f-a", 4)]),
            ApplicationFixtures.Recipe("r-absent", "計画外の素材", [("i-ore", 1)], [("i-absent", 1)],
                [ApplicationFixtures.Pair("f-a", 4)]),
            ApplicationFixtures.Recipe("r-gath", "採取化した素材", [("i-ore", 1)], [("i-gath", 1)],
                [ApplicationFixtures.Pair("f-a", 4)]),
            ApplicationFixtures.Recipe("r-virt", "仮想素材", [("i-ore", 1)], [("i-virt", 1)],
                [ApplicationFixtures.Pair("f-a", 4)]),
            ApplicationFixtures.Recipe("r-target", "目標素材", [("i-ore", 1)], [("i-target", 1)],
                [ApplicationFixtures.Pair("f-a", 4)]),
        ]);

    // BAS-16: 母集団外（採取化・削除・成果物消失・仮想）と目標重複の保持値は整合で外れ、
    // 計画に登場しない指定は保持される。
    [Fact]
    public void ReconcileDropsOutOfPopulationAndTargets()
    {
        var specified = new HashSet<string>(StringComparer.Ordinal)
        {
            "i-gath", "i-ghost", "i-noout", "i-virt", "i-target", "i-mid", "i-absent",
        };

        CalculationInputBuilder.ReconcileSpecifiedBaseItems(specified, Snapshot, ["i-target"]);

        Assert.Equal(["i-absent", "i-mid"], specified.Order(StringComparer.Ordinal));
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

/// <summary>CIB: 入力エラー文面の 名前（Id） 表記（docs/phases/test-specification-phase33.md・仕様決定 CF）。</summary>
public class InputErrorFormatTests
{
    private static readonly MasterDataSnapshot Snapshot = ApplicationFixtures.A01();

    // CIB-01: 目標数量の入力エラーは 名前（Id） を含む。
    [Fact]
    public void TargetRateErrorUsesNameWithId()
    {
        bool ok = CalculationInputBuilder.TryParseTargets(
            [new TargetRowInput("i-part", "abc")], Snapshot, out _, out string? error);

        Assert.False(ok);
        Assert.Equal("汎用部品（i-part） の数量を 0 より大きい数値で入力してください。", error);
    }

    // CIB-02: 採取素材でない入力のエラーは 名前（Id） を含む。
    [Fact]
    public void NonGatherableErrorUsesNameWithId()
    {
        bool ok = CalculationInputBuilder.TryParseGatherRates(
            [new GatherRateInput("i-part", "汎用部品", "10")],
            Snapshot,
            out _,
            out string? error);

        Assert.False(ok);
        Assert.Equal("汎用部品（i-part） は採取素材ではありません。", error);
    }
}
