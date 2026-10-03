using EndfieldAicWeb.Application;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application.Tests;

/// <summary>CPH: CalculatorPanel から抽出した期間入力パース（仕様決定 M）。</summary>
public class PeriodParseTests
{
    // CPH-01: 日・時・分の各欄を合算して PeriodAmount にする（1 日 2 時 30 分 = 1590 分）。
    [Fact]
    public void ParsesAllParts()
    {
        bool ok = CalculationInputBuilder.TryParsePeriod("1", "2", "30", out PeriodAmount period);

        Assert.True(ok);
        Assert.Equal(1590, period.TotalMinutes);
    }

    // CPH-02: 空欄・空白のみ・null の欄は 0 扱い。
    [Theory]
    [InlineData("", "0", "0", 0)]
    [InlineData(" ", null, "15", 15)]
    [InlineData(null, null, null, 0)]
    public void BlankPartsCountAsZero(string? days, string? hours, string? minutes, double expected)
    {
        bool ok = CalculationInputBuilder.TryParsePeriod(days, hours, minutes, out PeriodAmount period);

        Assert.True(ok);
        Assert.Equal(expected, period.TotalMinutes);
    }

    // CPH-03: 非数値・負・非有限の欄があれば拒否し、既定値（全 0）を返す。
    [Theory]
    [InlineData("abc", "0", "0")]
    [InlineData("0", "-1", "0")]
    [InlineData("0", "0", "1e999")]
    public void RejectsInvalidParts(string? days, string? hours, string? minutes)
    {
        bool ok = CalculationInputBuilder.TryParsePeriod(days, hours, minutes, out PeriodAmount period);

        Assert.False(ok);
        Assert.Equal(default, period);
    }
}

/// <summary>CPH: 採取レート保持値と計画の整合（散布機台数の仕様決定 AH と同型の保持規則）。</summary>
public class GatherRateReconcileTests
{
    private static readonly MasterDataSnapshot Snapshot = ApplicationFixtures.A06();

    // CPH-04: 計画の採取対象順で行を組み、保持している入力値を適用する。保持値のない対象は空欄。
    [Fact]
    public void BuildsRowsInPlanOrderWithKeptTexts()
    {
        var kept = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["i-ore"] = "45",
        };

        List<GatherRateState> rows = CalculationInputBuilder.ReconcileGatherRateRows(
            kept, ["i-ore", "i-gas"], Snapshot);

        Assert.Equal(["i-ore", "i-gas"], rows.Select(row => row.ItemId));
        Assert.Equal(["45", ""], rows.Select(row => row.RateText));
    }

    // CPH-05: 採取対象外になった保持値のうち、採取素材でない・未知のアイテム分は破棄する。
    [Fact]
    public void PrunesKeptTextsForNonGatherableItems()
    {
        var kept = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["i-part"] = "10",
            ["i-ghost"] = "5",
            ["i-ore"] = "30",
        };

        List<GatherRateState> rows = CalculationInputBuilder.ReconcileGatherRateRows(kept, [], Snapshot);

        Assert.Empty(rows);
        Assert.Equal(["i-ore"], kept.Keys);
    }

    // CPH-06: 採取対象外で受理されない値は破棄し、採取対象内の値はそのまま残す
    // （対象内の値の検証は計算入力の TryParseGatherRates が担う）。
    [Fact]
    public void KeepsListedTextsAndPrunesInvalidUnlisted()
    {
        var kept = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["i-ore"] = "abc",
            ["i-gas"] = "-5",
        };

        List<GatherRateState> rows = CalculationInputBuilder.ReconcileGatherRateRows(
            kept, ["i-ore"], Snapshot);

        Assert.Equal("abc", Assert.Single(rows).RateText);
        Assert.Equal("abc", kept["i-ore"]);
        Assert.False(kept.ContainsKey("i-gas"));
    }
}

/// <summary>CPH: 採取上限の有効値と「上限到達」判定（仕様決定 AC・AD・AE）。</summary>
public class GatherCapTests
{
    private static readonly MasterDataSnapshot Snapshot = ApplicationFixtures.A06();
    private static readonly ContextFilter CapContext = new() { MapId = "m-cap" };

    private static CalculationOutcome Calculate() =>
        new CalculationService().Calculate(
            Snapshot, [new ProductionTarget("i-part", 60)], CapContext, [], [], []);

    // CPH-07: 空欄はマップ既定値、0 以上の有限値は上書き（マップ値超過も許容）。
    [Theory]
    [InlineData("", 60)]
    [InlineData(null, 60)]
    [InlineData("90", 90)]
    [InlineData("0", 0)]
    public void EffectiveCapFollowsInputOrMapDefault(string? text, double expected) =>
        Assert.Equal(expected, MapSelection.EffectiveGatherCap(Snapshot, CapContext, "i-ore", text));

    // CPH-08: 受理されない入力（非数値・負・非有限）はマップ既定値へ戻す。
    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("Infinity")]
    public void InvalidCapInputFallsBackToMapDefault(string text) =>
        Assert.Equal(60, MapSelection.EffectiveGatherCap(Snapshot, CapContext, "i-ore", text));

    // CPH-09: マップの無限行とマップ未選択は上限なし（null）。
    [Fact]
    public void UnlimitedCapsAreNull()
    {
        Assert.Null(MapSelection.EffectiveGatherCap(Snapshot, CapContext, "i-gas", ""));
        Assert.Null(MapSelection.EffectiveGatherCap(Snapshot, new ContextFilter(), "i-ore", ""));
    }

    // CPH-10: 採取充当と要求レートは計画から読み出す。登場しないアイテムは 0。
    [Fact]
    public void ReadsGatheredAndRequiredRates()
    {
        ProductionPlan plan = Calculate().Plan;

        Assert.Equal(120, MapSelection.RequiredRate(plan, "i-ore"));
        Assert.Equal(60, MapSelection.GatheredRate(plan, "i-ore"));
        Assert.Equal(0, MapSelection.GatheredRate(plan, "i-part"));
        Assert.Equal(0, MapSelection.RequiredRate(plan, "i-ghost"));
    }

    // CPH-11: 採取充当が有効上限へ達し需要が残るとき「上限到達」。
    [Fact]
    public void DetectsCapReached()
    {
        ProductionPlan plan = Calculate().Plan;

        Assert.True(MapSelection.IsGatherCapReached(plan, Snapshot, CapContext, "i-ore", ""));
        Assert.True(MapSelection.IsGatherCapReached(plan, Snapshot, CapContext, "i-ore", "30"));
    }

    // CPH-12: 上限未満の充当・上限なし（無限行）は到達しない。
    [Fact]
    public void CapNotReached()
    {
        ProductionPlan plan = Calculate().Plan;

        Assert.False(MapSelection.IsGatherCapReached(plan, Snapshot, CapContext, "i-ore", "200"));
        Assert.False(MapSelection.IsGatherCapReached(plan, Snapshot, CapContext, "i-gas", ""));
    }
}

/// <summary>CPH: 表示単位の整形（仕様決定 M）。</summary>
public class ResultFormatTests
{
    private static readonly PeriodAmount TwoHours = CreatePeriod(0, 2, 0);

    private static PeriodAmount CreatePeriod(double days, double hours, double minutes)
    {
        Assert.True(PeriodAmount.TryCreate(days, hours, minutes, out PeriodAmount period));
        return period;
    }

    // CPH-13: 毎分はそのまま 2 桁、毎秒は ÷60 の 3 桁、期間は ×合計分数の 2 桁で整形する。
    [Fact]
    public void FormatsByUnit()
    {
        Assert.Equal("120", ResultViewText.Format(120, AmountUnit.PerMinute, TwoHours));
        Assert.Equal("2", ResultViewText.Format(120, AmountUnit.PerSecond, TwoHours));
        Assert.Equal("0.025", ResultViewText.Format(1.5, AmountUnit.PerSecond, TwoHours));
        Assert.Equal("14400", ResultViewText.Format(120, AmountUnit.Period, TwoHours));
    }

    // CPH-14: 個/分固定の整形（採取レートなど単位切替を適用しない表示）。
    [Fact]
    public void FormatsPerMinute() =>
        Assert.Equal("12.5", ResultViewText.FormatPerMinute(12.5));

    // CPH-15: 個/期間の併記は期間表示かつ有効な期間入力があるときだけ付く。
    [Fact]
    public void PeriodConsumptionOnlyInPeriodUnit()
    {
        Assert.Equal("（7200 個/期間）", ResultViewText.PeriodConsumption(60, AmountUnit.Period, TwoHours));
        Assert.Equal("", ResultViewText.PeriodConsumption(60, AmountUnit.PerMinute, TwoHours));
        Assert.Equal("", ResultViewText.PeriodConsumption(60, AmountUnit.Period, null));
    }
}

/// <summary>CPH: 供給内訳・ペア候補・イベント期間・採取ラベルの表示文。</summary>
public class ResultTextTests
{
    private static readonly MasterDataSnapshot Snapshot = ApplicationFixtures.A01();
    private static readonly MasterDataSnapshot MapSnapshot = ApplicationFixtures.A06();
    private static readonly ContextFilter CapContext = new() { MapId = "m-cap" };

    // CPH-16: 供給内訳は種別ごとの文になる。レシピ名はスナップショットから引く。
    [Theory]
    [InlineData(SupplyKind.Recipe, "r-part", "レシピ 汎用部品 60/分")]
    [InlineData(SupplyKind.Byproduct, "r-part", "副産物 汎用部品 60/分")]
    [InlineData(SupplyKind.Gathered, null, "採取 60/分")]
    public void SupplyTexts(SupplyKind kind, string? recipeId, string expected)
    {
        var portion = new SupplyPortion(kind, recipeId, 60);

        Assert.Equal(expected, ResultViewText.SupplyText(Snapshot, portion, AmountUnit.PerMinute, default));
    }

    // CPH-17: 供給内訳の流量は表示単位と接尾辞に従う。
    [Fact]
    public void SupplyTextFollowsUnit()
    {
        var portion = new SupplyPortion(SupplyKind.Recipe, "r-part", 60);

        Assert.Equal(
            "レシピ 汎用部品 1/秒",
            ResultViewText.SupplyText(Snapshot, portion, AmountUnit.PerSecond, default));
    }

    // CPH-18: ペア候補ラベルは「レシピ ／ 設備 秒・環境・固定消費・（既定）」を並べる。
    [Fact]
    public void OptionLabelParts()
    {
        var withEnv = new PairOption("k1", "r-part", "f-asm", 3, "env-gas", null, true);
        Assert.Equal("汎用部品 ／ 加工機 3秒・ガス散布（既定）", ResultViewText.OptionLabel(Snapshot, withEnv));

        var plain = new PairOption("k2", "r-part", "f-asm", 4, null, null, false);
        Assert.Equal("汎用部品 ／ 加工機 4秒・環境なし", ResultViewText.OptionLabel(Snapshot, plain));

        var withFixed = new PairOption(
            "k3", "r-part", "f-asm", 4, null,
            new FixedConsumption { ItemId = "i-ore", RatePerMinute = 12 }, false);
        Assert.Equal("汎用部品 ／ 加工機 4秒・環境なし・鉄鉱石 12/分", ResultViewText.OptionLabel(Snapshot, withFixed));
    }

    // CPH-19: 期間イベントは開催期間を併記し、常設は空。未登録名は Id に倒れる。
    [Fact]
    public void EventPeriodText()
    {
        Assert.Equal("", ResultViewText.EventPeriod(ApplicationFixtures.Event("ev-std", "常設")));

        GameEvent limited = ApplicationFixtures.Event(
            "ev-ltd", "限定", new DateTime(2026, 10, 1), new DateTime(2026, 10, 15));
        string from = limited.ActiveFrom!.Value.ToLocalTime().ToString("MM/dd");
        string to = limited.ActiveTo!.Value.ToLocalTime().ToString("MM/dd");
        Assert.Equal($"（{from}〜{to}）", ResultViewText.EventPeriod(limited));
    }

    // CPH-20: 採取レート欄のプレースホルダと既定値ラベル。上限なしは「無制限」。
    [Fact]
    public void GatherRateLabels()
    {
        Assert.Equal("60", ResultViewText.GatherRatePlaceholder(MapSnapshot, CapContext, "i-ore"));
        Assert.Equal("マップ既定 60 個/分", ResultViewText.GatherRateDefaultLabel(MapSnapshot, CapContext, "i-ore"));
        Assert.Equal("無制限", ResultViewText.GatherRatePlaceholder(MapSnapshot, CapContext, "i-gas"));
        Assert.Equal("マップ既定 無制限", ResultViewText.GatherRateDefaultLabel(MapSnapshot, CapContext, "i-gas"));
    }

    // CPH-21: スナップショット未構築でもラベルは「無制限」系へ倒れる。
    [Fact]
    public void GatherLabelsWithoutSnapshot()
    {
        Assert.Equal("無制限", ResultViewText.GatherRatePlaceholder(null, CapContext, "i-ore"));
        Assert.Equal("マップ既定 無制限", ResultViewText.GatherRateDefaultLabel(null, CapContext, "i-ore"));
    }
}

/// <summary>CPH: スナップショットの表示名・アイコン検索。</summary>
public class SnapshotLookupTests
{
    private static readonly MasterDataSnapshot Snapshot = ApplicationFixtures.A05();

    // CPH-22: 登録済み Id は表示名、未登録とスナップショットなしは Id フォールバック。
    [Fact]
    public void NamesFallBackToId()
    {
        Assert.Equal("上流素材", Snapshot.ItemName("i-u"));
        Assert.Equal("加工機", Snapshot.FacilityName("f-asm"));
        Assert.Equal("主産物", Snapshot.RecipeName("r-side"));
        Assert.Equal("i-ghost", Snapshot.ItemName("i-ghost"));
        Assert.Equal("f-ghost", Snapshot.FacilityName("f-ghost"));
        Assert.Equal("r-ghost", Snapshot.RecipeName("r-ghost"));
        Assert.Equal("i-u", SnapshotLookup.ItemName(null, "i-u"));

        MasterDataSnapshot withEnv = ApplicationFixtures.A01();
        Assert.Equal("ガス散布", withEnv.EnvName("env-gas"));
        Assert.Equal("env-ghost", withEnv.EnvName("env-ghost"));
    }

    // CPH-23: アイコンキーは設定値を返し、未設定・未登録・スナップショットなしは null。
    [Fact]
    public void IconKeys()
    {
        var iconItem = new Item
        {
            Id = "i-icon",
            Name = "アイコン付き",
            Category = "",
            TransportKind = TransportKind.Belt,
            VersionAdded = "1.0.0",
            IconKey = "icon-item-icon",
        };
        MasterDataSnapshot snapshot = ApplicationFixtures.Snapshot([iconItem], [], [], [], []);

        Assert.Equal("icon-item-icon", snapshot.ItemIconKey("i-icon"));
        Assert.Null(snapshot.ItemIconKey("i-ghost"));
        Assert.Null(snapshot.FacilityIconKey("f-ghost"));
        Assert.Null(snapshot.EnvIconKey("env-ghost"));
        Assert.Null(SnapshotLookup.ItemIconKey(null, "i-icon"));
    }

    // CPH-24: イベント所属アイテムの判定。未登録・スナップショットなしは false。
    [Fact]
    public void DetectsEventItems()
    {
        Assert.True(Snapshot.IsEventItem("i-ltd"));
        Assert.False(Snapshot.IsEventItem("i-u"));
        Assert.False(Snapshot.IsEventItem("i-ghost"));
        Assert.False(SnapshotLookup.IsEventItem(null, "i-ltd"));
    }
}

/// <summary>CPH: 入力行の UI 状態クラスの既定値。</summary>
public class InputRowStateTests
{
    // CPH-25: 生産リスト行の新規レート既定値は 30（仕様決定 AZ）。他の入力行は空欄開始。
    [Fact]
    public void StateDefaults()
    {
        Assert.Equal("30", new TargetRowState().RateText);
        Assert.Equal("", new GatherRateState("i-ore").RateText);

        var env = ApplicationFixtures.Env("env-a", "環境A", "f-disp", "i-gas", 60);
        Assert.Equal("", new EnvCountState(env, 3).CountText);
    }
}
