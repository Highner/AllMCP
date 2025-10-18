using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Attributes;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("create_bottle", "Imports bottles for existing wines after validating the wine metadata.")]
public sealed class CreateBottleTool : BottleToolBase
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string[] _colorOptions = Enum.GetNames(typeof(WineColor));

    public CreateBottleTool(
        IBottleRepository bottleRepository,
        IWineRepository wineRepository,
        ICountryRepository countryRepository,
        IRegionRepository regionRepository,
        IAppellationRepository appellationRepository,
        IWineVintageRepository wineVintageRepository)
        : base(bottleRepository, wineRepository, countryRepository, regionRepository, appellationRepository, wineVintageRepository)
    {
    }

    public override string Name => "create_bottle";
    public override string Description => "Imports bottles for existing wines after validating the wine metadata.";
    public override string Title => "Import Bottles";
    protected override string InvokingMessage => "Importing bottles…";
    protected override string InvokedMessage => "Bottle import complete.";

    protected override async Task<BottleOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        try
        {
            var requests = ExtractBottleRequests(parameters);
            if (requests.Count == 0)
            {
                return Failure(
                    "batch_create",
                    "No bottles were provided for import.",
                    new[] { "Provide at least one bottle in the 'bottles' array." });
            }

            var successes = new List<object>();
            var failures = new List<object>();

            for (var index = 0; index < requests.Count; index++)
            {
                var normalized = ToParameterDictionary(requests[index]);
                var result = await ProcessBottleAsync(normalized, ct);

                if (result.Success)
                {
                    successes.Add(new
                    {
                        index,
                        message = result.Message,
                        bottle = result.Bottle is null ? null : BottleResponseMapper.MapBottle(result.Bottle)
                    });
                }
                else
                {
                    failures.Add(new
                    {
                        index,
                        message = result.Message,
                        errors = result.Errors,
                        suggestions = result.Suggestions,
                        exception = result.Exception is null
                            ? null
                            : new
                            {
                                message = result.Exception.Message,
                                stackTrace = result.Exception.StackTrace
                            }
                    });
                }
            }

            if (successes.Count == 0)
            {
                return Failure(
                    "batch_create",
                    "All bottle imports failed.",
                    new[] { "None of the provided bottles could be imported." },
                    new
                    {
                        failures
                    });
            }

            var summaryMessage = failures.Count == 0
                ? $"Imported {successes.Count} bottle(s) successfully."
                : $"Imported {successes.Count} bottle(s) successfully. {failures.Count} failed.";

            return Success(
                "batch_create",
                summaryMessage,
                new
                {
                    processed = requests.Count,
                    successful = successes.Count,
                    failed = failures.Count,
                    successes,
                    failures
                });
        }
        catch (Exception ex)
        {
            return Failure(
                "batch_create",
                ex.Message,
                new[] { ex.Message },
                new
                {
                    type = "exception",
                    stackTrace = ex.StackTrace
                },
                ex);
        }
    }

    protected override JsonObject BuildInputSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["bottles"] = new JsonObject
                {
                    ["type"] = "array",
                    ["description"] = "Collection of bottles to import. Each entry must include at least a name and a vintage.",
                    ["items"] = BuildBottleItemSchema()
                }
            },
            ["required"] = new JsonArray("bottles")
        };
    }

    private JsonObject BuildBottleItemSchema()
    {
        var properties = new JsonObject
        {
            ["name"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Name of the wine the bottle belongs to.",
            },
            ["vintage"] = new JsonObject
            {
                ["type"] = "integer",
                ["description"] = "Vintage year for the bottle.",
            },
            ["country"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Country name of the wine. Required when the wine must be created.",
            },
            ["region"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Region of the wine (such as Bordeaux, Burgundy, or California). Required when the wine must be created.",
            },
            ["appellation"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Appellation of the wine (such as Napa Valley, Pomerol or Chambolle-Musigny). Required when the wine must be created.",
            },
            ["color"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Wine color. Required when the wine must be created. Valid options: " + string.Join(", ", _colorOptions)
            },
            ["price"] = new JsonObject
            {
                ["type"] = "number",
                ["description"] = "Optional bottle price.",
            },
            ["score"] = new JsonObject
            {
                ["type"] = "number",
                ["description"] = "Optional rating or score.",
            },
            ["tastingNote"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional tasting notes.",
            },
            ["tasting_note"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Snake_case alias for tastingNote.",
            },
            ["isDrunk"] = new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "Indicates whether the bottle has been consumed.",
            },
            ["is_drunk"] = new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "Snake_case alias for isDrunk.",
            },
            ["drunkAt"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "date-time",
                ["description"] = "Timestamp of when the bottle was consumed (requires isDrunk=true).",
            },
            ["drunk_at"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "date-time",
                ["description"] = "Snake_case alias for drunkAt.",
            }
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray("name", "vintage", "country", "region", "appellation", "color")
        };
    }

    private async Task<BottleProcessingResult> ProcessBottleAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        try
        {
            var errors = new List<string>();

            var name = ParameterHelpers.GetStringParameter(parameters, "name", "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add("'name' is required.");
            }

            var vintage = ParameterHelpers.GetIntParameter(parameters, "vintage", "vintage");
            if (vintage is null)
            {
                errors.Add("'vintage' is required and must be a valid year.");
            }

            var price = ParameterHelpers.GetDecimalParameter(parameters, "price", "price");
            var score = ParameterHelpers.GetDecimalParameter(parameters, "score", "score");
            var tastingNote = ParameterHelpers.GetStringParameter(parameters, "tastingNote", "tasting_note")
                ?? ParameterHelpers.GetStringParameter(parameters, "tastingNotes", "tasting_notes");
            var isDrunk = ParameterHelpers.GetBoolParameter(parameters, "isDrunk", "is_drunk");
            var drunkAt = ParameterHelpers.GetDateTimeParameter(parameters, "drunkAt", "drunk_at");

            if (drunkAt.HasValue && isDrunk != true)
            {
                errors.Add("'drunkAt' can only be provided when 'isDrunk' is true.");
            }

            var colorInput = ParameterHelpers.GetStringParameter(parameters, "color", "color");
            WineColor? color = null;
            if (!string.IsNullOrWhiteSpace(colorInput))
            {
                if (Enum.TryParse<WineColor>(colorInput, true, out var parsedColor))
                {
                    color = parsedColor;
                }
                else
                {
                    return BottleProcessingResult.CreateFailure(
                        $"Color '{colorInput}' is not recognised.",
                        new[] { $"Color '{colorInput}' is not recognised." },
                        new
                        {
                            type = "color",
                            query = colorInput,
                            suggestions = _colorOptions
                        });
                }
            }

            var countryName = ParameterHelpers.GetStringParameter(parameters, "country", "country");
            var regionName = ParameterHelpers.GetStringParameter(parameters, "region", "region");
            var appellationName = ParameterHelpers.GetStringParameter(parameters, "appellation", "appellation");

            var trimmedCountryName = string.IsNullOrWhiteSpace(countryName) ? null : countryName.Trim();
            var trimmedRegionName = string.IsNullOrWhiteSpace(regionName) ? null : regionName.Trim();
            var normalizedAppellation = string.IsNullOrWhiteSpace(appellationName)
                ? null
                : appellationName.Trim();

            if (errors.Count > 0)
            {
                return BottleProcessingResult.CreateFailure("Validation failed.", errors);
            }

            Country? country = null;
            if (!string.IsNullOrWhiteSpace(trimmedCountryName))
            {
                country = await CountryRepository.GetOrCreateAsync(trimmedCountryName!, ct);
            }

            Region? region = null;
            if (!string.IsNullOrWhiteSpace(trimmedRegionName))
            {
                var existingRegion = await RegionRepository.FindByNameAsync(trimmedRegionName!, ct);
                if (existingRegion is not null)
                {
                    if (country is not null && existingRegion.CountryId != country.Id)
                    {
                        return BottleProcessingResult.CreateFailure(
                            $"Region '{existingRegion.Name}' belongs to country '{existingRegion.Country?.Name ?? "unknown"}'.",
                            new[] { $"Region '{existingRegion.Name}' belongs to country '{existingRegion.Country?.Name ?? "unknown"}'." },
                            new
                            {
                                type = "region_country_mismatch",
                                requestedCountry = new { name = country.Name, id = country.Id },
                                regionCountry = existingRegion.Country is null
                                    ? null
                                    : new { name = existingRegion.Country.Name, id = existingRegion.Country.Id }
                            });
                    }

                    region = existingRegion;
                }
                else if (country is null)
                {
                    return BottleProcessingResult.CreateFailure(
                        $"Region '{trimmedRegionName}' was not found. Provide a country so it can be created automatically.",
                        new[] { "Country is required to create a new region." },
                        new
                        {
                            type = "region_creation_missing_country",
                            query = trimmedRegionName
                        });
                }
                else
                {
                    region = await RegionRepository.GetOrCreateAsync(trimmedRegionName!, country, ct);
                }
            }

            if (region is not null)
            {
                country ??= region.Country;
            }

            var wine = await WineRepository.FindByNameAsync(name!, normalizedAppellation, ct);
            if (wine is null)
            {
                if (!color.HasValue)
                {
                    return BottleProcessingResult.CreateFailure(
                        $"Wine '{name}' does not exist. Provide a color so it can be created automatically.",
                        new[] { "Color is required to create a new wine." },
                        new
                        {
                            type = "wine_creation_missing_color",
                            query = name,
                            suggestions = _colorOptions
                        });
                }

                if (region is null)
                {
                    return BottleProcessingResult.CreateFailure(
                        $"Wine '{name}' does not exist. Provide a region so it can be created automatically.",
                        new[] { "Region is required to create a new wine." },
                        new
                        {
                            type = "wine_creation_missing_region",
                            query = name
                        });
                }

                country ??= region.Country;

                if (string.IsNullOrWhiteSpace(normalizedAppellation))
                {
                    return BottleProcessingResult.CreateFailure(
                        $"Wine '{name}' does not exist. Provide an appellation so it can be created automatically.",
                        new[] { "Appellation is required to create a new wine." },
                        new
                        {
                            type = "wine_creation_missing_appellation",
                            query = name
                        });
                }

                var appellation = await AppellationRepository.GetOrCreateAsync(normalizedAppellation!, region.Id, ct);

                var newWine = new Wine
                {
                    Id = Guid.NewGuid(),
                    Name = name!.Trim(),
                    GrapeVariety = string.Empty,
                    Color = color.Value,
                    AppellationId = appellation.Id,
                    Appellation = appellation
                };

                await WineRepository.AddAsync(newWine, ct);
                wine = await WineRepository.GetByIdAsync(newWine.Id, ct) ?? newWine;
            }

            var wineAppellation = wine.Appellation;
            var wineRegion = wineAppellation?.Region;
            var wineCountry = wineRegion?.Country;

            if (!string.IsNullOrWhiteSpace(normalizedAppellation)
                && !string.Equals(wineAppellation?.Name, normalizedAppellation, StringComparison.OrdinalIgnoreCase))
            {
                var recordedAppellation = string.IsNullOrWhiteSpace(wineAppellation?.Name) ? "unknown" : wineAppellation!.Name;
                return BottleProcessingResult.CreateFailure(
                    $"Wine '{wine.Name}' is recorded for appellation '{recordedAppellation}'.",
                    new[] { $"Wine '{wine.Name}' is recorded for appellation '{recordedAppellation}'." },
                    new
                    {
                        type = "wine_appellation_mismatch",
                        requested = normalizedAppellation,
                        actual = string.IsNullOrWhiteSpace(wineAppellation?.Name) ? null : wineAppellation.Name
                    });
            }

            if (color.HasValue && wine.Color != color)
            {
                return BottleProcessingResult.CreateFailure(
                    $"Wine '{wine.Name}' exists with color '{wine.Color}'.",
                    new[] { $"Wine '{wine.Name}' exists with color '{wine.Color}'." },
                    new
                    {
                        type = "wine_color_mismatch",
                        requested = color.Value.ToString(),
                        actual = wine.Color.ToString()
                    });
            }

            if (country is not null && wineCountry?.Id != country.Id)
            {
                return BottleProcessingResult.CreateFailure(
                    $"Wine '{wine.Name}' is recorded for country '{wineCountry?.Name ?? "unknown"}'.",
                    new[] { $"Wine '{wine.Name}' is recorded for country '{wineCountry?.Name ?? "unknown"}'." },
                    new
                    {
                        type = "wine_country_mismatch",
                        requested = new { name = country.Name, id = country.Id },
                        actual = wineCountry is null ? null : new { name = wineCountry.Name, id = wineCountry.Id }
                    });
            }

            if (region is not null && wineRegion?.Id != region.Id)
            {
                var recordedRegion = wineRegion?.Name ?? "unknown";
                return BottleProcessingResult.CreateFailure(
                    $"Wine '{wine.Name}' is recorded for region '{recordedRegion}'.",
                    new[] { $"Wine '{wine.Name}' is recorded for region '{recordedRegion}'." },
                    new
                    {
                        type = "wine_region_mismatch",
                        requested = new { name = region.Name, id = region.Id },
                        actual = wineRegion is null ? null : new { name = wineRegion.Name, id = wineRegion.Id }
                    });
            }

            var hasBeenDrunk = isDrunk ?? false;
            DateTime? resolvedDrunkAt = null;
            if (hasBeenDrunk)
            {
                if (drunkAt.HasValue)
                {
                    resolvedDrunkAt = drunkAt.Value.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(drunkAt.Value, DateTimeKind.Utc)
                        : drunkAt.Value.ToUniversalTime();
                }
                else
                {
                    resolvedDrunkAt = DateTime.UtcNow;
                }
            }

            var wineVintage = await WineVintageRepository.GetOrCreateAsync(wine.Id, vintage!.Value, ct);

            var bottle = new Bottle
            {
                Id = Guid.NewGuid(),
                WineVintageId = wineVintage.Id,
                Price = price,
                Score = score,
                TastingNote = tastingNote?.Trim() ?? string.Empty,
                IsDrunk = hasBeenDrunk,
                DrunkAt = resolvedDrunkAt
            };

            await BottleRepository.AddAsync(bottle, ct);

            var created = await BottleRepository.GetByIdAsync(bottle.Id, ct) ?? new Bottle
            {
                Id = bottle.Id,
                WineVintageId = bottle.WineVintageId,
                Price = bottle.Price,
                Score = bottle.Score,
                TastingNote = bottle.TastingNote,
                IsDrunk = bottle.IsDrunk,
                DrunkAt = bottle.DrunkAt,
                WineVintage = wineVintage
            };

            return BottleProcessingResult.CreateSuccess("Bottle created successfully.", created);
        }
        catch (Exception ex)
        {
            return BottleProcessingResult.CreateFailure(
                "An unexpected error occurred while importing the bottle.",
                new[] { ex.Message },
                new { type = "exception" },
                ex);
        }
    }

    private IReadOnlyList<Dictionary<string, object?>> ExtractBottleRequests(Dictionary<string, object>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return Array.Empty<Dictionary<string, object?>>();
        }

        var requests = new List<Dictionary<string, object?>>();

        if (TryGetParameter(parameters, "bottles", out var rawBottles))
        {
            requests.AddRange(ConvertToRequestList(rawBottles));
        }

        if (requests.Count == 0 && TryGetParameter(parameters, "items", out var rawItems))
        {
            requests.AddRange(ConvertToRequestList(rawItems));
        }

        if (requests.Count == 0 && TryGetParameter(parameters, "records", out var rawRecords))
        {
            requests.AddRange(ConvertToRequestList(rawRecords));
        }

        if (requests.Count == 0 && ContainsBottleLikeFields(parameters))
        {
            requests.Add(CloneParameters(parameters));
        }

        return requests;
    }

    private static bool TryGetParameter(Dictionary<string, object> parameters, string key, out object? value)
    {
        if (parameters.TryGetValue(key, out value))
        {
            return true;
        }

        var snakeCase = ParameterHelpers.ToSnakeCase(key);
        if (!string.Equals(snakeCase, key, StringComparison.OrdinalIgnoreCase)
            && parameters.TryGetValue(snakeCase, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    private static bool ContainsBottleLikeFields(Dictionary<string, object> parameters)
    {
        return parameters.ContainsKey("name")
            || parameters.ContainsKey("vintage")
            || parameters.ContainsKey("color")
            || parameters.ContainsKey("region")
            || parameters.ContainsKey("country");
    }

    private static Dictionary<string, object?> CloneParameters(Dictionary<string, object> parameters)
    {
        var clone = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in parameters)
        {
            clone[kvp.Key] = kvp.Value;
        }

        return clone;
    }

    private static IEnumerable<Dictionary<string, object?>> ConvertToRequestList(object? raw)
    {
        if (raw is null)
        {
            yield break;
        }

        switch (raw)
        {
            case JsonElement element:
                foreach (var entry in ConvertJsonElementToRequests(element))
                {
                    yield return entry;
                }

                break;
            case JsonArray jsonArray:
                foreach (var entry in ConvertJsonArray(jsonArray))
                {
                    yield return entry;
                }

                break;
            case IDictionary dictionary:
                yield return DictionaryFromDictionary(dictionary);
                break;
            case IEnumerable enumerable when raw is not string:
                foreach (var item in enumerable)
                {
                    foreach (var entry in ConvertToRequestList(item))
                    {
                        yield return entry;
                    }
                }

                break;
        }
    }

    private static IEnumerable<Dictionary<string, object?>> ConvertJsonElementToRequests(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            Dictionary<string, object?>[]? items = null;
            try
            {
                items = JsonSerializer.Deserialize<Dictionary<string, object?>[]?>(element.GetRawText(), SerializerOptions);
            }
            catch
            {
                items = null;
            }

            if (items is not null)
            {
                foreach (var item in items)
                {
                    yield return CreateCaseInsensitiveDictionary(item);
                }
            }

            yield break;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return JsonElementToDictionary(element);
        }
    }

    private static IEnumerable<Dictionary<string, object?>> ConvertJsonArray(JsonArray array)
    {
        Dictionary<string, object?>[]? items = null;
        try
        {
            items = JsonSerializer.Deserialize<Dictionary<string, object?>[]?>(array.ToJsonString(), SerializerOptions);
        }
        catch
        {
            items = null;
        }

        if (items is null)
        {
            yield break;
        }

        foreach (var item in items)
        {
            yield return CreateCaseInsensitiveDictionary(item);
        }
    }

    private static Dictionary<string, object?> DictionaryFromDictionary(IDictionary dictionary)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is string key)
            {
                dict[key] = entry.Value;
            }
        }

        return dict;
    }

    private static Dictionary<string, object?> JsonElementToDictionary(JsonElement element)
    {
        try
        {
            var deserialized = JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText(), SerializerOptions)
                ?? new Dictionary<string, object?>();
            return CreateCaseInsensitiveDictionary(deserialized);
        }
        catch
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, object?> CreateCaseInsensitiveDictionary(Dictionary<string, object?> source)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in source)
        {
            dict[kvp.Key] = kvp.Value;
        }

        return dict;
    }

    private static Dictionary<string, object> ToParameterDictionary(Dictionary<string, object?> source)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in source)
        {
            dict[kvp.Key] = kvp.Value!;
        }

        return dict;
    }

    private sealed record BottleProcessingResult : IProcessingResult
    {
        private BottleProcessingResult()
        {
        }

        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public Bottle? Bottle { get; init; }
        public IReadOnlyList<string>? Errors { get; init; }
        public object? Suggestions { get; init; }
        public Exception? Exception { get; init; }

        public static BottleProcessingResult CreateSuccess(string message, Bottle bottle)
            => new()
            {
                Success = true,
                Message = message,
                Bottle = bottle
            };

        public static BottleProcessingResult CreateFailure(string message, IReadOnlyList<string>? errors = null, object? suggestions = null, Exception? exception = null)
            => new()
            {
                Success = false,
                Message = message,
                Errors = errors,
                Suggestions = suggestions,
                Exception = exception
            };
    }
}
