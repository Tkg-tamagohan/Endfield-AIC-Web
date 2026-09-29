using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Application;

/// <summary>
/// マスタ文書から計算用スナップショットを作る。
/// </summary>
public static class MasterSnapshotFactory
{
    public static MasterDataSnapshot Create(MasterDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new MasterDataSnapshot
        {
            Items = document.Items,
            Facilities = document.Facilities,
            Environments = document.Environments,
            GameEvents = document.GameEvents,
            Recipes = document.Recipes,
        };
    }
}
