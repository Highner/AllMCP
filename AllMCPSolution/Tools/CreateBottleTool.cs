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
    private readonly InventoryIntakeService _inventoryIntakeService;

    public CreateBottleTool(
        IBottleRepository bottleRepository,
        IWineRepository wineRepository,
        ICountryRepository countryRepository,
        IRegionRepository regionRepository,
        IAppellationRepository appellationRepository,
        IWineVintageRepository wineVintageRepository,
        ITastingNoteRepository tastingNoteRepository,
        InventoryIntakeService inventoryIntakeService)
        : base(bottleRepository, wineRepository, countryRepository, regionRepository, appellationRepository, wineVintageRepository, tastingNoteRepository)
    {
        _inventoryIntakeService = inventoryIntakeService;
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
                var request = requests[index];
                var result = await _inventoryIntakeService.CreateBottleAsync(request, ct);

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
            ["userId"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "uuid",
                ["description"] = "User identifier required when providing tasting notes or scores when the name is not provided.",
            },
            ["user_id"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "uuid",
                ["description"] = "Snake_case alias for userId.",
            },
            ["userName"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional user name when the identifier is unknown.",
            },
            ["user_name"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Snake_case alias for userName.",
            },
            ["score"] = new JsonObject
            {
                ["type"] = "number",
                ["description"] = "Optional rating or score (requires userId or userName).",
            },
            ["tastingNote"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional tasting notes (requires userId or userName).",
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

}
