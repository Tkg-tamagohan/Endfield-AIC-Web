using EndfieldAicWeb.Domain.Models;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Application;

/// <summary>他エンティティからの参照箇所 1 件。</summary>
public sealed record MasterReference(string EntityKind, string EntityId, string Field);

/// <summary>
/// 指定 Id を参照している箇所を文書全体から列挙する。
/// 管理ツールで削除・Id 変更を行う前の影響確認に使う（参照の有無を警告文へ載せる）。
/// </summary>
public static class MasterReferenceFinder
{
    /// <summary>アイテム Id を参照する箇所（Environment.ConsumeItemId、レシピ入出力、ペア固定消費）。</summary>
    public static IReadOnlyList<MasterReference> FindItemReferences(MasterDocument document, string itemId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(itemId);

        var refs = new List<MasterReference>();
        foreach (Environment env in document.Environments)
        {
            if (env.ConsumeItemId == itemId)
            {
                refs.Add(new MasterReference("Environment", env.Id, "ConsumeItemId"));
            }
        }

        foreach (Recipe recipe in document.Recipes)
        {
            for (int i = 0; i < recipe.Inputs.Count; i++)
            {
                if (recipe.Inputs[i].ItemId == itemId)
                {
                    refs.Add(new MasterReference("Recipe", recipe.Id, $"Inputs[{i}].ItemId"));
                }
            }

            for (int i = 0; i < recipe.Outputs.Count; i++)
            {
                if (recipe.Outputs[i].ItemId == itemId)
                {
                    refs.Add(new MasterReference("Recipe", recipe.Id, $"Outputs[{i}].ItemId"));
                }
            }

            for (int i = 0; i < recipe.Facilities.Count; i++)
            {
                if (recipe.Facilities[i].FixedConsumption?.ItemId == itemId)
                {
                    refs.Add(new MasterReference("Recipe", recipe.Id, $"Facilities[{i}].FixedConsumption.ItemId"));
                }
            }
        }

        foreach (GameMap map in document.Maps)
        {
            for (int i = 0; i < map.GatherRates.Count; i++)
            {
                if (map.GatherRates[i].ItemId == itemId)
                {
                    refs.Add(new MasterReference("GameMap", map.Id, $"GatherRates[{i}].ItemId"));
                }
            }
        }

        return refs;
    }

    /// <summary>設備 Id を参照する箇所（Environment.ProviderFacilityId、ペアの FacilityId）。</summary>
    public static IReadOnlyList<MasterReference> FindFacilityReferences(MasterDocument document, string facilityId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(facilityId);

        var refs = new List<MasterReference>();
        foreach (Environment env in document.Environments)
        {
            if (env.ProviderFacilityId == facilityId)
            {
                refs.Add(new MasterReference("Environment", env.Id, "ProviderFacilityId"));
            }
        }

        foreach (Recipe recipe in document.Recipes)
        {
            for (int i = 0; i < recipe.Facilities.Count; i++)
            {
                if (recipe.Facilities[i].FacilityId == facilityId)
                {
                    refs.Add(new MasterReference("Recipe", recipe.Id, $"Facilities[{i}].FacilityId"));
                }
            }
        }

        return refs;
    }

    /// <summary>環境 Id を参照する箇所（ペアの EnvironmentId）。</summary>
    public static IReadOnlyList<MasterReference> FindEnvironmentReferences(MasterDocument document, string environmentId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(environmentId);

        var refs = new List<MasterReference>();
        foreach (Recipe recipe in document.Recipes)
        {
            for (int i = 0; i < recipe.Facilities.Count; i++)
            {
                if (recipe.Facilities[i].EnvironmentId == environmentId)
                {
                    refs.Add(new MasterReference("Recipe", recipe.Id, $"Facilities[{i}].EnvironmentId"));
                }
            }
        }

        return refs;
    }

    /// <summary>イベント Id を参照する箇所（Item/Environment/Recipe の GameEventId）。</summary>
    public static IReadOnlyList<MasterReference> FindGameEventReferences(MasterDocument document, string gameEventId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(gameEventId);

        var refs = new List<MasterReference>();
        foreach (Item item in document.Items)
        {
            if (item.GameEventId == gameEventId)
            {
                refs.Add(new MasterReference("Item", item.Id, "GameEventId"));
            }
        }

        foreach (Environment env in document.Environments)
        {
            if (env.GameEventId == gameEventId)
            {
                refs.Add(new MasterReference("Environment", env.Id, "GameEventId"));
            }
        }

        foreach (Recipe recipe in document.Recipes)
        {
            if (recipe.GameEventId == gameEventId)
            {
                refs.Add(new MasterReference("Recipe", recipe.Id, "GameEventId"));
            }
        }

        foreach (GameMap map in document.Maps)
        {
            if (map.GameEventId == gameEventId)
            {
                refs.Add(new MasterReference("GameMap", map.Id, "GameEventId"));
            }
        }

        return refs;
    }
}
