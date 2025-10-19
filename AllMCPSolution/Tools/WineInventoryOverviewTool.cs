using System;
using System.Linq;
using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Repositories;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AllMCPSolution.Tools;

[McpTool("wine_inventory_overview", "Summarizes undrunk bottles by geography and vintage for quick inventory insights.")]
public sealed class WineInventoryOverviewTool : IToolBase, IMcpTool
{
    private readonly IBottleRepository _bottles;
    public WineInventoryOverviewTool(IBottleRepository bottles)
    {
        _bottles = bottles;
    }

    public string Name => "wine_inventory_overview";
    public string Description => "Summarizes undrunk bottles by region/appellation and vintage.";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        var locations = await _bottles.GetActiveBottleLocationsAsync();
        var capturedAt = DateTimeOffset.UtcNow;

        var totalBottles = locations.Count;

        var regions = locations
            .GroupBy(l => new
            {
                l.RegionId,
                Name = string.IsNullOrWhiteSpace(l.RegionName) ? "Unknown region" : l.RegionName!.Trim()
            })
            .Select(regionGroup => new
            {
                regionId = regionGroup.Key.RegionId,
                regionName = regionGroup.Key.Name,
                count = regionGroup.Count(),
                appellations = regionGroup
                    .GroupBy(a => new
                    {
                        a.AppellationId,
                        Name = string.IsNullOrWhiteSpace(a.AppellationName) ? "Unknown appellation" : a.AppellationName!.Trim()
                    })
                    .Select(appGroup => new
                    {
                        appellationId = appGroup.Key.AppellationId,
                        appellationName = appGroup.Key.Name,
                        count = appGroup.Count()
                    })
                    .OrderByDescending(a => a.count)
                    .ThenBy(a => a.appellationName)
                    .ToList()
            })
            .OrderByDescending(r => r.count)
            .ThenBy(r => r.regionName)
            .ToList();

        var totalAppellations = regions.Sum(r => r.appellations.Count);

        var vintages = locations
            .GroupBy(l => l.Vintage)
            .Select(group => new
            {
                vintage = group.Key,
                count = group.Count()
            })
            .OrderByDescending(v => v.count)
            .ThenBy(v => v.vintage ?? int.MaxValue)
            .ToList();

        return new
        {
            success = true,
            generatedAt = capturedAt,
            totalBottles,
            totals = new
            {
                regions = regions.Count,
                appellations = totalAppellations,
                vintages = vintages.Count
            },
            regions,
            vintages
        };
    }

    public object GetToolDefinition() => new
    {
        name = Name,
        description = Description,
        safety = new
        {
            level = SafetyLevel
        },
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
                    schema = new
                    {
                        type = "object"
                    }
                }
            }
        },
        responses = new
        {
            _200 = new
            {
                description = "Wine inventory overview grouped by geography and vintage.",
                content = new
                {
                    application__json = new
                    {
                        schema = new
                        {
                            type = "object"
                        }
                    }
                }
            }
        }
    };

    public Tool GetDefinition() => new()
    {
        Name = Name,
        Title = "Wine inventory overview",
        Description = Description,
        InputSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {},
          "required": []
        }
        """).RootElement,
        Meta = new JsonObject
        {
            ["openai/toolInvocation/invoking"] = "Summarizing wine inventory…",
            ["openai/toolInvocation/invoked"] = "Wine inventory overview ready"
        }
    };

    public async ValueTask<CallToolResult> RunAsync(CallToolRequestParams request, CancellationToken ct)
    {
        Dictionary<string, object?>? dict = null;
        if (request?.Arguments is not null)
        {
            dict = new Dictionary<string, object?>();
            foreach (var kv in request.Arguments)
            {
                dict[kv.Key] = kv.Value.ValueKind switch
                {
                    JsonValueKind.String => kv.Value.GetString(),
                    JsonValueKind.Number => kv.Value.TryGetInt32(out var i)
                        ? i
                        : kv.Value.TryGetInt64(out var l)
                            ? l
                            : kv.Value.TryGetDouble(out var d)
                                ? d
                                : null,
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null
                };
            }
        }

        var result = await ExecuteAsync(dict);
        var node = JsonSerializer.SerializeToNode(result) as JsonObject;
        var message = "Wine inventory overview ready.";
        if (node?.TryGetPropertyValue("totalBottles", out var totalNode) == true && totalNode is JsonValue totalValue && totalValue.TryGetValue<int>(out var total))
        {
            message = total == 1 ? "1 undrunk bottle remaining." : $"{total} undrunk bottles remaining.";
        }

        return new CallToolResult
        {
            Content = new[]
            {
                new TextContentBlock { Type = "text", Text = message }
            },
            StructuredContent = node
        };
    }


}
