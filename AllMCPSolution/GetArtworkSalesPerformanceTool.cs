using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Artworks;

[McpTool("get_artwork_sales_performance_timeseries", "Returns a time series of artwork sales performance relative to estimates")]
public class GetArtworkSalesPerformanceTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;
    private const int MaxResults = 1000; // Limit for time series

    public GetArtworkSalesPerformanceTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "get_artwork_sales_performance_timeseries";
    public string Description => "Returns a time series showing how hammer prices performed relative to estimate ranges. Performance factor: 0-1 if within range, >1 if above ceiling, <0 if below floor.";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        parameters ??= new Dictionary<string, object>();

        // Extract parameters - same as ListArtworkSalesTool
        var artistId = parameters.ContainsKey("artistId") ? Guid.Parse(parameters["artistId"]?.ToString() ?? "") : (Guid?)null;
        var name = parameters.ContainsKey("name") ? parameters["name"]?.ToString() : null;
        var minHeight = parameters.ContainsKey("minHeight") ? Convert.ToDecimal(parameters["minHeight"]) : (decimal?)null;
        var maxHeight = parameters.ContainsKey("maxHeight") ? Convert.ToDecimal(parameters["maxHeight"]) : (decimal?)null;
        var minWidth = parameters.ContainsKey("minWidth") ? Convert.ToDecimal(parameters["minWidth"]) : (decimal?)null;
        var maxWidth = parameters.ContainsKey("maxWidth") ? Convert.ToDecimal(parameters["maxWidth"]) : (decimal?)null;
        var yearCreatedFrom = parameters.ContainsKey("yearCreatedFrom") ? Convert.ToInt32(parameters["yearCreatedFrom"]) : (int?)null;
        var yearCreatedTo = parameters.ContainsKey("yearCreatedTo") ? Convert.ToInt32(parameters["yearCreatedTo"]) : (int?)null;
        var saleDateFrom = parameters.ContainsKey("saleDateFrom") ? DateTime.Parse(parameters["saleDateFrom"]?.ToString() ?? "") : (DateTime?)null;
        var saleDateTo = parameters.ContainsKey("saleDateTo") ? DateTime.Parse(parameters["saleDateTo"]?.ToString() ?? "") : (DateTime?)null;
        var technique = parameters.ContainsKey("technique") ? parameters["technique"]?.ToString() : null;
        var category = parameters.ContainsKey("category") ? parameters["category"]?.ToString() : null;
        var currency = parameters.ContainsKey("currency") ? parameters["currency"]?.ToString() : null;
        var minLowEstimate = parameters.ContainsKey("minLowEstimate") ? Convert.ToDecimal(parameters["minLowEstimate"]) : (decimal?)null;
        var maxLowEstimate = parameters.ContainsKey("maxLowEstimate") ? Convert.ToDecimal(parameters["maxLowEstimate"]) : (decimal?)null;
        var minHighEstimate = parameters.ContainsKey("minHighEstimate") ? Convert.ToDecimal(parameters["minHighEstimate"]) : (decimal?)null;
        var maxHighEstimate = parameters.ContainsKey("maxHighEstimate") ? Convert.ToDecimal(parameters["maxHighEstimate"]) : (decimal?)null;
        var minHammerPrice = parameters.ContainsKey("minHammerPrice") ? Convert.ToDecimal(parameters["minHammerPrice"]) : (decimal?)null;
        var maxHammerPrice = parameters.ContainsKey("maxHammerPrice") ? Convert.ToDecimal(parameters["maxHammerPrice"]) : (decimal?)null;
        var sold = parameters.ContainsKey("sold") ? Convert.ToBoolean(parameters["sold"]) : (bool?)null;

        // Build query
        var query = _dbContext.ArtworkSales
            .Include(a => a.Artist)
            .AsQueryable();

        // Apply filters
        if (artistId.HasValue)
            query = query.Where(a => a.ArtistId == artistId.Value);

        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(a => a.Name.Contains(name));

        if (minHeight.HasValue)
            query = query.Where(a => a.Height >= minHeight.Value);

        if (maxHeight.HasValue)
            query = query.Where(a => a.Height <= maxHeight.Value);

        if (minWidth.HasValue)
            query = query.Where(a => a.Width >= minWidth.Value);

        if (maxWidth.HasValue)
            query = query.Where(a => a.Width <= maxWidth.Value);

        if (yearCreatedFrom.HasValue)
            query = query.Where(a => a.YearCreated >= yearCreatedFrom.Value);

        if (yearCreatedTo.HasValue)
            query = query.Where(a => a.YearCreated <= yearCreatedTo.Value);

        if (saleDateFrom.HasValue)
            query = query.Where(a => a.SaleDate >= saleDateFrom.Value);

        if (saleDateTo.HasValue)
            query = query.Where(a => a.SaleDate <= saleDateTo.Value);

        if (!string.IsNullOrWhiteSpace(technique))
            query = query.Where(a => a.Technique.Contains(technique));

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(a => a.Category.Contains(category));

        if (!string.IsNullOrWhiteSpace(currency))
            query = query.Where(a => a.Currency == currency);

        if (minLowEstimate.HasValue)
            query = query.Where(a => a.LowEstimate >= minLowEstimate.Value);

        if (maxLowEstimate.HasValue)
            query = query.Where(a => a.LowEstimate <= maxLowEstimate.Value);

        if (minHighEstimate.HasValue)
            query = query.Where(a => a.HighEstimate >= minHighEstimate.Value);

        if (maxHighEstimate.HasValue)
            query = query.Where(a => a.HighEstimate <= maxHighEstimate.Value);

        if (minHammerPrice.HasValue)
            query = query.Where(a => a.HammerPrice >= minHammerPrice.Value);

        if (maxHammerPrice.HasValue)
            query = query.Where(a => a.HammerPrice <= maxHammerPrice.Value);

        if (sold.HasValue)
            query = query.Where(a => a.Sold == sold.Value);

        // Only include sold items with valid estimates and hammer prices
        query = query.Where(a => a.Sold == true && a.LowEstimate > 0 && a.HighEstimate > 0 && a.HammerPrice > 0);

        // Get results ordered by sale date
        var sales = await query
            .OrderBy(a => a.SaleDate)
            .Take(MaxResults)
            .Select(a => new
            {
                a.SaleDate,
                a.LowEstimate,
                a.HighEstimate,
                a.HammerPrice
            })
            .ToListAsync();

        // Transform into time series with performance factor
        var timeSeries = sales.Select(sale => new
        {
            Time = sale.SaleDate,
            PerformanceFactor = CalculatePerformanceFactor(
                sale.HammerPrice,
                sale.LowEstimate,
                sale.HighEstimate
            )
        }).ToList();

        return new
        {
            timeSeries,
            count = timeSeries.Count,
            description = "Performance factor: 0-1 if within estimate range, >1 if above high estimate, <0 if below low estimate"
        };
    }

    private static double CalculatePerformanceFactor(decimal hammerPrice, decimal lowEstimate, decimal highEstimate)
    {
        // If hammer price is below low estimate
        if (hammerPrice < lowEstimate)
        {
            // Return negative factor: how far below as a fraction of low estimate
            // E.g., if hammer is 80 and low is 100, factor = -0.2
            return (double)((hammerPrice - lowEstimate) / lowEstimate);
        }

        // If hammer price is above high estimate
        if (hammerPrice > highEstimate)
        {
            // Return factor > 1: 1 + how far above as a fraction of high estimate
            // E.g., if hammer is 150 and high is 100, factor = 1.5
            return (double)(hammerPrice / highEstimate);
        }

        // If hammer price is within range
        // Map linearly from low (0) to high (1)
        var range = highEstimate - lowEstimate;
        if (range == 0)
            return 0.5; // Edge case: if low == high, return midpoint

        return (double)((hammerPrice - lowEstimate) / range);
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
                properties = new
                {
                    artistId = new
                    {
                        type = "string",
                        description = "Filter by artist ID (exact match)"
                    },
                    name = new
                    {
                        type = "string",
                        description = "Filter by artwork name (partial match)"
                    },
                    minHeight = new
                    {
                        type = "number",
                        description = "Minimum height in cm"
                    },
                    maxHeight = new
                    {
                        type = "number",
                        description = "Maximum height in cm"
                    },
                    minWidth = new
                    {
                        type = "number",
                        description = "Minimum width in cm"
                    },
                    maxWidth = new
                    {
                        type = "number",
                        description = "Maximum width in cm"
                    },
                    yearCreatedFrom = new
                    {
                        type = "integer",
                        description = "Start year for creation year filter"
                    },
                    yearCreatedTo = new
                    {
                        type = "integer",
                        description = "End year for creation year filter"
                    },
                    saleDateFrom = new
                    {
                        type = "string",
                        format = "date-time",
                        description = "Start date for sale date filter (ISO 8601 format)"
                    },
                    saleDateTo = new
                    {
                        type = "string",
                        format = "date-time",
                        description = "End date for sale date filter (ISO 8601 format)"
                    },
                    technique = new
                    {
                        type = "string",
                        description = "Filter by technique (partial match)"
                    },
                    category = new
                    {
                        type = "string",
                        description = "Filter by category (partial match)"
                    },
                    currency = new
                    {
                        type = "string",
                        description = "Filter by currency (exact match)"
                    },
                    minLowEstimate = new
                    {
                        type = "number",
                        description = "Minimum low estimate"
                    },
                    maxLowEstimate = new
                    {
                        type = "number",
                        description = "Maximum low estimate"
                    },
                    minHighEstimate = new
                    {
                        type = "number",
                        description = "Minimum high estimate"
                    },
                    maxHighEstimate = new
                    {
                        type = "number",
                        description = "Maximum high estimate"
                    },
                    minHammerPrice = new
                    {
                        type = "number",
                        description = "Minimum hammer price"
                    },
                    maxHammerPrice = new
                    {
                        type = "number",
                        description = "Maximum hammer price"
                    },
                    sold = new
                    {
                        type = "boolean",
                        description = "Filter by sold status"
                    }
                }
            }
        };
    }

    public object GetOpenApiSchema()
    {
        return new
        {
            summary = Description,
            operationId = Name,
            requestBody = new
            {
                required = false,
                content = new
                {
                    applicationJson = new
                    {
                        schema = new
                        {
                            type = "object",
                            properties = new
                            {
                                artistId = new { type = "string" },
                                name = new { type = "string" },
                                minHeight = new { type = "number" },
                                maxHeight = new { type = "number" },
                                minWidth = new { type = "number" },
                                maxWidth = new { type = "number" },
                                yearCreatedFrom = new { type = "integer" },
                                yearCreatedTo = new { type = "integer" },
                                saleDateFrom = new { type = "string", format = "date-time" },
                                saleDateTo = new { type = "string", format = "date-time" },
                                technique = new { type = "string" },
                                category = new { type = "string" },
                                currency = new { type = "string" },
                                minLowEstimate = new { type = "number" },
                                maxLowEstimate = new { type = "number" },
                                minHighEstimate = new { type = "number" },
                                maxHighEstimate = new { type = "number" },
                                minHammerPrice = new { type = "number" },
                                maxHammerPrice = new { type = "number" },
                                sold = new { type = "boolean" }
                            }
                        }
                    }
                }
            },
            responses = new
            {
                _200 = new
                {
                    description = "Success",
                    content = new
                    {
                        applicationJson = new
                        {
                            schema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    result = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            timeSeries = new
                                            {
                                                type = "array",
                                                items = new
                                                {
                                                    type = "object",
                                                    properties = new
                                                    {
                                                        time = new { type = "string", format = "date-time" },
                                                        performanceFactor = new { type = "number" }
                                                    }
                                                }
                                            },
                                            count = new { type = "integer" },
                                            description = new { type = "string" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}
