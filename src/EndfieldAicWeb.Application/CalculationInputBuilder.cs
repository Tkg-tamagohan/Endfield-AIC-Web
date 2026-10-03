using System.Globalization;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;
using DomainEnv = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Application;

/// <summary>生産リスト 1 行の入力値（選択アイテム Id と個/分の文字列）。</summary>
public sealed record TargetRowInput(string? ItemId, string? RateText);

/// <summary>生産リスト 1 行の入力状態（カテゴリ絞り込み・選択アイテム Id・個/分の文字列）。</summary>
public sealed class TargetRowState
{
    public string? ItemId;
    public string RateText = "30";
    public string? Category;
}

/// <summary>散布機台数 1 行の入力値（環境 Id・表示名・台数文字列・入力範囲の下限と上限）。</summary>
public sealed record EnvCountInput(string EnvId, string EnvName, string? CountText, int Min, int Max);

/// <summary>散布機台数 1 行の入力状態（対象環境・入力範囲・台数の文字列）。</summary>
public sealed class EnvCountState(DomainEnv environment, int min, int max)
{
    public DomainEnv Env { get; } = environment;
    public int Min { get; set; } = min;
    public int Max { get; set; } = max;
    public string CountText = "";
}

/// <summary>採取素材の利用可能レート 1 行の入力値（アイテム Id・表示名・レート文字列。空欄はマップ既定値）。</summary>
public sealed record GatherRateInput(string ItemId, string ItemName, string? RateText);

/// <summary>採取レート 1 行の入力状態（対象アイテム Id とレート文字列。空欄はマップ既定値）。</summary>
public sealed class GatherRateState(string itemId)
{
    public string ItemId { get; } = itemId;
    public string RateText = "";
}

/// <summary>
/// イベントのチェック状態。Checked は UI が書き換え、
/// IsActiveByDefault は期間外イベントを折りたたみへ振り分ける判定に使う。
/// </summary>
public sealed class EventCheck(GameEvent gameEvent, bool isActiveByDefault)
{
    public GameEvent Event { get; } = gameEvent;
    public bool IsActiveByDefault { get; internal set; } = isActiveByDefault;
    public bool Checked { get; set; } = isActiveByDefault;

    /// <summary>ユーザーがチェックを操作したか。未操作分だけが既定の再評価へ追従する。</summary>
    public bool Touched { get; set; }
}

/// <summary>
/// 計算入力の組み立て。生産リスト行から ProductionTarget への変換と
/// イベントチェックの初期化を担う純粋関数群（UI 非依存）。
/// </summary>
public static class CalculationInputBuilder
{
    /// <summary>
    /// 入力行を ProductionTarget の列に変換する。
    /// アイテム未選択かつ数量空の行は無視する。アイテム未選択・未知アイテム・
    /// 数量が非数値または 0 以下の行があればエラーを返す。有効な行が 0 件ならエラー。
    /// </summary>
    public static bool TryParseTargets(
        IEnumerable<TargetRowInput> rows,
        MasterDataSnapshot snapshot,
        out List<ProductionTarget> targets,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(snapshot);

        targets = [];
        error = null;
        foreach (TargetRowInput row in rows)
        {
            if (row.ItemId is null && string.IsNullOrWhiteSpace(row.RateText))
            {
                continue;
            }

            if (row.ItemId is null || !snapshot.ItemsById.TryGetValue(row.ItemId, out Item? item))
            {
                error = "アイテムを選んでください。";
                return false;
            }

            if (!double.TryParse(row.RateText, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate)
                || !double.IsFinite(rate) || rate <= 0)
            {
                error = $"{EntityDisplay.Format(item.Name, item.Id)} の数量を 0 より大きい数値で入力してください。";
                return false;
            }

            targets.Add(new ProductionTarget(row.ItemId, rate));
        }

        if (targets.Count == 0)
        {
            error = "生産リストにアイテムを追加してください。";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 散布機台数の入力行を EnvironmentCountOverride の列に変換する。
    /// 空欄の行は自動値扱いで無視する。整数でない・範囲外（下限未満・上限超過）の
    /// 行があればエラーを返す（入力範囲は仕様決定 BS）。
    /// </summary>
    public static bool TryParseEnvironmentCounts(
        IEnumerable<EnvCountInput> inputs,
        out List<EnvironmentCountOverride> overrides,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        overrides = [];
        error = null;
        foreach (EnvCountInput row in inputs)
        {
            if (string.IsNullOrWhiteSpace(row.CountText))
            {
                continue;
            }

            if (!int.TryParse(row.CountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                || count < row.Min || count > row.Max)
            {
                error = $"{EntityDisplay.Format(row.EnvName, row.EnvId)} の散布機台数は {row.Min}〜{row.Max} の整数で入力してください。";
                return false;
            }

            overrides.Add(new EnvironmentCountOverride(row.EnvId, count));
        }

        return true;
    }

    /// <summary>
    /// 再計算をまたいで保持する散布機台数の入力を、新しい入力範囲と整合させる。
    /// 整数かつ下限〜上限の範囲内の値だけを残し、範囲外・非整数は
    /// 空欄（自動値）へ戻す（仕様決定 AH・BS）。
    /// ユーザーが直前に入力した値の検証は <see cref="TryParseEnvironmentCounts"/> が担うため、
    /// ここでは保持値の丸め（クランプ）は行わず自動値への復帰のみを行う。
    /// </summary>
    public static string ReconcileEnvCountText(string? countText, int min, int max)
    {
        if (int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
            && count >= min && count <= max)
        {
            return countText!;
        }

        return "";
    }

    /// <summary>
    /// 再計算をまたいで保持する採取レート入力を、新しい計画の採取対象と整合させて
    /// 入力行を再構築する。採取対象から外れ、かつ採取レートとして受理されない保持値
    /// （アイテムが採取素材でない・値が受理されない）は keptTexts から取り除く。
    /// 受理される値は採取対象へ戻ったときのために保持し続ける（仕様決定 AH と同型）。
    /// </summary>
    public static List<GatherRateState> ReconcileGatherRateRows(
        IDictionary<string, string> keptTexts,
        IReadOnlyList<string> gatherableItemIds,
        MasterDataSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(keptTexts);
        ArgumentNullException.ThrowIfNull(gatherableItemIds);
        ArgumentNullException.ThrowIfNull(snapshot);

        foreach (string itemId in keptTexts.Keys.ToList())
        {
            if (!gatherableItemIds.Contains(itemId)
                && !TryParseGatherRates(
                    [new GatherRateInput(itemId, itemId, keptTexts[itemId])],
                    snapshot,
                    out _,
                    out _))
            {
                keptTexts.Remove(itemId);
            }
        }

        return gatherableItemIds
            .Select(itemId => new GatherRateState(itemId)
            {
                RateText = keptTexts.TryGetValue(itemId, out string? kept) ? kept : "",
            })
            .ToList();
    }

    /// <summary>
    /// 採取素材の利用可能レート入力行を GatherRateOverride の列に変換する。
    /// 空欄の行はマップ既定値扱いで無視する（仕様決定 AE）。
    /// 行が指すアイテムが存在しない・採取素材でない、または値が非数値・負・非有限ならエラーを返す。
    /// 0 以上ならマップ値を超える入力も受理する。
    /// </summary>
    public static bool TryParseGatherRates(
        IEnumerable<GatherRateInput> inputs,
        MasterDataSnapshot snapshot,
        out List<GatherRateOverride> overrides,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(snapshot);
        overrides = [];
        error = null;
        foreach (GatherRateInput row in inputs)
        {
            if (string.IsNullOrWhiteSpace(row.RateText))
            {
                continue;
            }

            if (!snapshot.ItemsById.TryGetValue(row.ItemId, out Item? item) || !item.IsGatherable)
            {
                error = $"{EntityDisplay.Format(item?.Name, row.ItemId)} は採取素材ではありません。";
                return false;
            }

            if (!double.TryParse(row.RateText, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate)
                || !double.IsFinite(rate) || rate < 0)
            {
                error = $"{EntityDisplay.Format(item.Name, item.Id)} の利用可能レートは 0 以上の数値で入力してください。";
                return false;
            }

            overrides.Add(new GatherRateOverride(row.ItemId, rate));
        }

        return true;
    }

    /// <summary>
    /// 期間入力（日・時・分の各欄の文字列）を PeriodAmount へ変換する。
    /// 空欄は 0 扱い。すべての欄が 0 以上の有限値なら true を返す。
    /// </summary>
    public static bool TryParsePeriod(
        string? daysText,
        string? hoursText,
        string? minutesText,
        out PeriodAmount period)
    {
        period = default;
        return
            TryParsePart(daysText, out double days)
            && TryParsePart(hoursText, out double hours)
            && TryParsePart(minutesText, out double minutes)
            && PeriodAmount.TryCreate(days, hours, minutes, out period);
    }

    private static bool TryParsePart(string? text, out double value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0;
            return true;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// 検索ボックスの本文が選択済みアイテム名とずれたとき、選択を解除すべきか。
    /// 古い ItemId のまま計算へ進まないための UI 判定。
    /// </summary>
    public static bool ShouldDeselectItem(string? selectedItemId, string? selectedItemName, string? filterText) =>
        selectedItemId is not null && filterText != selectedItemName;

    /// <summary>
    /// イベントチェックの初期状態を作る。開催期間（±1 日）内のイベントは
    /// 既定で有効とし、期間外は IsActiveByDefault=false で返す（仕様決定 T の UI 既定）。
    /// </summary>
    public static List<EventCheck> InitializeEventChecks(IEnumerable<GameEvent> gameEvents, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(gameEvents);
        return gameEvents
            .Select(e => new EventCheck(e, EventAutoActivation.IsActiveByDefault(e, today)))
            .ToList();
    }

    /// <summary>
    /// 判定時点の日付で既定の有効判定を評価し直す。
    /// 日付をまたいでページを開き続けた場合の既定の鮮度を保つための再評価であり、
    /// ユーザーが未操作のチェックのみ既定へ追従させ、操作済みのものは保持する。
    /// </summary>
    public static void RefreshEventCheckDefaults(IEnumerable<EventCheck> checks, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(checks);
        foreach (EventCheck check in checks)
        {
            check.IsActiveByDefault = EventAutoActivation.IsActiveByDefault(check.Event, today);
            if (!check.Touched)
            {
                check.Checked = check.IsActiveByDefault;
            }
        }
    }
}
