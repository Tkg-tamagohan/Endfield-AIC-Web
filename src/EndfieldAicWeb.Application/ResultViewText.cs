using System.Globalization;
using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application;

/// <summary>
/// 計算結果パネルの表示文字列を組み立てる純粋関数群。
/// 単位換算は AmountUnit と期間を引数に取り、エンティティ名は SnapshotLookup の
/// フォールバック（未登録は Id）に従う。
/// </summary>
public static class ResultViewText
{
    /// <summary>個/分の値を表示単位へ換算して整形する。毎秒だけ小数桁を増やす。</summary>
    public static string Format(double perMinute, AmountUnit unit, PeriodAmount period)
    {
        double converted = AmountConverter.Convert(perMinute, unit, period);
        return converted.ToString(unit == AmountUnit.PerSecond ? "0.###" : "0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>個/分のまま整形する（単位切替を適用しない表示用）。</summary>
    public static string FormatPerMinute(double perMinute) =>
        perMinute.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>期間表示のときだけ付ける個/期間の併記。期間未入力・不正（null）なら空。</summary>
    public static string PeriodConsumption(double ratePerMinute, AmountUnit unit, PeriodAmount? period) =>
        unit == AmountUnit.Period && period is { } p
            ? $"（{(ratePerMinute * p.TotalMinutes).ToString("0.##", CultureInfo.InvariantCulture)} 個/期間）"
            : "";

    /// <summary>素材の供給内訳 1 件の表示文。</summary>
    public static string SupplyText(
        MasterDataSnapshot? snapshot,
        SupplyPortion portion,
        AmountUnit unit,
        PeriodAmount period)
    {
        ArgumentNullException.ThrowIfNull(portion);

        string amount = $"{Format(portion.AmountPerMinute, unit, period)}{AmountConverter.Suffix(unit)}";
        return portion.Kind switch
        {
            SupplyKind.Recipe => $"レシピ {snapshot.RecipeLabel(portion.RecipeId!)} {amount}",
            SupplyKind.Byproduct => $"副産物 {snapshot.RecipeLabel(portion.RecipeId!)} {amount}",
            SupplyKind.Gathered => $"採取 {amount}",
            SupplyKind.ExternalProcurement => $"外部調達 {amount}",
            _ => "",
        };
    }

    /// <summary>ペア代替選択ドロップダウンの候補ラベル。</summary>
    public static string OptionLabel(MasterDataSnapshot? snapshot, PairOption option)
    {
        ArgumentNullException.ThrowIfNull(option);

        string label = $"{snapshot.RecipeLabel(option.RecipeId)} ／ {snapshot.FacilityName(option.FacilityId)} {option.CycleTime.ToString("0.##", CultureInfo.InvariantCulture)}秒";
        label += option.EnvironmentId is null ? "・環境なし" : $"・{snapshot.EnvName(option.EnvironmentId)}";
        if (option.FixedConsumption is { } fc)
        {
            label += $"・{snapshot.ItemName(fc.ItemId)} {fc.RatePerMinute.ToString("0.##", CultureInfo.InvariantCulture)}/分";
        }

        if (option.IsDefault)
        {
            label += "（既定）";
        }

        return label;
    }

    /// <summary>イベント名に付ける開催期間の併記。常設（両端 null）は空。</summary>
    public static string EventPeriod(GameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        if (gameEvent.ActiveFrom is null && gameEvent.ActiveTo is null)
        {
            return "";
        }

        string from = gameEvent.ActiveFrom?.ToLocalTime().ToString("MM/dd") ?? "";
        string to = gameEvent.ActiveTo?.ToLocalTime().ToString("MM/dd") ?? "";
        return $"（{from}〜{to}）";
    }

    /// <summary>採取レート入力欄のプレースホルダ（マップ既定値。無制限なら「無制限」）。</summary>
    public static string GatherRatePlaceholder(MasterDataSnapshot? snapshot, ContextFilter context, string itemId)
    {
        ArgumentNullException.ThrowIfNull(context);

        return snapshot is not null && MapSelection.DefaultGatherCap(snapshot, context, itemId) is double cap
            ? FormatPerMinute(cap)
            : "無制限";
    }

    /// <summary>採取レート入力欄のマップ既定値ラベル。</summary>
    public static string GatherRateDefaultLabel(MasterDataSnapshot? snapshot, ContextFilter context, string itemId)
    {
        ArgumentNullException.ThrowIfNull(context);

        return snapshot is not null && MapSelection.DefaultGatherCap(snapshot, context, itemId) is double cap
            ? $"マップ既定 {FormatPerMinute(cap)} 個/分"
            : "マップ既定 無制限";
    }
}
