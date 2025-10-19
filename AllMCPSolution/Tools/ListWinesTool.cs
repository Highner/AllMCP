using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Repositories;
using AllMCPSolution.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AllMCPSolution.Wines;

[McpTool("list_wines", "Lists all wines with their identifiers.")]
public sealed class ListWinesTool : IToolBase, IMcpTool
{
    private const string InvokingMessage = "Loading wines…";
    private const string InvokedMessage = "Wines loaded.";

    private readonly IWineRepository _wines;

    public ListWinesTool(IWineRepository wines)
    {
        _wines = wines;
    }

    public string Name => "list_wines";
    public string Description => "Lists all wines from the catalog with their identifiers.";
    public string? SafetyLevel => "non_critical";

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
                : "Wine list ready.";

        return new CallToolResult
        {
            Content = new[] { new TextContentBlock { Type = "text", Text = message } },
            StructuredContent = node
        };
    }

    public Tool GetDefinition() => new()
    {
        Name = Name,
        Title = "List wines",
        Description = Description,
        InputSchema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{},\"required\":[]}").RootElement,
        Meta = new JsonObject
        {
            ["openai/toolInvocation/invoking"] = InvokingMessage,
            ["openai/toolInvocation/invoked"] = InvokedMessage
        }
    };

    public object GetToolDefinition() => new
    {
        name = Name,
        description = Description,
        safety = new { level = SafetyLevel },
        inputSchema = new
        {
            type = "object",
            properties = new { },
            required = Array.Empty<string>()
        }
    };

    public object GetOpenApiSchema() => new
    {
        operationId = Name,
        summary = Description,
        description = Description,
        requestBody = new
        {
            required = false,
            content = new
            {
                application__json = new
                {
                    schema = new { type = "object" }
                }
            }
        },
        responses = new
        {
            _200 = new
            {
                description = "Successful response containing the wine list.",
                content = new
                {
                    application__json = new
                    {
                        schema = new
                        {
                            type = "object",
                            properties = new
                            {
                                success = new { type = "boolean" },
                                count = new { type = "integer" },
                                message = new { type = "string" },
                                wines = new
                                {
                                    type = "array",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            id = new { type = "string", format = "uuid" },
                                            name = new { type = "string" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    };

    private async Task<object> ExecuteInternalAsync(Dictionary<string, object?>? parameters, CancellationToken ct)
    {
        var wines = await _wines.GetAllAsync(ct);
        var payload = wines
            .Select(w => new
            {
                id = w.Id,
                name = w.Name
            })
            .ToList();

        var count = payload.Count;
        var message = count == 0
            ? "No wines found."
            : $"Found {count} wine{(count == 1 ? string.Empty : "s")}.";

        return new
        {
            success = true,
            count,
            message,
            wines = payload
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
}
