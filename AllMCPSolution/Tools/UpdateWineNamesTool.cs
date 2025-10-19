using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;
using AllMCPSolution.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AllMCPSolution.Wines;

[McpTool("update_wine_names", "Updates wine names for multiple wines in a single operation.")]
public sealed class UpdateWineNamesTool : IToolBase, IMcpTool
{
    private const string InvokingMessage = "Updating wine names…";
    private const string InvokedMessage = "Wine names updated.";

    private readonly IWineRepository _wines;

    public UpdateWineNamesTool(IWineRepository wines)
    {
        _wines = wines;
    }

    public string Name => "update_wine_names";
    public string Description => "Updates wine names for multiple wines in a single operation.";
    public string? SafetyLevel => "caution";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
        => await ExecuteInternalAsync(NormalizeParameters(parameters), CancellationToken.None);

    public async ValueTask<CallToolResult> RunAsync(CallToolRequestParams request, CancellationToken ct)
    {
        var parameters = ConvertArgumentsToDictionary(request?.Arguments);
        var result = await ExecuteInternalAsync(parameters, ct);
        var node = JsonSerializer.SerializeToNode(result) as JsonObject;

        var message = node?.TryGetPropertyValue("message", out var messageNode) == true
            && messageNode is JsonValue value
            && value.TryGetValue<string>(out var parsed)
                ? parsed
                : (node?.TryGetPropertyValue("success", out var successNode) == true
                    && successNode is JsonValue successValue
                    && successValue.TryGetValue<bool>(out var success)
                        ? success ? "Wine names updated." : "Wine name update failed."
                        : "Wine name update completed.");

        return new CallToolResult
        {
            Content = new[] { new TextContentBlock { Type = "text", Text = message } },
            StructuredContent = node
        };
    }

    public Tool GetDefinition()
    {
        var schema = BuildInputSchema();
        return new Tool
        {
            Name = Name,
            Title = "Update wine names",
            Description = Description,
            InputSchema = JsonDocument.Parse(schema.ToJsonString()).RootElement,
            Meta = new JsonObject
            {
                ["openai/toolInvocation/invoking"] = InvokingMessage,
                ["openai/toolInvocation/invoked"] = InvokedMessage
            }
        };
    }

    public object GetToolDefinition() => new
    {
        name = Name,
        description = Description,
        safety = new { level = SafetyLevel },
        inputSchema = JsonSerializer.Deserialize<object>(BuildInputSchema().ToJsonString())
    }!;

    public object GetOpenApiSchema()
    {
        var schema = JsonSerializer.Deserialize<object>(BuildInputSchema().ToJsonString());
        return new
        {
            operationId = Name,
            summary = Description,
            description = Description,
            requestBody = new
            {
                required = true,
                content = new
                {
                    application__json = new
                    {
                        schema
                    }
                }
            },
            responses = new
            {
                _200 = new
                {
                    description = "Batch update completed.",
                    content = new
                    {
                        application__json = new
                        {
                            schema = new { type = "object" }
                        }
                    }
                }
            }
        };
    }

    private async Task<object> ExecuteInternalAsync(Dictionary<string, object?>? parameters, CancellationToken ct)
    {
        var (payloads, validationErrors) = ParseUpdates(parameters);
        if (validationErrors.Count > 0)
        {
            return new
            {
                success = false,
                message = "Validation failed for one or more requested wine name updates.",
                processed = 0,
                updated = 0,
                unchanged = 0,
                failed = validationErrors.Count,
                results = Array.Empty<object>(),
                errors = validationErrors
            };
        }

        var results = new List<object>(payloads.Count);
        var errors = new List<string>();
        var updatedCount = 0;
        var unchangedCount = 0;

        foreach (var payload in payloads)
        {
            var wine = await _wines.GetByIdAsync(payload.WineId, ct);
            if (wine is null)
            {
                var suggestions = await _wines.FindClosestMatchesAsync(payload.DesiredName, 3, ct);
                var suggestionList = suggestions
                    .Select(s => new { id = s.Id, name = s.Name })
                    .ToList();

                var message = $"Wine '{payload.WineId}' was not found.";
                errors.Add(message);
                results.Add(new
                {
                    wineId = payload.WineId,
                    requestedName = payload.DesiredName,
                    status = "not_found",
                    message,
                    suggestions = suggestionList.Count > 0 ? suggestionList : null
                });
                continue;
            }

            var trimmed = payload.DesiredName.Trim();
            if (string.Equals(wine.Name, trimmed, StringComparison.Ordinal))
            {
                unchangedCount++;
                results.Add(new
                {
                    wineId = wine.Id,
                    previousName = wine.Name,
                    newName = trimmed,
                    status = "unchanged",
                    message = "Name already matches the requested value."
                });
                continue;
            }

            var previousName = wine.Name;
            wine.Name = trimmed;
            await _wines.UpdateAsync(wine, ct);
            updatedCount++;
            results.Add(new
            {
                wineId = wine.Id,
                previousName,
                newName = trimmed,
                status = "updated"
            });
        }

        var success = errors.Count == 0;
        var processed = payloads.Count;
        var messageText = success
            ? processed == 0
                ? "No wine name changes were requested."
                : $"Updated {updatedCount} wine name{(updatedCount == 1 ? string.Empty : "s")} with {unchangedCount} unchanged."
            : $"Processed {processed} wine name update{(processed == 1 ? string.Empty : "s")}, but {errors.Count} failed.";

        return new
        {
            success,
            message = messageText,
            processed,
            updated = updatedCount,
            unchanged = unchangedCount,
            failed = errors.Count,
            results,
            errors = errors.Count > 0 ? errors : null
        };
    }

    private static (List<WineNameUpdate> Payloads, List<string> Errors) ParseUpdates(Dictionary<string, object?>? parameters)
    {
        var errors = new List<string>();
        if (parameters is null)
        {
            errors.Add("Parameter 'updates' is required.");
            return (new List<WineNameUpdate>(), errors);
        }

        object? raw = null;
        if (!parameters.TryGetValue("updates", out raw)
            && !parameters.TryGetValue("wineUpdates", out raw)
            && !parameters.TryGetValue("wine_updates", out raw))
        {
            errors.Add("Parameter 'updates' is required.");
            return (new List<WineNameUpdate>(), errors);
        }

        var payloads = new List<WineNameUpdate>();
        if (raw is null)
        {
            errors.Add("Parameter 'updates' must be a non-empty array of objects.");
            return (payloads, errors);
        }

        void ParseElement(JsonElement element, int index)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"Update #{index}: each update must be an object.");
                return;
            }

            if (!TryGetProperty(element, "wineId", out var idElement)
                && !TryGetProperty(element, "id", out idElement))
            {
                errors.Add($"Update #{index}: 'wineId' is required.");
                return;
            }

            if (!TryParseGuid(idElement, out var wineId))
            {
                errors.Add($"Update #{index}: 'wineId' must be a valid UUID.");
                return;
            }

            if (!TryGetProperty(element, "newName", out var nameElement)
                && !TryGetProperty(element, "name", out nameElement))
            {
                errors.Add($"Update #{index}: 'newName' is required.");
                return;
            }

            var desiredName = TryParseString(nameElement)?.Trim();
            if (string.IsNullOrWhiteSpace(desiredName))
            {
                errors.Add($"Update #{index}: 'newName' cannot be empty.");
                return;
            }

            payloads.Add(new WineNameUpdate(wineId, desiredName));
        }

        void ParseObject(object candidate, int index)
        {
            try
            {
                var element = JsonSerializer.SerializeToElement(candidate);
                ParseElement(element, index);
            }
            catch
            {
                errors.Add($"Update #{index}: could not be parsed.");
            }
        }

        switch (raw)
        {
            case JsonElement jsonElement:
                if (jsonElement.ValueKind == JsonValueKind.Array)
                {
                    var idx = 1;
                    foreach (var item in jsonElement.EnumerateArray())
                    {
                        ParseElement(item, idx++);
                    }
                }
                else if (jsonElement.ValueKind == JsonValueKind.Object)
                {
                    ParseElement(jsonElement, 1);
                }
                else if (jsonElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    errors.Add("Parameter 'updates' must be a non-empty array of objects.");
                }
                else
                {
                    errors.Add("Parameter 'updates' must be a non-empty array of objects.");
                }
                break;
            case string text:
                if (string.IsNullOrWhiteSpace(text))
                {
                    errors.Add("Parameter 'updates' must be a non-empty array of objects.");
                }
                else
                {
                    try
                    {
                        var element = JsonSerializer.Deserialize<JsonElement>(text);
                        ParseElementOrArray(element);
                    }
                    catch
                    {
                        errors.Add("Parameter 'updates' could not be parsed from the provided string.");
                    }
                }
                break;
            case IEnumerable enumerable:
                var index = 1;
                foreach (var item in enumerable)
                {
                    if (item is JsonElement element)
                    {
                        ParseElement(element, index++);
                    }
                    else if (item is null)
                    {
                        errors.Add($"Update #{index++}: value cannot be null.");
                    }
                    else
                    {
                        ParseObject(item, index++);
                    }
                }
                break;
            default:
                ParseObject(raw, 1);
                break;
        }

        if (payloads.Count == 0 && errors.Count == 0)
        {
            errors.Add("No valid wine name updates were provided.");
        }

        return (payloads, errors);

        void ParseElementOrArray(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                var idx = 1;
                foreach (var item in element.EnumerateArray())
                {
                    ParseElement(item, idx++);
                }
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                ParseElement(element, 1);
            }
            else
            {
                errors.Add("Parameter 'updates' must be a non-empty array of objects.");
            }
        }
    }

    private static JsonObject BuildInputSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["updates"] = new JsonObject
                {
                    ["type"] = "array",
                    ["description"] = "Array of wine name update objects. Each object must include 'wineId' (or 'wine_id') and 'newName' (or 'new_name').",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["wineId"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["format"] = "uuid",
                                ["description"] = "Identifier of the wine to rename. Accepts 'wineId' or 'wine_id'."
                            },
                            ["newName"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "New display name for the wine. Accepts 'newName' or 'new_name'."
                            }
                        },
                        ["required"] = new JsonArray { "wineId", "newName" }
                    }
                }
            },
            ["required"] = new JsonArray { "updates" }
        };
    }

    private static Dictionary<string, object?>? NormalizeParameters(Dictionary<string, object>? parameters)
    {
        if (parameters is null)
        {
            return null;
        }

        var dict = new Dictionary<string, object?>(parameters.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in parameters)
        {
            dict[kvp.Key] = kvp.Value;
        }

        return dict;
    }

    private static Dictionary<string, object?>? ConvertArgumentsToDictionary(IReadOnlyDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null)
        {
            return null;
        }

        var dict = new Dictionary<string, object?>(arguments.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in arguments)
        {
            dict[kvp.Key] = kvp.Value;
        }

        return dict;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        var snake = ParameterHelpers.ToSnakeCase(propertyName);
        if (!string.Equals(snake, propertyName, StringComparison.Ordinal)
            && element.TryGetProperty(snake, out value))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseGuid(JsonElement element, out Guid value)
    {
        value = default;
        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            if (!string.IsNullOrWhiteSpace(text) && Guid.TryParse(text, out value))
            {
                return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Object || element.ValueKind == JsonValueKind.Array)
        {
            var text = element.ToString();
            if (!string.IsNullOrWhiteSpace(text) && Guid.TryParse(text, out value))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryParseString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => element.ToString()
        };
    }

    private sealed record WineNameUpdate(Guid WineId, string DesiredName);
}
