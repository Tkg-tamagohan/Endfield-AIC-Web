using System.Text.Json;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Infrastructure.Transfer;

/// <summary>
/// マスタ JSON の解析・構造検証・実体化・意味検証の共有経路。
/// 読み込み（<see cref="MasterJsonLoader"/>）とエクスポート時検証で同一規則を適用する。
/// 違反はすべて <see cref="MasterValidationError"/> へ集約し、例外は投げない。
/// </summary>
internal static partial class MasterJsonReader
{
    /// <summary>対応するスキーマ版。新系統の v1 = 1（仕様決定 C）。</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>
    /// JSON を DTO へデシリアライズする。
    /// 構文エラー・型不一致・必須キー欠落（required メンバー違反）は error に返す。
    /// </summary>
    public static MasterJsonDocument? Parse(string json, out string? syntaxError)
    {
        try
        {
            MasterJsonDocument? document = JsonSerializer.Deserialize<MasterJsonDocument>(json);
            if (document is null)
            {
                syntaxError = "JSON が空です。";
                return null;
            }

            syntaxError = null;
            return document;
        }
        catch (JsonException ex)
        {
            syntaxError = $"JSON の解析に失敗しました: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// ID 系値の前後空白を除去する正規化（仕様決定 BX）。構造検証の前に呼ぶ。
    /// 必須値は空になっても null にせず後段の必須違反に委ね、null 許容の参照値は空なら null（未指定）にする。
    /// 内部空白は残り、空白禁止（BW）の違反として後段の検証で捕捉される。
    /// 除去が発生した値は "{ロケーション}: 「{正規化後の値}」" 形式で normalizations へ記録する。
    /// </summary>
    public static void NormalizeIdValues(MasterJsonDocument document, ICollection<string> normalizations)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(normalizations);

        NormalizeElements(document.Items, "Items", normalizations, (item, location) =>
        {
            item.Id = TrimIdValue(item.Id, $"{location}.Id", normalizations, nullable: false);
            item.GameEventId = TrimIdValue(item.GameEventId, $"{location}.GameEventId", normalizations, nullable: true);
        });

        NormalizeElements(document.Facilities, "Facilities", normalizations, (facility, location) =>
        {
            facility.Id = TrimIdValue(facility.Id, $"{location}.Id", normalizations, nullable: false);
        });

        NormalizeElements(document.Environments, "Environments", normalizations, (environment, location) =>
        {
            environment.Id = TrimIdValue(environment.Id, $"{location}.Id", normalizations, nullable: false);
            environment.ProviderFacilityId = TrimIdValue(
                environment.ProviderFacilityId, $"{location}.ProviderFacilityId", normalizations, nullable: false);
            environment.ConsumeItemId = TrimIdValue(
                environment.ConsumeItemId, $"{location}.ConsumeItemId", normalizations, nullable: false);
            environment.GameEventId = TrimIdValue(
                environment.GameEventId, $"{location}.GameEventId", normalizations, nullable: true);
        });

        NormalizeElements(document.GameEvents, "GameEvents", normalizations, (gameEvent, location) =>
        {
            gameEvent.Id = TrimIdValue(gameEvent.Id, $"{location}.Id", normalizations, nullable: false);
        });

        if (document.Recipes is not null)
        {
            for (int i = 0; i < document.Recipes.Count; i++)
            {
                if (document.Recipes[i] is not { } recipe)
                {
                    continue;
                }

                string location = $"Recipes[{i}]";
                recipe.Id = TrimIdValue(recipe.Id, $"{location}.Id", normalizations, nullable: false);
                recipe.GameEventId = TrimIdValue(
                    recipe.GameEventId, $"{location}.GameEventId", normalizations, nullable: true);
                NormalizeItemIds(recipe.Inputs, $"{location}.Inputs", normalizations,
                    io => io.ItemId, (io, value) => io.ItemId = value);
                NormalizeItemIds(recipe.Outputs, $"{location}.Outputs", normalizations,
                    output => output.ItemId, (output, value) => output.ItemId = value);

                if (recipe.Facilities is null)
                {
                    continue;
                }

                for (int j = 0; j < recipe.Facilities.Count; j++)
                {
                    if (recipe.Facilities[j] is not { } pair)
                    {
                        continue;
                    }

                    string pairLocation = $"{location}.Facilities[{j}]";
                    pair.FacilityId = TrimIdValue(
                        pair.FacilityId, $"{pairLocation}.FacilityId", normalizations, nullable: false);
                    pair.EnvironmentId = TrimIdValue(
                        pair.EnvironmentId, $"{pairLocation}.EnvironmentId", normalizations, nullable: true);
                    if (pair.FixedConsumption is { } fixedConsumption)
                    {
                        fixedConsumption.ItemId = TrimIdValue(
                            fixedConsumption.ItemId, $"{pairLocation}.FixedConsumption.ItemId",
                            normalizations, nullable: false);
                    }
                }
            }
        }

        NormalizeElements(document.Maps, "Maps", normalizations, (map, location) =>
        {
            map.Id = TrimIdValue(map.Id, $"{location}.Id", normalizations, nullable: false);
            map.GameEventId = TrimIdValue(map.GameEventId, $"{location}.GameEventId", normalizations, nullable: true);
            if (map.GatherRates is null)
            {
                return;
            }

            for (int i = 0; i < map.GatherRates.Count; i++)
            {
                if (map.GatherRates[i] is { } rate)
                {
                    rate.ItemId = TrimIdValue(
                        rate.ItemId, $"{location}.GatherRates[{i}].ItemId", normalizations, nullable: false);
                }
            }
        });
    }

    /// <summary>
    /// 構造検証を通過したドキュメントを実体化し、意味検証（値域・参照整合性・ペア一意性、
    /// <see cref="MasterValidator.ValidateAll"/> と同一規則）の結果を errors へ集約する。
    /// </summary>
    public static MasterDocument ToEntities(MasterJsonDocument document, ICollection<MasterValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(errors);

        var result = new MasterDocument
        {
            SchemaVersion = document.SchemaVersion!.Value,
            DataVersion = document.DataVersion!,
            Items = document.Items!.Select(e => new Item
            {
                Id = e!.Id!,
                Name = e.Name!,
                Description = e.Description!,
                IconKey = e.IconKey,
                VersionAdded = e.VersionAdded!,
                VersionRemoved = e.VersionRemoved,
                Category = e.Category!,
                IsGatherable = e.IsGatherable!.Value,
                TransportKind = Enum.Parse<TransportKind>(e.TransportKind!),
                GameEventId = e.GameEventId,
            }).ToList(),
            Facilities = document.Facilities!.Select(e => new Facility
            {
                Id = e!.Id!,
                Name = e.Name!,
                Description = e.Description!,
                IconKey = e.IconKey,
                VersionAdded = e.VersionAdded!,
                VersionRemoved = e.VersionRemoved,
                Width = e.Width!.Value,
                Height = e.Height!.Value,
                PowerConsumption = e.PowerConsumption!.Value,
            }).ToList(),
            Environments = document.Environments!.Select(e => new Environment
            {
                Id = e!.Id!,
                Name = e.Name!,
                Description = e.Description!,
                IconKey = e.IconKey,
                VersionAdded = e.VersionAdded!,
                VersionRemoved = e.VersionRemoved,
                ProviderFacilityId = e.ProviderFacilityId!,
                ConsumeItemId = e.ConsumeItemId!,
                ConsumeRatePerMinute = e.ConsumeRatePerMinute!.Value,
                CoverableMachines = e.CoverableMachines!.Value,
                GameEventId = e.GameEventId,
            }).ToList(),
            GameEvents = document.GameEvents!.Select(e => new GameEvent
            {
                Id = e!.Id!,
                Name = e.Name!,
                Description = e.Description!,
                IconKey = e.IconKey,
                VersionAdded = e.VersionAdded!,
                VersionRemoved = e.VersionRemoved,
                ActiveFrom = e.ActiveFrom,
                ActiveTo = e.ActiveTo,
            }).ToList(),
            Recipes = document.Recipes!.Select(e => new Recipe
            {
                Id = e!.Id!,
                Name = e.Name!,
                Description = e.Description!,
                IconKey = e.IconKey,
                VersionAdded = e.VersionAdded!,
                VersionRemoved = e.VersionRemoved,
                GameEventId = e.GameEventId,
                Inputs = e.Inputs!.Select(i => new RecipeInput
                {
                    ItemId = i!.ItemId!,
                    Quantity = i.Quantity!.Value,
                }).ToList(),
                Outputs = e.Outputs!.Select(o => new RecipeOutput
                {
                    ItemId = o!.ItemId!,
                    Quantity = o.Quantity!.Value,
                    SortOrder = o.SortOrder!.Value,
                }).ToList(),
                Facilities = e.Facilities!.Select(p => new RecipeFacility
                {
                    RecipeId = e.Id!,
                    FacilityId = p!.FacilityId!,
                    CycleTime = p.CycleTime!.Value,
                    EnvironmentId = p.EnvironmentId,
                    FixedConsumption = p.FixedConsumption is null
                        ? null
                        : new FixedConsumption
                        {
                            ItemId = p.FixedConsumption.ItemId!,
                            RatePerMinute = p.FixedConsumption.RatePerMinute!.Value,
                        },
                }).ToList(),
            }).ToList(),
            Maps = document.Maps!.Select(e => new GameMap
            {
                Id = e!.Id!,
                Name = e.Name!,
                Description = e.Description!,
                IconKey = e.IconKey,
                VersionAdded = e.VersionAdded!,
                VersionRemoved = e.VersionRemoved,
                GameEventId = e.GameEventId,
                GatherRates = e.GatherRates!.Select(rate => new GatherRate
                {
                    ItemId = rate!.ItemId!,
                    IsUnlimited = rate.IsUnlimited!.Value,
                    RatePerMinute = rate.RatePerMinute,
                }).ToList(),
            }).ToList(),
            Icons = document.Icons!.Select(e => new IconEntry
            {
                Key = e!.Key!,
                File = e.File!,
                Sha256 = e.Sha256!,
                Bytes = e.Bytes!.Value,
            }).ToList(),
        };

        MasterValidator.ValidateAll(
            result.Items,
            result.Facilities,
            result.Environments,
            result.GameEvents,
            result.Recipes,
            result.Maps,
            errors);

        return result;
    }

    private static void NormalizeElements<T>(
        List<T?>? entities,
        string collectionName,
        ICollection<string> normalizations,
        Action<T, string> normalize)
        where T : class
    {
        if (entities is null)
        {
            return;
        }

        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] is { } entity)
            {
                normalize(entity, $"{collectionName}[{i}]");
            }
        }
    }

    private static void NormalizeItemIds<T>(
        List<T?>? items,
        string location,
        ICollection<string> normalizations,
        Func<T, string?> get,
        Action<T, string?> set)
        where T : class
    {
        if (items is null)
        {
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is { } item)
            {
                set(item, TrimIdValue(get(item), $"{location}[{i}].ItemId", normalizations, nullable: false));
            }
        }
    }

    // 前後空白を除去する。除去が起きたときだけ記録し、nullable 指定で空になった値は null（未指定）にする。
    private static string? TrimIdValue(
        string? value,
        string location,
        ICollection<string> normalizations,
        bool nullable)
    {
        if (value is null)
        {
            return null;
        }

        string trimmed = value.Trim();
        if (nullable && trimmed.Length == 0)
        {
            // 空文字列は Domain 検証を素通りする一方 idValue（minLength 1）に反するため、
            // 「空白のみ→未指定」の拡張として null に揃える。空白除去がなかった空文字列は記録しない。
            if (trimmed != value)
            {
                normalizations.Add($"{location}: 「null」");
            }

            return null;
        }

        if (trimmed == value)
        {
            return value;
        }

        normalizations.Add($"{location}: 「{trimmed}」");
        return trimmed;
    }
}
