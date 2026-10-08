using EndfieldAicWeb.Admin.Services;
using EndfieldAicWeb.Application.MasterEditing;
using EndfieldAicWeb.Domain.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EndfieldAicWeb.Admin.Components;

/// <summary>
/// エンティティ編集ページの共通骨格。未読み込みゲート・編集パネル・一覧・新規/削除・選択・スクロールを集約する。
/// 固有フィールドとサブテーブルは <see cref="EntityFields"/> フラグメントでページ側が書く。
/// </summary>
public partial class EntityEditLayout<TEntity> where TEntity : MasterEntity
{
    [Inject]
    private AdminDocumentService Store { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    /// <summary>編集パネルの見出し（例: アイテム編集）。</summary>
    [Parameter, EditorRequired]
    public string EditorTitle { get; set; } = "";

    /// <summary>未選択時のヒント文。</summary>
    [Parameter, EditorRequired]
    public string EmptyHint { get; set; } = "";

    /// <summary>選択中エンティティ。ページ側の保持フィールドと <c>@bind-Selected</c> で双方向に同期する。</summary>
    [Parameter]
    public TEntity? Selected { get; set; }

    [Parameter]
    public EventCallback<TEntity?> SelectedChanged { get; set; }

    /// <summary>対象コレクション（文書側のリスト参照）。一覧・選択・追加・削除で使う。</summary>
    [Parameter, EditorRequired]
    public Func<MasterDocument, List<TEntity>> Collection { get; set; } = default!;

    /// <summary>新規 Id のプレフィックス（<see cref="EntityFactory.SuggestId"/> へ渡す）。</summary>
    [Parameter, EditorRequired]
    public string IdPrefix { get; set; } = default!;

    /// <summary>新規エンティティの生成。文書と採番済み Id を受け取る。</summary>
    [Parameter, EditorRequired]
    public Func<MasterDocument, string, TEntity> CreateEntity { get; set; } = default!;

    /// <summary>固有フィールドとサブテーブルの断片。選択中エンティティを受け取る。</summary>
    [Parameter, EditorRequired]
    public RenderFragment<TEntity> EntityFields { get; set; } = default!;

    /// <summary>削除確認の参照解析。null は被参照フィールドのない系統で、単純確認文になる。</summary>
    [Parameter]
    public Func<MasterDocument, string, IReadOnlyList<MasterReference>>? FindReferences { get; set; }

    /// <summary>削除時に ClearInvalidOwners へ渡す関連行（エンティティ自身以外）。null なら自身のみクリアする。</summary>
    [Parameter]
    public Func<TEntity, IEnumerable<object>>? RelatedOwnersOf { get; set; }

    /// <summary>共通フィールド確定後の追加処理（レシピの RecipeId 伝搬用）。</summary>
    [Parameter]
    public Action<TEntity>? AfterFieldsChanged { get; set; }

    /// <summary>ListPane の追加検索対象へそのまま渡す（ItemsPage のカテゴリ検索用）。</summary>
    [Parameter]
    public Func<TEntity, string?>? ExtraSearchOf { get; set; }

    /// <summary>ListPane のカテゴリ絞り込み対象へそのまま渡す（ItemsPage 用）。</summary>
    [Parameter]
    public Func<TEntity, string?>? CategoryOf { get; set; }

    /// <summary>共通ヒント文の前置き（EventsPage の常設イベント注記用）。</summary>
    [Parameter]
    public string? HintPrefix { get; set; }

    private ElementReference _panelRef;
    private bool _scrollToPanel;

    private List<TEntity> Entities => Store.Document is { } doc ? Collection(doc) : [];

    private async Task SelectEntity(string id)
    {
        Selected = Entities.FirstOrDefault(e => e.Id == id);
        _scrollToPanel = true;
        await SelectedChanged.InvokeAsync(Selected);
    }

    private async Task New()
    {
        MasterDocument doc = Store.Document!;
        List<TEntity> entities = Collection(doc);
        string id = EntityFactory.SuggestId(entities.Select(e => e.Id), IdPrefix);
        TEntity entity = CreateEntity(doc, id);
        entities.Add(entity);
        Store.NotifyChanged();
        Selected = entity;
        _scrollToPanel = true;
        await SelectedChanged.InvokeAsync(entity);
    }

    private async Task Delete(TEntity entity)
    {
        IReadOnlyList<MasterReference> refs = FindReferences?.Invoke(Store.Document!, entity.Id) ?? [];
        string message = refs.Count == 0
            ? $"{entity.Name}（{entity.Id}）を削除しますか？"
            : $"{entity.Name}（{entity.Id}）は {refs.Count} 箇所から参照されています:\n"
                + string.Join("\n", refs.Select(r => $"・{r.EntityKind} {r.EntityId} の {r.Field}"))
                + "\n\n参照が残るため検証違反になります。削除しますか？";
        if (!await JS.InvokeAsync<bool>("confirm", message))
        {
            return;
        }

        Entities.Remove(entity);
        if (RelatedOwnersOf is { } related)
        {
            Store.ClearInvalidOwners([entity, ..related(entity)]);
        }
        else
        {
            Store.ClearInvalidOwner(entity);
        }

        if (Selected == entity)
        {
            Selected = null;
            await SelectedChanged.InvokeAsync(null);
        }

        Store.NotifyChanged();
    }

    private void Changed(string? _)
    {
        if (Selected is { } entity)
        {
            AfterFieldsChanged?.Invoke(entity);
        }

        Store.NotifyChanged();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_scrollToPanel)
        {
            _scrollToPanel = false;
            await JS.InvokeVoidAsync("scrollElementIntoView", _panelRef);
        }
    }
}
