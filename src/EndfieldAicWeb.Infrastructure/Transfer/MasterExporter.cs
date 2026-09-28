using System.Text.Encodings.Web;
using System.Text.Json;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Infrastructure.Transfer;

/// <summary>
/// <see cref="MasterDocument"/> を全置換形式の正本 JSON へ書き出す（管理ツールのエクスポート物、仕様決定 D/R）。
/// 書き出し前に <see cref="MasterValidator"/> と Icons 節の構造規則を同一適用し、
/// 違反時は <see cref="MasterValidationException"/> で拒否する。
/// </summary>
public static class MasterExporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// SchemaVersion/DataVersion 付きの JSON 文字列を返す。
    /// 全エンティティを含み、Icons 節は <see cref="MasterDocument.Icons"/> をそのまま出力する。
    /// </summary>
    public static string Export(MasterDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var errors = new List<MasterValidationError>();

        if (document.SchemaVersion != MasterJsonReader.SupportedSchemaVersion)
        {
            errors.Add(new MasterValidationError(
                "Document",
                "",
                "SchemaVersion",
                $"未対応の SchemaVersion です: {document.SchemaVersion}（対応版: {MasterJsonReader.SupportedSchemaVersion}）"));
        }

        if (string.IsNullOrWhiteSpace(document.DataVersion))
        {
            errors.Add(new MasterValidationError("Document", "", "DataVersion", "DataVersion は必須です。"));
        }

        // null コレクション・null 要素も拒否対象とし、検証自体は残りの内容で続行する。
        List<Item> items = RequiredCollection(document.Items, "Items", errors);
        List<Facility> facilities = RequiredCollection(document.Facilities, "Facilities", errors);
        List<Environment> environments = RequiredCollection(document.Environments, "Environments", errors);
        List<GameEvent> gameEvents = RequiredCollection(document.GameEvents, "GameEvents", errors);
        List<Recipe> recipes = RequiredCollection(document.Recipes, "Recipes", errors);
        List<IconEntry> icons = RequiredCollection(document.Icons, "Icons", errors);

        RejectNullElements(items, "Items", errors);
        RejectNullElements(facilities, "Facilities", errors);
        RejectNullElements(environments, "Environments", errors);
        RejectNullElements(gameEvents, "GameEvents", errors);
        RejectNullElements(recipes, "Recipes", errors);
        RejectNullElements(icons, "Icons", errors);

        // Description はスキーマ type=string の必須。null（空文字化しない呼び出し側の違反）は拒否する。
        RejectNullDescription(items, "Item", errors);
        RejectNullDescription(facilities, "Facility", errors);
        RejectNullDescription(environments, "Environment", errors);
        RejectNullDescription(gameEvents, "GameEvent", errors);
        RejectNullDescription(recipes, "Recipe", errors);

        // レシピ内配列・要素の null は MasterValidator が参照できないため、ここで検出して対象から外す。
        var safeRecipes = new List<Recipe>();
        for (int i = 0; i < recipes.Count; i++)
        {
            Recipe? recipe = recipes[i];
            if (recipe is null)
            {
                continue;
            }

            bool unsafe_ = false;
            unsafe_ |= RejectNullList(recipe.Inputs, nameof(recipe.Inputs), recipe, errors);
            unsafe_ |= RejectNullList(recipe.Outputs, nameof(recipe.Outputs), recipe, errors);
            unsafe_ |= RejectNullList(recipe.Facilities, nameof(recipe.Facilities), recipe, errors);
            if (!unsafe_)
            {
                safeRecipes.Add(recipe);
            }
        }

        MasterValidator.ValidateAll(
            items.Where(e => e is not null).ToList(),
            facilities.Where(e => e is not null).ToList(),
            environments.Where(e => e is not null).ToList(),
            gameEvents.Where(e => e is not null).ToList(),
            safeRecipes,
            errors);

        MasterJsonReader.ValidateIconManifestValues(
            icons.Select((e, i) => (Element: e, Index: i))
                .Where(x => x.Element is not null)
                .Select(x => (x.Index, (string?)x.Element!.Key, (string?)x.Element.File,
                    (string?)x.Element.Sha256, (long?)x.Element.Bytes)),
            errors);

        if (errors.Count > 0)
        {
            throw new MasterValidationException(errors);
        }

        var jsonDocument = new MasterJsonDocument
        {
            SchemaVersion = document.SchemaVersion,
            DataVersion = document.DataVersion,
            Items = document.Items.Select(ToItemJson).Cast<ItemJson?>().ToList(),
            Facilities = document.Facilities.Select(ToFacilityJson).Cast<FacilityJson?>().ToList(),
            Environments = document.Environments.Select(ToEnvironmentJson).Cast<EnvironmentJson?>().ToList(),
            GameEvents = document.GameEvents.Select(ToGameEventJson).Cast<GameEventJson?>().ToList(),
            Recipes = document.Recipes.Select(ToRecipeJson).Cast<RecipeJson?>().ToList(),
            Icons = document.Icons.Select(e => (IconJson?)new IconJson
            {
                Key = e.Key,
                File = e.File,
                Sha256 = e.Sha256,
                Bytes = e.Bytes,
            }).ToList(),
        };

        return JsonSerializer.Serialize(jsonDocument, SerializerOptions);
    }

    private static List<T> RequiredCollection<T>(
        List<T>? collection,
        string name,
        ICollection<MasterValidationError> errors)
    {
        if (collection is null)
        {
            errors.Add(new MasterValidationError(
                "Document", "", name, $"必須配列 {name} がありません。"));
            return [];
        }

        return collection;
    }

    private static void RejectNullElements<T>(
        List<T> entities,
        string collectionName,
        ICollection<MasterValidationError> errors)
        where T : class
    {
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] is null)
            {
                errors.Add(new MasterValidationError(
                    collectionName, "", "", $"配列 {collectionName} の {i} 番目の要素が null です。"));
            }
        }
    }

    private static void RejectNullDescription(
        IEnumerable<MasterEntity> entities,
        string entityKind,
        ICollection<MasterValidationError> errors)
    {
        foreach (MasterEntity entity in entities)
        {
            if (entity is not null && entity.Description is null)
            {
                errors.Add(new MasterValidationError(
                    entityKind, entity.Id ?? "", "Description", $"{entityKind} の Description が null です。"));
            }
        }
    }

    /// <summary>レシピ内配列が null・null 要素を含む場合にエラーを記録し、対象外とする。</summary>
    private static bool RejectNullList<T>(
        List<T>? list,
        string name,
        Recipe recipe,
        ICollection<MasterValidationError> errors)
        where T : class
    {
        if (list is null)
        {
            errors.Add(new MasterValidationError(
                "Recipe", recipe.Id ?? "", name, $"レシピ {recipe.Id} の {name} が null です。"));
            return true;
        }

        bool found = false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is null)
            {
                errors.Add(new MasterValidationError(
                    "Recipe", recipe.Id ?? "", name, $"レシピ {recipe.Id} の {name}[{i}] が null です。"));
                found = true;
            }
        }

        return found;
    }

    private static ItemJson ToItemJson(Item e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
        IconKey = e.IconKey,
        VersionAdded = e.VersionAdded,
        VersionRemoved = e.VersionRemoved,
        Category = e.Category,
        IsBaseMaterial = e.IsBaseMaterial,
        TransportKind = e.TransportKind.ToString(),
        GameEventId = e.GameEventId,
    };

    private static FacilityJson ToFacilityJson(Facility e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
        IconKey = e.IconKey,
        VersionAdded = e.VersionAdded,
        VersionRemoved = e.VersionRemoved,
        Width = e.Width,
        Height = e.Height,
        PowerConsumption = e.PowerConsumption,
    };

    private static EnvironmentJson ToEnvironmentJson(Environment e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
        IconKey = e.IconKey,
        VersionAdded = e.VersionAdded,
        VersionRemoved = e.VersionRemoved,
        ProviderFacilityId = e.ProviderFacilityId,
        ConsumeItemId = e.ConsumeItemId,
        ConsumeRatePerSecond = e.ConsumeRatePerSecond,
        GameEventId = e.GameEventId,
    };

    private static GameEventJson ToGameEventJson(GameEvent e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
        IconKey = e.IconKey,
        VersionAdded = e.VersionAdded,
        VersionRemoved = e.VersionRemoved,
        ActiveFrom = e.ActiveFrom,
        ActiveTo = e.ActiveTo,
    };

    private static RecipeJson ToRecipeJson(Recipe e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
        IconKey = e.IconKey,
        VersionAdded = e.VersionAdded,
        VersionRemoved = e.VersionRemoved,
        GameEventId = e.GameEventId,
        Inputs = e.Inputs.Select(i => (RecipeIoJson?)new RecipeIoJson
        {
            ItemId = i.ItemId,
            Quantity = i.Quantity,
        }).ToList(),
        Outputs = e.Outputs.Select(o => (RecipeOutputJson?)new RecipeOutputJson
        {
            ItemId = o.ItemId,
            Quantity = o.Quantity,
            SortOrder = o.SortOrder,
        }).ToList(),
        Facilities = e.Facilities.Select(p => (RecipeFacilityJson?)new RecipeFacilityJson
        {
            FacilityId = p.FacilityId,
            CycleTime = p.CycleTime,
            EnvironmentId = p.EnvironmentId,
            FixedConsumption = p.FixedConsumption is null
                ? null
                : new FixedConsumptionJson
                {
                    ItemId = p.FixedConsumption.ItemId,
                    RatePerSecond = p.FixedConsumption.RatePerSecond,
                },
        }).ToList(),
    };
}
