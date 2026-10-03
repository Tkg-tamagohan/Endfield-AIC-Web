using EndfieldAicWeb.Domain.Calculation;
using EndfieldAicWeb.Domain.Models;
using DomainEnv = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Application;

/// <summary>
/// スナップショットから表示名・アイコンキーを引く検索群。
/// スナップショット未構築や未登録の Id は例外を出さずフォールバックを返す
/// （表示用の検索なので、取れないときは Id や null のまま表示に回す）。
/// </summary>
public static class SnapshotLookup
{
    /// <summary>アイテムの表示名。未登録・スナップショットなしは Id をそのまま返す。</summary>
    public static string ItemName(this MasterDataSnapshot? snapshot, string id) =>
        snapshot is not null && snapshot.ItemsById.TryGetValue(id, out Item? item) ? item.Name : id;

    /// <summary>設備の表示名。未登録・スナップショットなしは Id をそのまま返す。</summary>
    public static string FacilityName(this MasterDataSnapshot? snapshot, string id) =>
        snapshot is not null && snapshot.FacilitiesById.TryGetValue(id, out Facility? facility) ? facility.Name : id;

    /// <summary>レシピの表示名。未登録・スナップショットなしは Id をそのまま返す。</summary>
    public static string RecipeName(this MasterDataSnapshot? snapshot, string id) =>
        snapshot is not null && snapshot.RecipesById.TryGetValue(id, out Recipe? recipe) ? recipe.Name : id;

    /// <summary>環境の表示名。未登録・スナップショットなしは Id をそのまま返す。</summary>
    public static string EnvName(this MasterDataSnapshot? snapshot, string id) =>
        snapshot is not null && snapshot.EnvironmentsById.TryGetValue(id, out DomainEnv? env) ? env.Name : id;

    /// <summary>アイテムのアイコンキー。未登録・スナップショットなしは null。</summary>
    public static string? ItemIconKey(this MasterDataSnapshot? snapshot, string itemId) =>
        snapshot is not null && snapshot.ItemsById.TryGetValue(itemId, out Item? item) ? item.IconKey : null;

    /// <summary>設備のアイコンキー。未登録・スナップショットなしは null。</summary>
    public static string? FacilityIconKey(this MasterDataSnapshot? snapshot, string facilityId) =>
        snapshot is not null && snapshot.FacilitiesById.TryGetValue(facilityId, out Facility? facility) ? facility.IconKey : null;

    /// <summary>環境のアイコンキー。未登録・スナップショットなしは null。</summary>
    public static string? EnvIconKey(this MasterDataSnapshot? snapshot, string envId) =>
        snapshot is not null && snapshot.EnvironmentsById.TryGetValue(envId, out DomainEnv? env) ? env.IconKey : null;

    /// <summary>イベント所属アイテムか（「イベント」タグ表示の判定）。</summary>
    public static bool IsEventItem(this MasterDataSnapshot? snapshot, string itemId) =>
        snapshot is not null
        && snapshot.ItemsById.TryGetValue(itemId, out Item? item)
        && item.GameEventId is not null;
}
