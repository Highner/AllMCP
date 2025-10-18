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
        var action = GetString(normalized, "action");
        if (string.IsNullOrWhiteSpace(action))
        {
            return Failure("inventory", "Action is required.", new[] { "'action' is required." });
        }

        switch (action)
        {
            case "add_bottle":
            case "add_bottle_with_note":
            {
                var bottleParameters = ExtractBottlePayload(normalized);
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
                    action,
                    bottle = result.Bottle is null ? null : BottleResponseMapper.MapBottle(result.Bottle),
                    tastingNote = result.TastingNote is null ? null : TastingNoteResponseMapper.MapTastingNote(result.TastingNote)
                };

                return Success("inventory", result.Message, data);
            }

            case "add_tasting_note":
            {
                var tastingNoteParameters = ExtractTastingNotePayload(normalized);
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
                    action,
                    tastingNote = result.TastingNote is null ? null : TastingNoteResponseMapper.MapTastingNote(result.TastingNote)
                };

                return Success("inventory", result.Message, data);
            }

            default:
                return Failure("inventory", $"Unsupported action '{action}'.", new[] { "Valid actions: add_bottle, add_bottle_with_note, add_tasting_note." });
        }
    }

    protected override JsonObject BuildInputSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["action"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray("add_bottle", "add_bottle_with_note", "add_tasting_note"),
                    ["description"] = "Inventory action to perform."
                },
                ["bottle"] = BuildBottleSchema(),
                ["bottle_payload"] = BuildBottleSchema(),
                ["payload"] = BuildBottleSchema(),
                ["tastingNote"] = BuildTastingNoteSchema(),
                ["tasting_note"] = BuildTastingNoteSchema()
            },
            ["required"] = new JsonArray("action")
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
                ["description"] = "User name alias when the identifier is unknown."
            },
            ["user_name"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Snake_case alias for userName."
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
