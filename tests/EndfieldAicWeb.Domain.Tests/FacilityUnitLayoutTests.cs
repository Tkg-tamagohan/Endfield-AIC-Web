using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Domain.Tests;

/// <summary>
/// docs/phases/test-specification-phase26.md §1 の FUL 系検査（ユニット割当の防御的上限）。
/// 発散した計画の巨大台数でスロット実体化が尽きないよう上限を設けつつ、
/// 輸送警告とグラフの台数分表示が同じ割当を見るためスキップはしない（計画書 §4 項目 11）。
/// </summary>
public class FacilityUnitLayoutTests
{
    // FUL-01: 台数が防御的上限を超えても割当は行われ、先頭 10,000 ユニットで占有、
    // 収まらない分は末尾ユニットへ集約される（share 合計は 1.0 を維持）。
    [Fact]
    public void OversizedFacilityAllocatesCappedUnitsAndAbsorbsRest()
    {
        MasterDataSnapshot snapshot = CalculationFixtures.Snapshot(
            [CalculationFixtures.Item("i-a"), CalculationFixtures.Item("i-x")],
            [CalculationFixtures.Facility("f-a")],
            [CalculationFixtures.Recipe("r-x", "f-a", 60.0, [("i-a", 1.0)], [("i-x", 1.0)])]);

        // 機械数 20,000（内部上限 MaxUnitSlots=10,000 を超過。internal のため値を直書きする）
        var runs = new List<RecipeRun> { new("r-x", "f-a", 20_000.0) };
        var facilities = new List<FacilityRequirement> { new("f-a", 20_000.0, 20_000) };

        Dictionary<string, List<FacilityUnitSlot>> units = FacilityUnitLayout.Allocate(
            runs, facilities, [], snapshot, []);

        List<FacilityUnitSlot> slots = units["f-a"];
        Assert.Equal(10_000, slots.Count);

        // 先頭側は各 1.0 機、末尾ユニットが残量 10,000 機を背負う
        Assert.All(slots.SkipLast(1), u => Assert.Equal(1.0, u.Used, 9));
        Assert.Equal(10_001.0, slots[^1].Used, 6);

        double shareTotal = slots.Sum(u => u.RunShares.GetValueOrDefault(0));
        Assert.Equal(1.0, shareTotal, 6);
    }
}
