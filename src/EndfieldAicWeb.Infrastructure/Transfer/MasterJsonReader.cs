using System.Text.Json;
using System.Text.RegularExpressions;
using EndfieldAicWeb.Domain.Models;
using EndfieldAicWeb.Domain.Validation;
using Environment = EndfieldAicWeb.Domain.Models.Environment;

namespace EndfieldAicWeb.Infrastructure.Transfer;

/// <summary>
/// マスタ JSON の解析・構造検証・実体化・意味検証の共有経路。
/// 読み込み（<see cref="MasterJsonLoader"/>）とエクスポート時検証で同一規則を適用する。
/// 違反はすべて <see cref="MasterValidationError"/> へ集約し、例外は投げない。
/// </summary>
internal static class MasterJsonReader
{
    /// <summary>対応するスキーマ版。新系統の v1 = 1（仕様決定 C）。</summary>
    public const int SupportedSchemaVersion = 1;

    // 末尾改行を許さないよう ^$ ではなく \A\z で固定する。
    private static readonly Regex Sha256Pattern = new("\\A[0-9a-f]{64}\\z", RegexOptions.Compiled);

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
    /// 構造検証（SchemaVersion、DataVersion、必須配列、null 要素、必須フィールド、enum 値、
    /// Icons 節の構造）。ここを通過したドキュメントは <see cref="ToEntities"/> で安全に実体化できる。
    /// </summary>
    public static void ValidateStructure(MasterJsonDocument document, ICollection<MasterValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(errors);

        if (document.SchemaVersion is null)
        {
            errors.Add(new MasterValidationError(
                "Document", "", "SchemaVersion", "SchemaVersion は必須です。"));
        }
        else if (document.SchemaVersion != SupportedSchemaVersion)
        {
            errors.Add(new MasterValidationError(
                "Document", "", "SchemaVersion",
                $"未対応の SchemaVersion です: {document.SchemaVersion}（対応版: {SupportedSchemaVersion}）"));
        }

        if (string.IsNullOrWhiteSpace(document.DataVersion))
        {
            errors.Add(new MasterValidationError(
                "Document", "", "DataVersion", "DataVersion は必須です。"));
        }

        // スキーマ未定義プロパティ（additionalProperties:false 準拠）は拒否する。
        RejectUnknownProperties(document.ExtensionData, "ルート", "Document", "", errors);

        RequireArray(document.Items, nameof(document.Items), errors);
        RequireArray(document.Facilities, nameof(document.Facilities), errors);
        RequireArray(document.Environments, nameof(document.Environments), errors);
        RequireArray(document.GameEvents, nameof(document.GameEvents), errors);
        RequireArray(document.Recipes, nameof(document.Recipes), errors);
        RequireArray(document.Maps, nameof(document.Maps), errors);
        RequireArray(document.Icons, nameof(document.Icons), errors);

        ValidateEntityElements(document.Items, "Items", ValidateItemElement, errors);
        ValidateEntityElements(document.Facilities, "Facilities", ValidateFacilityElement, errors);
        ValidateEntityElements(document.Environments, "Environments", ValidateEnvironmentElement, errors);
        ValidateEntityElements(document.GameEvents, "GameEvents", ValidateGameEventElement, errors);
        ValidateEntityElements(document.Recipes, "Recipes", ValidateRecipeElement, errors);
        ValidateEntityElements(document.Maps, "Maps", ValidateGameMapElement, errors);
        ValidateIconElements(document.Icons, errors);
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

    /// <summary>
    /// Icons 節の構造規則を検査する。実ファイルとの照合（Bytes/Sha256 の一致）は
    /// <see cref="Icons.IconManifestVerifier"/> の責務であり、ここでは形式のみを見る。
    /// DTO（読み込み時）とエンティティ（エクスポート時検証）の両方から呼べるよう、
    /// 値の形へ正規化して検査する。
    /// </summary>
    public static void ValidateIconManifestValues(
        IEnumerable<(int Index, string? Key, string? File, string? Sha256, long? Bytes)> entries,
        ICollection<MasterValidationError> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((int index, string? key, string? file, string? sha256, long? bytes) in entries)
        {
            string location = $"Icons[{index}]";
            string entityId = key ?? "";
            bool keyValid = !string.IsNullOrWhiteSpace(key) && IconKeyRules.IsValid(key);

            if (string.IsNullOrWhiteSpace(key))
            {
                errors.Add(new MasterValidationError("Icons", "", "Key", $"{location}.Key は必須です。"));
            }
            else if (!IconKeyRules.IsValid(key))
            {
                errors.Add(new MasterValidationError(
                    "Icons", entityId, "Key",
                    $"{location}.Key は英数字・ハイフン・アンダースコアの 1〜64 文字で指定してください: {key}"));
            }
            else if (!seen.Add(key!))
            {
                errors.Add(new MasterValidationError(
                    "Icons", entityId, "Key", $"Icons に Key の重複があります: {key}"));
            }

            if (string.IsNullOrWhiteSpace(file))
            {
                errors.Add(new MasterValidationError("Icons", entityId, "File", $"{location}.File は必須です。"));
            }
            else if (keyValid && file != IconKeyRules.ManifestFile(key!))
            {
                errors.Add(new MasterValidationError(
                    "Icons", entityId, "File",
                    $"{location}.File は icons/<Key>.png 形式である必要があります: {file}"));
            }

            if (string.IsNullOrWhiteSpace(sha256))
            {
                errors.Add(new MasterValidationError("Icons", entityId, "Sha256", $"{location}.Sha256 は必須です。"));
            }
            else if (!Sha256Pattern.IsMatch(sha256))
            {
                errors.Add(new MasterValidationError(
                    "Icons", entityId, "Sha256",
                    $"{location}.Sha256 は 64 桁の 16 進数（小文字）である必要があります: {sha256}"));
            }

            if (bytes is null)
            {
                errors.Add(new MasterValidationError("Icons", entityId, "Bytes", $"{location}.Bytes は必須です。"));
            }
            else if (bytes < 1)
            {
                errors.Add(new MasterValidationError(
                    "Icons", entityId, "Bytes",
                    $"{location}.Bytes は 1 以上である必要があります: {bytes}"));
            }
        }
    }

    private static void ValidateItemElement(ItemJson? item, string location, ICollection<MasterValidationError> errors)
    {
        RequireField(item!.Id, $"{location}.Id", "Item", item.Id ?? "", errors);
        RequireField(item.Name, $"{location}.Name", "Item", item.Id ?? "", errors);
        RequireField(item.Category, $"{location}.Category", "Item", item.Id ?? "", errors);
        if (item.IsGatherable is null)
        {
            errors.Add(new MasterValidationError(
                "Item", item.Id ?? "", "IsGatherable", $"{location}.IsGatherable は必須です。"));
        }

        RequireEnum<TransportKind>(item.TransportKind, $"{location}.TransportKind", "Item", item.Id ?? "", errors);
        RequirePresent(item.Description, $"{location}.Description", "Item", item.Id ?? "", errors);
        RejectUnknownProperties(item.ExtensionData, location, "Item", item.Id ?? "", errors);
    }

    private static void ValidateFacilityElement(FacilityJson? facility, string location, ICollection<MasterValidationError> errors)
    {
        RequireField(facility!.Id, $"{location}.Id", "Facility", facility.Id ?? "", errors);
        RequireField(facility.Name, $"{location}.Name", "Facility", facility.Id ?? "", errors);
        RequireNumber(facility.Width, $"{location}.Width", "Facility", facility.Id ?? "", errors);
        RequireNumber(facility.Height, $"{location}.Height", "Facility", facility.Id ?? "", errors);
        RequireNumber(facility.PowerConsumption, $"{location}.PowerConsumption", "Facility", facility.Id ?? "", errors);
        RequirePresent(facility.Description, $"{location}.Description", "Facility", facility.Id ?? "", errors);
        RejectUnknownProperties(facility.ExtensionData, location, "Facility", facility.Id ?? "", errors);
    }

    private static void ValidateEnvironmentElement(EnvironmentJson? environment, string location, ICollection<MasterValidationError> errors)
    {
        RequireField(environment!.Id, $"{location}.Id", "Environment", environment.Id ?? "", errors);
        RequireField(environment.Name, $"{location}.Name", "Environment", environment.Id ?? "", errors);
        RequireField(environment.ProviderFacilityId, $"{location}.ProviderFacilityId", "Environment", environment.Id ?? "", errors);
        RequireField(environment.ConsumeItemId, $"{location}.ConsumeItemId", "Environment", environment.Id ?? "", errors);
        RequireNumber(environment.ConsumeRatePerMinute, $"{location}.ConsumeRatePerMinute", "Environment", environment.Id ?? "", errors);
        RequireNumber(environment.CoverableMachines, $"{location}.CoverableMachines", "Environment", environment.Id ?? "", errors);
        RequirePresent(environment.Description, $"{location}.Description", "Environment", environment.Id ?? "", errors);
        RejectUnknownProperties(environment.ExtensionData, location, "Environment", environment.Id ?? "", errors);
    }

    private static void ValidateGameEventElement(GameEventJson? gameEvent, string location, ICollection<MasterValidationError> errors)
    {
        RequireField(gameEvent!.Id, $"{location}.Id", "GameEvent", gameEvent.Id ?? "", errors);
        RequireField(gameEvent.Name, $"{location}.Name", "GameEvent", gameEvent.Id ?? "", errors);
        RequirePresent(gameEvent.Description, $"{location}.Description", "GameEvent", gameEvent.Id ?? "", errors);
        RejectUnknownProperties(gameEvent.ExtensionData, location, "GameEvent", gameEvent.Id ?? "", errors);
    }

    private static void ValidateRecipeElement(RecipeJson? recipe, string location, ICollection<MasterValidationError> errors)
    {
        string id = recipe!.Id ?? "";
        RequireField(recipe.Id, $"{location}.Id", "Recipe", id, errors);
        RequireField(recipe.Name, $"{location}.Name", "Recipe", id, errors);
        RequirePresent(recipe.Description, $"{location}.Description", "Recipe", id, errors);
        RejectUnknownProperties(recipe.ExtensionData, location, "Recipe", id, errors);

        if (recipe.Inputs is null)
        {
            errors.Add(new MasterValidationError("Recipe", id, "Inputs", $"{location}.Inputs は必須です。"));
        }
        else
        {
            for (int i = 0; i < recipe.Inputs.Count; i++)
            {
                RecipeIoJson? input = recipe.Inputs[i];
                string ioLocation = $"{location}.Inputs[{i}]";
                if (input is null)
                {
                    errors.Add(new MasterValidationError("Recipe", id, "Inputs", $"{ioLocation} が null です。"));
                    continue;
                }

                RequireField(input.ItemId, $"{ioLocation}.ItemId", "Recipe", id, errors);
                RequireNumber(input.Quantity, $"{ioLocation}.Quantity", "Recipe", id, errors);
                RejectUnknownProperties(input.ExtensionData, ioLocation, "Recipe", id, errors);
            }
        }

        if (recipe.Outputs is null)
        {
            errors.Add(new MasterValidationError("Recipe", id, "Outputs", $"{location}.Outputs は必須です。"));
        }
        else
        {
            for (int i = 0; i < recipe.Outputs.Count; i++)
            {
                RecipeOutputJson? output = recipe.Outputs[i];
                string ioLocation = $"{location}.Outputs[{i}]";
                if (output is null)
                {
                    errors.Add(new MasterValidationError("Recipe", id, "Outputs", $"{ioLocation} が null です。"));
                    continue;
                }

                RequireField(output.ItemId, $"{ioLocation}.ItemId", "Recipe", id, errors);
                RequireNumber(output.Quantity, $"{ioLocation}.Quantity", "Recipe", id, errors);
                RejectUnknownProperties(output.ExtensionData, ioLocation, "Recipe", id, errors);
                if (output.SortOrder is null)
                {
                    errors.Add(new MasterValidationError("Recipe", id, "Outputs", $"{ioLocation}.SortOrder は必須です。"));
                }
                else if (output.SortOrder < 0)
                {
                    errors.Add(new MasterValidationError(
                        "Recipe", id, "Outputs",
                        $"{ioLocation}.SortOrder は 0 以上である必要があります: {output.SortOrder}"));
                }
            }
        }

        if (recipe.Facilities is null)
        {
            errors.Add(new MasterValidationError("Recipe", id, "Facilities", $"{location}.Facilities は必須です。"));
        }
        else
        {
            for (int i = 0; i < recipe.Facilities.Count; i++)
            {
                RecipeFacilityJson? pair = recipe.Facilities[i];
                string pairLocation = $"{location}.Facilities[{i}]";
                if (pair is null)
                {
                    errors.Add(new MasterValidationError("Recipe", id, "Facilities", $"{pairLocation} が null です。"));
                    continue;
                }

                RequireField(pair.FacilityId, $"{pairLocation}.FacilityId", "Recipe", id, errors);
                RequireNumber(pair.CycleTime, $"{pairLocation}.CycleTime", "Recipe", id, errors);
                RejectUnknownProperties(pair.ExtensionData, pairLocation, "Recipe", id, errors);

                if (pair.FixedConsumption is FixedConsumptionJson fixedConsumption)
                {
                    RequireField(
                        fixedConsumption.ItemId, $"{pairLocation}.FixedConsumption.ItemId", "Recipe", id, errors);
                    RequireNumber(
                        fixedConsumption.RatePerMinute,
                        $"{pairLocation}.FixedConsumption.RatePerMinute",
                        "Recipe",
                        id,
                        errors);
                    RejectUnknownProperties(
                        fixedConsumption.ExtensionData, $"{pairLocation}.FixedConsumption", "Recipe", id, errors);
                }
            }
        }
    }

    private static void ValidateGameMapElement(GameMapJson? map, string location, ICollection<MasterValidationError> errors)
    {
        string id = map!.Id ?? "";
        RequireField(map.Id, $"{location}.Id", "GameMap", id, errors);
        RequireField(map.Name, $"{location}.Name", "GameMap", id, errors);
        RequirePresent(map.Description, $"{location}.Description", "GameMap", id, errors);
        RejectUnknownProperties(map.ExtensionData, location, "GameMap", id, errors);

        if (map.GatherRates is null)
        {
            errors.Add(new MasterValidationError("GameMap", id, "GatherRates", $"{location}.GatherRates は必須です。"));
            return;
        }

        for (int i = 0; i < map.GatherRates.Count; i++)
        {
            GatherRateJson? rate = map.GatherRates[i];
            string rateLocation = $"{location}.GatherRates[{i}]";
            if (rate is null)
            {
                errors.Add(new MasterValidationError(
                    "GameMap", id, "GatherRates", $"{rateLocation} が null です。"));
                continue;
            }

            RequireField(rate.ItemId, $"{rateLocation}.ItemId", "GameMap", id, errors);
            if (rate.IsUnlimited is null)
            {
                errors.Add(new MasterValidationError(
                    "GameMap", id, "GatherRates",
                    $"{rateLocation}.IsUnlimited は必須です。"));
            }

            RejectUnknownProperties(rate.ExtensionData, rateLocation, "GameMap", id, errors);
        }
    }

    private static void ValidateIconElements(List<IconJson?>? icons, ICollection<MasterValidationError> errors)
    {
        if (icons is null)
        {
            return;
        }

        for (int i = 0; i < icons.Count; i++)
        {
            if (icons[i] is null)
            {
                errors.Add(new MasterValidationError("Icons", "", "", $"配列 Icons の {i} 番目の要素が null です。"));
                continue;
            }

            RejectUnknownProperties(icons[i]!.ExtensionData, $"Icons[{i}]", "Icons", icons[i]!.Key ?? "", errors);
        }

        ValidateIconManifestValues(
            icons.Select((e, i) => (Element: e, Index: i))
                .Where(x => x.Element is not null)
                .Select(x => (x.Index, x.Element!.Key, x.Element.File, x.Element.Sha256, x.Element.Bytes)),
            errors);
    }

    private static void ValidateEntityElements<T>(
        List<T?>? entities,
        string collectionName,
        Action<T?, string, ICollection<MasterValidationError>> validateElement,
        ICollection<MasterValidationError> errors)
        where T : EntityJson
    {
        if (entities is null)
        {
            return;
        }

        for (int i = 0; i < entities.Count; i++)
        {
            T? entity = entities[i];
            string location = $"{collectionName}[{i}]";
            if (entity is null)
            {
                errors.Add(new MasterValidationError(
                    collectionName, "", "", $"配列 {collectionName} の {i} 番目の要素が null です。"));
                continue;
            }

            validateElement(entity, location, errors);
        }
    }

    private static void RejectUnknownProperties(
        IDictionary<string, JsonElement>? extensionData,
        string location,
        string entityKind,
        string entityId,
        ICollection<MasterValidationError> errors)
    {
        if (extensionData is null)
        {
            return;
        }

        foreach (string key in extensionData.Keys)
        {
            errors.Add(new MasterValidationError(
                entityKind, entityId, key, $"{location} にスキーマ未定義のプロパティ {key} があります。"));
        }
    }

    private static void RequireArray<T>(List<T>? array, string name, ICollection<MasterValidationError> errors)
    {
        if (array is null)
        {
            errors.Add(new MasterValidationError(
                "Document", "", name, $"必須配列 {name} がありません。"));
        }
    }

    private static void RequireField(
        string? value,
        string location,
        string entityKind,
        string entityId,
        ICollection<MasterValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            string field = location[(location.IndexOf('.') + 1)..];
            errors.Add(new MasterValidationError(entityKind, entityId, field, $"{location} は必須です。"));
        }
    }

    private static void RequireNumber<T>(
        T? value,
        string location,
        string entityKind,
        string entityId,
        ICollection<MasterValidationError> errors)
        where T : struct
    {
        if (value is null)
        {
            string field = location[(location.IndexOf('.') + 1)..];
            errors.Add(new MasterValidationError(entityKind, entityId, field, $"{location} は必須です。"));
        }
    }

    // スキーマ type=string の必須フィールド。空文字は許容し、null のみ拒否する。
    private static void RequirePresent(
        string? value,
        string location,
        string entityKind,
        string entityId,
        ICollection<MasterValidationError> errors)
    {
        if (value is null)
        {
            string field = location[(location.IndexOf('.') + 1)..];
            errors.Add(new MasterValidationError(entityKind, entityId, field, $"{location} は必須です。"));
        }
    }

    private static void RequireEnum<TEnum>(
        string? value,
        string location,
        string entityKind,
        string entityId,
        ICollection<MasterValidationError> errors)
        where TEnum : struct, Enum
    {
        // TryParse は数値文字列・前後空白も受け付けるため、定義名との完全一致を要求する。
        bool valid = Enum.TryParse<TEnum>(value, out TEnum parsed)
            && Enum.IsDefined(parsed)
            && string.Equals(parsed.ToString(), value, StringComparison.Ordinal);
        if (!valid)
        {
            string field = location[(location.IndexOf('.') + 1)..];
            errors.Add(new MasterValidationError(
                entityKind, entityId, field, $"{location} の値が不正です: {value}"));
        }
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
