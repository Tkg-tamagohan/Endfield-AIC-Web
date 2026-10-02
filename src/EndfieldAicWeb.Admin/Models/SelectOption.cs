using EndfieldAicWeb.Domain.Models;

namespace EndfieldAicWeb.Admin.Models;

/// <summary>RefSelect の選択肢（値＋表示名）。</summary>
public sealed record SelectOption(string Value, string Label)
{
    /// <summary>エンティティ一覧から（Id→値、Name→表示名）の選択肢を作る。</summary>
    public static IReadOnlyList<SelectOption> Of(IEnumerable<MasterEntity> entities) =>
        entities.Select(e => new SelectOption(e.Id, e.Name)).ToList();
}
