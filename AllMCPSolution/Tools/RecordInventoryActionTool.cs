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
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("record_inventory_action", "Records wine inventory actions like adding bottles or tasting notes.")]
public sealed class RecordInventoryActionTool : CrudToolBase
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly InventoryIntakeService _inventoryIntakeService;
    private readonly string[] _colorOptions = Enum.GetNames(typeof(WineColor));

    public RecordInventoryActionTool(InventoryIntakeService inventoryIntakeService)
    {
        _inventoryIntakeService = inventoryIntakeService;
    }

    public override string Name => "record_inventory_action";
    public override string Description => "Records inventory actions such as adding bottles and tasting notes.";
    public override string Title => "Record Inventory Action";
    protected override string InvokingMessage => "Recording inventory action…";
    protected override string InvokedMessage => "Inventory action completed.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var normalized = NormalizeParameters(parameters);

        if (normalized.TryGetValue("actions", out var rawActions) && rawActions is not null)
        {
            var actionBatch = ConvertToDictionaryList(rawActions).ToList();

            if (actionBatch.Count == 0)
            {
                return Failure(
                    "inventory",
                    "No valid actions were provided in the 'actions' array.",
                    new[] { "Provide at least one action object with an 'action' field." });
            }

            return await ProcessBatchAsync(actionBatch, ct);
        }

        return await ProcessSingleActionAsync(normalized, ct);
    }

    private async Task<CrudOperationResult> ProcessSingleActionAsync(Dictionary<string, object?> normalized, CancellationToken ct)
    {
        var action = GetString(normalized, "action");
        if (string.IsNullOrWhiteSpace(action))
        {
            return Failure("inventory", "Action is required.", new[] { "'action' is required." });
        }

        return await ExecuteActionCoreAsync(action, normalized, ct);
    }

    private async Task<CrudOperationResult> ProcessBatchAsync(IReadOnlyList<Dictionary<string, object?>> actions, CancellationToken ct)
    {
        var results = new List<object>(actions.Count);
        var successCount = 0;

        for (var index = 0; index < actions.Count; index++)
        {
            ct.ThrowIfCancellationRequested();

            var actionParameters = actions[index];
            var action = GetString(actionParameters, "action");
            var trimmedAction = action?.Trim();

            CrudOperationResult actionResult;
            if (string.IsNullOrWhiteSpace(trimmedAction))
            {
                actionResult = Failure(
                    "inventory",
                    $"Action at index {index} is missing 'action'.",
                    new[] { "'action' is required." });
            }
            else
            {
                actionResult = await ExecuteActionCoreAsync(trimmedAction!, actionParameters, ct);
                if (actionResult.Success)
                {
                    successCount++;
                }
            }

            results.Add(new
            {
                index,
                action = trimmedAction,
                actionResult.Success,
                actionResult.Message,
                actionResult.Data,
                actionResult.Errors,
                actionResult.Suggestions,
                actionResult.ExceptionMessage,
                actionResult.ExceptionStackTrace
            });
        }

        var failureCount = actions.Count - successCount;
        var message = failureCount == 0
            ? $"Processed {actions.Count} inventory action(s)."
            : $"Processed {actions.Count} inventory action(s) with {failureCount} failure(s).";

        var data = new
        {
            total = actions.Count,
            succeeded = successCount,
            failed = failureCount,
            results
        };

        if (failureCount == 0)
        {
            return Success("inventory", message, data);
        }

        var errors = new List<string> { $"{failureCount} of {actions.Count} actions failed." };
        return Failure("inventory", message, errors, suggestions: null, exception: null, data: data);
    }

    private async Task<CrudOperationResult> ExecuteActionCoreAsync(string action, Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var trimmedAction = action.Trim();
        var actionKey = trimmedAction.ToLowerInvariant();

        switch (actionKey)
        {
            case "add_bottle":
            case "add_bottle_with_note":
            {
                var bottleParameters = ExtractBottlePayload(parameters);
                if (bottleParameters.Count == 0)
                {
                    return Failure("inventory", "Bottle payload is required.", new[] { "Provide bottle details under 'bottle' or alongside the action." });
                }

                var result = await _inventoryIntakeService.CreateBottleAsync(bottleParameters, ct);
                if (!result.Success)
                {
                    return Failure("inventory", result.Message, result.Errors, result.Suggestions, result.Exception);
                }

                var data = new
                {
                    action = trimmedAction,
                    bottle = result.Bottle is null ? null : BottleResponseMapper.MapBottle(result.Bottle),
                    tastingNote = result.TastingNote is null ? null : TastingNoteResponseMapper.MapTastingNote(result.TastingNote)
                };

                return Success("inventory", result.Message, data);
            }

            case "add_tasting_note":
            {
                var tastingNoteParameters = ExtractTastingNotePayload(parameters);
                if (tastingNoteParameters.Count == 0)
                {
                    return Failure("inventory", "Tasting note payload is required.", new[] { "Provide tasting note details under 'tastingNote' or alongside the action." });
                }

                var result = await _inventoryIntakeService.CreateTastingNoteAsync(tastingNoteParameters, ct);
                if (!result.Success)
                {
                    return Failure("inventory", result.Message, result.Errors, result.Suggestions, result.Exception);
                }

                var data = new
                {
                    action = trimmedAction,
                    tastingNote = result.TastingNote is null ? null : TastingNoteResponseMapper.MapTastingNote(result.TastingNote)
                };

                return Success("inventory", result.Message, data);
            }

            default:
                return Failure("inventory", $"Unsupported action '{trimmedAction}'.", new[] { "Valid actions: add_bottle, add_bottle_with_note, add_tasting_note." });
        }
    }

    protected override JsonObject BuildInputSchema()
    {
        var properties = CreateActionProperties();

        properties["actions"] = new JsonObject
        {
            ["type"] = "array",
            ["description"] = "Batch of inventory actions to process sequentially.",
            ["minItems"] = 1,
            ["items"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = CreateActionProperties(),
                ["required"] = new JsonArray("action")
            }
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["anyOf"] = new JsonArray
            {
                new JsonObject { ["required"] = new JsonArray("action") },
                new JsonObject { ["required"] = new JsonArray("actions") }
            }
        };
    }

    private JsonObject CreateActionProperties()
    {
        return new JsonObject
        {
            ["action"] = BuildActionProperty(),
            ["bottle"] = BuildBottleSchema(),
            ["bottle_payload"] = BuildBottleSchema(),
            ["payload"] = BuildBottleSchema(),
            ["tastingNote"] = BuildTastingNoteSchema(),
            ["tasting_note"] = BuildTastingNoteSchema()
        };
    }

    private static JsonObject BuildActionProperty()
    {
        return new JsonObject
        {
            ["type"] = "string",
            ["enum"] = new JsonArray("add_bottle", "add_bottle_with_note", "add_tasting_note"),
            ["description"] = "Inventory action to perform."
        };
    }

    private Dictionary<string, object?> ExtractBottlePayload(Dictionary<string, object?> parameters)
    {
        if (TryGetDictionary(parameters, "bottle", out var payload))
        {
            return payload;
        }

        if (TryGetDictionary(parameters, "bottle_payload", out payload))
        {
            return payload;
        }

        if (TryGetDictionary(parameters, "payload", out payload))
        {
            return payload;
        }

        var fallback = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in parameters)
        {
            if (string.Equals(kvp.Key, "action", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kvp.Key, "tastingNote", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kvp.Key, "tasting_note", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            fallback[kvp.Key] = kvp.Value;
        }

        return fallback;
    }

    private Dictionary<string, object?> ExtractTastingNotePayload(Dictionary<string, object?> parameters)
    {
        if (TryGetDictionary(parameters, "tastingNote", out var payload))
        {
            return payload;
        }

        if (TryGetDictionary(parameters, "tasting_note", out payload))
        {
            return payload;
        }

        var fallback = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in parameters)
        {
            if (string.Equals(kvp.Key, "action", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kvp.Key, "bottle", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kvp.Key, "bottle_payload", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kvp.Key, "payload", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            fallback[kvp.Key] = kvp.Value;
        }

        return fallback;
    }

    private static bool TryGetDictionary(Dictionary<string, object?> parameters, string key, out Dictionary<string, object?> dictionary)
    {
        if (parameters.TryGetValue(key, out var value) && value is not null)
        {
            dictionary = ConvertToDictionary(value);
            return true;
        }

        dictionary = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        return false;
    }

    private static Dictionary<string, object?> ConvertToDictionary(object? value)
    {
        return value switch
        {
            null => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            Dictionary<string, object?> dict => CreateCaseInsensitiveDictionary(dict),
            JsonObject jsonObject => CreateCaseInsensitiveDictionaryFromJsonObject(jsonObject),
            JsonElement element => JsonElementToDictionary(element),
            IDictionary dictionary => DictionaryFromDictionary(dictionary),
            _ => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IReadOnlyList<Dictionary<string, object?>> ConvertToDictionaryList(object? value)
    {
        if (value is null)
        {
            return Array.Empty<Dictionary<string, object?>>();
        }

        switch (value)
        {
            case JsonElement element:
                return JsonElementToDictionaryList(element);
            case JsonObject jsonObject:
                return new[] { CreateCaseInsensitiveDictionaryFromJsonObject(jsonObject) };
            case JsonArray jsonArray:
                return ConvertJsonArrayToDictionaryList(jsonArray);
            case Dictionary<string, object?> dictionary:
                return new[] { CreateCaseInsensitiveDictionary(dictionary) };
            case IDictionary dictionary:
                return new[] { DictionaryFromDictionary(dictionary) };
            case IEnumerable enumerable when value is not string:
                return ConvertEnumerableToDictionaryList(enumerable);
            default:
                return Array.Empty<Dictionary<string, object?>>();
        }
    }

    private static IReadOnlyList<Dictionary<string, object?>> ConvertJsonArrayToDictionaryList(JsonArray array)
    {
        var results = new List<Dictionary<string, object?>>();

        foreach (var item in array)
        {
            switch (item)
            {
                case null:
                    results.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
                    break;
                case JsonObject jsonObject:
                    results.Add(CreateCaseInsensitiveDictionaryFromJsonObject(jsonObject));
                    break;
                case JsonArray nestedArray:
                    var nestedResults = ConvertJsonArrayToDictionaryList(nestedArray);
                    if (nestedResults.Count > 0)
                    {
                        results.AddRange(nestedResults);
                    }
                    else
                    {
                        results.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
                    }
                    break;
                case JsonValue jsonValue when jsonValue.TryGetValue(out JsonElement element):
                    var nested = JsonElementToDictionaryList(element);
                    if (nested.Count > 0)
                    {
                        results.AddRange(nested);
                    }
                    else
                    {
                        results.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
                    }
                    break;
                default:
                    results.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
                    break;
            }
        }

        return results;
    }

    private static IReadOnlyList<Dictionary<string, object?>> ConvertEnumerableToDictionaryList(IEnumerable enumerable)
    {
        var results = new List<Dictionary<string, object?>>();

        foreach (var item in enumerable)
        {
            if (item is null)
            {
                continue;
            }

            switch (item)
            {
                case Dictionary<string, object?> dictionary:
                    results.Add(CreateCaseInsensitiveDictionary(dictionary));
                    break;
                case IDictionary dictionary:
                    results.Add(DictionaryFromDictionary(dictionary));
                    break;
                case JsonObject jsonObject:
                    results.Add(CreateCaseInsensitiveDictionaryFromJsonObject(jsonObject));
                    break;
                case JsonArray jsonArray:
                    var nestedResults = ConvertJsonArrayToDictionaryList(jsonArray);
                    if (nestedResults.Count > 0)
                    {
                        results.AddRange(nestedResults);
                    }
                    else
                    {
                        results.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
                    }
                    break;
                case JsonValue jsonValue when jsonValue.TryGetValue(out JsonElement element):
                    var nested = JsonElementToDictionaryList(element);
                    if (nested.Count > 0)
                    {
                        results.AddRange(nested);
                    }
                    else
                    {
                        results.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
                    }
                    break;
                case JsonElement element:
                    var fromElement = JsonElementToDictionaryList(element);
                    if (fromElement.Count > 0)
                    {
                        results.AddRange(fromElement);
                    }
                    else
                    {
                        results.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
                    }
                    break;
                default:
                    results.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
                    break;
            }
        }

        return results;
    }

    private static Dictionary<string, object?> JsonElementToDictionary(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
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

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    return JsonElementToDictionary(item);
                }
            }
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<Dictionary<string, object?>> JsonElementToDictionaryList(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            return new[] { JsonElementToDictionary(element) };
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var results = new List<Dictionary<string, object?>>();

            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    results.Add(JsonElementToDictionary(item));
                }
                else if (item.ValueKind == JsonValueKind.Array)
                {
                    results.AddRange(JsonElementToDictionaryList(item));
                }
                else
                {
                    results.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
                }
            }

            return results;
        }

        return Array.Empty<Dictionary<string, object?>>();
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

    private static Dictionary<string, object?> CreateCaseInsensitiveDictionary(Dictionary<string, object?>? source)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
        {
            return dict;
        }

        foreach (var kvp in source)
        {
            dict[kvp.Key] = kvp.Value;
        }

        return dict;
    }

    private static Dictionary<string, object?> CreateCaseInsensitiveDictionaryFromJsonObject(JsonObject jsonObject)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in jsonObject)
        {
            dict[kvp.Key] = kvp.Value switch
            {
                null => null,
                JsonValue value when value.TryGetValue(out object? scalar) => scalar,
                _ => kvp.Value
            };
        }

        return dict;
    }

    private static Dictionary<string, object?> NormalizeParameters(Dictionary<string, object>? parameters)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (parameters is null)
        {
            return dict;
        }

        foreach (var kvp in parameters)
        {
            dict[kvp.Key] = kvp.Value;
        }

        return dict;
    }

    private static string? GetString(Dictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        var text = value switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element when element.ValueKind == JsonValueKind.Number => element.ToString(),
            _ => value.ToString()
        };

        return text?.Trim();
    }

    private JsonObject BuildBottleSchema()
    {
        var properties = new JsonObject
        {
            ["name"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Name of the wine the bottle belongs to."
            },
            ["vintage"] = new JsonObject
            {
                ["type"] = "integer",
                ["description"] = "Vintage year for the bottle."
            },
            ["country"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Country name of the wine."
            },
            ["region"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Region of the wine (such as Bordeaux, Burgundy, or California)."
            },
            ["appellation"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Appellation of the wine (such as Napa Valley or Pomerol)."
            },
            ["color"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Wine color. Valid options: " + string.Join(", ", _colorOptions)
            },
            ["price"] = new JsonObject
            {
                ["type"] = "number",
                ["description"] = "Optional bottle price."
            },
            ["userId"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "uuid",
                ["description"] = "User identifier required when providing tasting notes or scores."
            },
            ["user_id"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "uuid",
                ["description"] = "Snake_case alias for userId."
            },
            ["userName"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The user's name or alias. Must be explicitly provided by the user; do not invent or assume any value. If unknown, ask the user for their username."
            },

            ["user_name"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The user's name or alias. Must be explicitly provided by the user; do not invent or assume any value. If unknown, ask the user for their username."
            },
            ["score"] = new JsonObject
            {
                ["type"] = "number",
                ["description"] = "Optional rating or score (requires user information)."
            },
            ["tastingNote"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional tasting notes (requires user information)."
            },
            ["tasting_note"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Snake_case alias for tastingNote."
            },
            ["isDrunk"] = new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "Indicates whether the bottle has been consumed."
            },
            ["is_drunk"] = new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "Snake_case alias for isDrunk."
            },
            ["drunkAt"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "date-time",
                ["description"] = "Timestamp of when the bottle was consumed (requires isDrunk=true)."
            },
            ["drunk_at"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "date-time",
                ["description"] = "Snake_case alias for drunkAt."
            }
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
    }

    private static JsonObject BuildTastingNoteSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["note"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Full tasting note text."
                },
                ["score"] = new JsonObject
                {
                    ["type"] = "number",
                    ["description"] = "Optional score between 0 and 100."
                },
                ["userId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "uuid",
                    ["description"] = "Identifier of the note author."
                },
                ["userName"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Name of the note author when the id is unknown."
                },
                ["bottleId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "uuid",
                    ["description"] = "Identifier of the bottle the note belongs to."
                }
            }
        };
    }

}
