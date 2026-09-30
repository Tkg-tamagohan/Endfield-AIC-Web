using System.Globalization;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application;

/// <summary>生産リスト 1 行の入力値（選択アイテム Id と個/分の文字列）。</summary>
public sealed record TargetRowInput(string? ItemId, string? RateText);

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
                error = $"{item.Name} の数量を 0 より大きい数値で入力してください。";
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
