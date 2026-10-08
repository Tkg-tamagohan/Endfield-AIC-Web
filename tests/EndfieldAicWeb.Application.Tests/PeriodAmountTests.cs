using EndfieldAicWeb.Application.PlanView;

namespace EndfieldAicWeb.Application.Tests;

/// <summary>PRD: 期間入力と表示単位の換算。</summary>
public class PeriodAmountTests
{
    // PRD-01: 日・時・分を合計分数に換算する（1 日 2 時 30 分 = 1590 分）。
    [Fact]
    public void TotalMinutesSumsComponents()
    {
        bool ok = PeriodAmount.TryCreate(1, 2, 30, out PeriodAmount period);

        Assert.True(ok);
        Assert.Equal(1590, period.TotalMinutes);
    }

    // PRD-02: 変換係数。毎分 = そのまま、毎秒 = ÷60、期間 = ×合計分数。
    [Fact]
    public void ConvertAppliesUnitFactor()
    {
        Assert.True(PeriodAmount.TryCreate(0, 2, 0, out PeriodAmount period));

        Assert.Equal(120, AmountConverter.Convert(120, AmountUnit.PerMinute, period));
        Assert.Equal(2, AmountConverter.Convert(120, AmountUnit.PerSecond, period));
        Assert.Equal(120 * 120, AmountConverter.Convert(120, AmountUnit.Period, period));
    }

    // PRD-03: 負・非有限の成分は拒否する。
    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    [InlineData(double.NaN, 0, 0)]
    [InlineData(0, double.PositiveInfinity, 0)]
    public void RejectsNegativeOrNonFinite(double days, double hours, double minutes)
    {
        Assert.False(PeriodAmount.TryCreate(days, hours, minutes, out _));
    }

    // PRD-04: 接尾辞は単位ごとに定まる。
    [Fact]
    public void Suffixes()
    {
        Assert.Equal("/分", AmountConverter.Suffix(AmountUnit.PerMinute));
        Assert.Equal("/秒", AmountConverter.Suffix(AmountUnit.PerSecond));
        Assert.Equal("/期間", AmountConverter.Suffix(AmountUnit.Period));
    }
}
