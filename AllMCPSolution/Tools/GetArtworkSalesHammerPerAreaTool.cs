using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Services;

namespace AllMCPSolution.Artworks;

[McpTool("get_artwork_sales_hammer_per_area_timeseries", "Returns hammer price per area (height*width) and inflation-adjusted per-area values.")]
public class GetArtworkSalesHammerPerAreaTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IHammerPerAreaAnalyticsService _analyticsService;

    public GetArtworkSalesHammerPerAreaTool(ApplicationDbContext dbContext, IHammerPerAreaAnalyticsService analyticsService)
    {
        _dbContext = dbContext;
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
                properties = ParameterHelpers.CreateOpenApiProperties(_dbContext),
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
                            properties = ParameterHelpers.CreateOpenApiProperties(_dbContext)
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
}
