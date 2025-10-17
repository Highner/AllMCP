using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using AllMCPSolution.Services;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AllMCPSolution.Artworks;

[McpTool("get_artwork_sales_hammer_per_area_timeseries", "Returns hammer price per area (height*width) and inflation-adjusted per-area values.")]
public class GetArtworkSalesHammerPerAreaTool : IToolBase, IMcpTool
{
    private readonly IHammerPerAreaAnalyticsService _analyticsService;

    public GetArtworkSalesHammerPerAreaTool(IHammerPerAreaAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    public string Name => "get_artwork_sales_hammer_per_area_timeseries";
    public string Description => "Returns hammer price per area and inflation-adjusted hammer price per area (to today's prices). Area = height * width in square cm.";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        parameters ??= new Dictionary<string, object>();

        var filter = new HammerPerAreaFilter
        {
            ArtistId = ParameterHelpers.GetGuidParameter(parameters, "artistId", "artist_id"),
            Name = ParameterHelpers.GetStringParameter(parameters, "name", "name"),
            MinHeight = ParameterHelpers.GetDecimalParameter(parameters, "minHeight", "min_height"),
            MaxHeight = ParameterHelpers.GetDecimalParameter(parameters, "maxHeight", "max_height"),
            MinWidth = ParameterHelpers.GetDecimalParameter(parameters, "minWidth", "min_width"),
            MaxWidth = ParameterHelpers.GetDecimalParameter(parameters, "maxWidth", "max_width"),
            YearCreatedFrom = ParameterHelpers.GetIntParameter(parameters, "yearCreatedFrom", "year_created_from"),
            YearCreatedTo = ParameterHelpers.GetIntParameter(parameters, "yearCreatedTo", "year_created_to"),
            SaleDateFrom = ParameterHelpers.GetDateTimeParameter(parameters, "saleDateFrom", "sale_date_from"),
            SaleDateTo = ParameterHelpers.GetDateTimeParameter(parameters, "saleDateTo", "sale_date_to"),
            Technique = ParameterHelpers.GetStringParameter(parameters, "technique", "technique"),
            Category = ParameterHelpers.GetStringParameter(parameters, "category", "category"),
            Currency = ParameterHelpers.GetStringParameter(parameters, "currency", "currency"),
            MinLowEstimate = ParameterHelpers.GetDecimalParameter(parameters, "minLowEstimate", "min_low_estimate"),
            MaxLowEstimate = ParameterHelpers.GetDecimalParameter(parameters, "maxLowEstimate", "max_low_estimate"),
            MinHighEstimate = ParameterHelpers.GetDecimalParameter(parameters, "minHighEstimate", "min_high_estimate"),
            MaxHighEstimate = ParameterHelpers.GetDecimalParameter(parameters, "maxHighEstimate", "max_high_estimate"),
            MinHammerPrice = ParameterHelpers.GetDecimalParameter(parameters, "minHammerPrice", "min_hammer_price"),
            MaxHammerPrice = ParameterHelpers.GetDecimalParameter(parameters, "maxHammerPrice", "max_hammer_price"),
            Sold = ParameterHelpers.GetBoolParameter(parameters, "sold", "sold"),
            Page = ParameterHelpers.GetIntParameter(parameters, "page", "page") ?? 1
        };

        var result = await _analyticsService.GetHammerPerAreaAsync(filter);
        return result;
    }

    public object GetToolDefinition()
    {
        return new
        {
            name = Name,
            description = Description,
            inputSchema = new
            {
                type = "object",
                properties = ParameterHelpers.CreateOpenApiProperties(null),
                required = Array.Empty<string>()
            }
        };
    }

    public object GetOpenApiSchema()
    {
        return new
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
                            type = "object",
                            properties = ParameterHelpers.CreateOpenApiProperties(null)
                        }
                    }
                }
            },
            responses = new
            {
                _200 = new
                {
                    description = "Successful response",
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
    }

    // IMcpTool implementation (delegates to ExecuteAsync)
    public Tool GetDefinition() => new Tool
    {
        Name = Name,
        Title = "Hammer price per area timeseries",
        Description = Description,
        InputSchema = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            type = "object",
            properties = ParameterHelpers.CreateOpenApiProperties(null),
            required = Array.Empty<string>()
        })).RootElement
    };

    public async ValueTask<CallToolResult> RunAsync(CallToolRequestParams request, CancellationToken ct)
    {
        Dictionary<string, object?>? dict = null;
        if (request?.Arguments is not null)
        {
            dict = new Dictionary<string, object?>();
            foreach (var kvp in request.Arguments)
            {
                dict[kvp.Key] = JsonElementToNet(kvp.Value);
            }
        }

        var result = await ExecuteAsync(dict);
        return new CallToolResult
        {
            StructuredContent = JsonSerializer.SerializeToNode(result) as JsonObject
        };
    }

    private static object? JsonElementToNet(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var i) ? i : el.TryGetDecimal(out var d) ? d : null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToNet(p.Value)),
        JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToNet).ToArray(),
        _ => null
    };
}
