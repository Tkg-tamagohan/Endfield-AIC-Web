namespace EndfieldAicWeb.Domain.Models;

/// <summary>
/// マップで採取できるアイテムと毎分の上限。
/// </summary>
public class GatherRate
{
    /// <summary>採取アイテムの Id。</summary>
    public required string ItemId { get; set; }

    /// <summary>採取上限がないか。</summary>
    public bool IsUnlimited { get; set; }

    /// <summary>毎分の採取上限。無限の場合は null。</summary>
    public double? RatePerMinute { get; set; }
}
