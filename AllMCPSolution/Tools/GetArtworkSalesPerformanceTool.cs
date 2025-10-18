using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using AllMCPSolution.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AllMCPSolution.Artworks;

[McpTool("get_artwork_sales_performance_timeseries", "Returns a time series of artwork sales performance relative to estimates")]
public class GetArtworkSalesPerformanceTool //: IToolBase, IMcpTool
{
    private readonly IArtworkSaleQueryRepository _repo;
    private const int MaxResults = 1000; // Limit for time series

    public GetArtworkSalesPerformanceTool(IArtworkSaleQueryRepository repo)
    {
        _repo = repo;
    }

    public string Name => "get_artwork_sales_performance_timeseries";
    public string Description => "Returns a time series showing how hammer prices performed relative to estimate ranges. Performance factor: 0-1 if within range, >1 if above ceiling, <0 if below floor.";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        parameters ??= new Dictionary<string, object>();

        // Extract parameters using helper methods that support both naming conventions
        var artistId = ParameterHelpers.GetGuidParameter(parameters, "artistId", "artist_id");
        var name = ParameterHelpers.GetStringParameter(parameters, "name", "name");
        var minHeight = ParameterHelpers.GetDecimalParameter(parameters, "minHeight", "min_height");
        var maxHeight = ParameterHelpers.GetDecimalParameter(parameters, "maxHeight", "max_height");
        var minWidth = ParameterHelpers.GetDecimalParameter(parameters, "minWidth", "min_width");
        var maxWidth = ParameterHelpers.GetDecimalParameter(parameters, "maxWidth", "max_width");
        var yearCreatedFrom = ParameterHelpers.GetIntParameter(parameters, "yearCreatedFrom", "year_created_from");
        var yearCreatedTo = ParameterHelpers.GetIntParameter(parameters, "yearCreatedTo", "year_created_to");
        var saleDateFrom = ParameterHelpers.GetDateTimeParameter(parameters, "saleDateFrom", "sale_date_from");
        var saleDateTo = ParameterHelpers.GetDateTimeParameter(parameters, "saleDateTo", "sale_date_to");
        var technique = ParameterHelpers.GetStringParameter(parameters, "technique", "technique");
        var category = ParameterHelpers.GetStringParameter(parameters, "category", "category");
        var currency = ParameterHelpers.GetStringParameter(parameters, "currency", "currency");
        var minLowEstimate = ParameterHelpers.GetDecimalParameter(parameters, "minLowEstimate", "min_low_estimate");
        var maxLowEstimate = ParameterHelpers.GetDecimalParameter(parameters, "maxLowEstimate", "max_low_estimate");
        var minHighEstimate = ParameterHelpers.GetDecimalParameter(parameters, "minHighEstimate", "min_high_estimate");
        var maxHighEstimate = ParameterHelpers.GetDecimalParameter(parameters, "maxHighEstimate", "max_high_estimate");
        var minHammerPrice = ParameterHelpers.GetDecimalParameter(parameters, "minHammerPrice", "min_hammer_price");
        var maxHammerPrice = ParameterHelpers.GetDecimalParameter(parameters, "maxHammerPrice", "max_hammer_price");
        var sold = ParameterHelpers.GetBoolParameter(parameters, "sold", "sold");
        var page = ParameterHelpers.GetIntParameter(parameters, "page", "page") ?? 1;

        // Build query (via repository)
        var query = _repo.ArtworkSales;

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
        //query = query.Where(a => a.Sold == true && a.LowEstimate > 0 && a.HighEstimate > 0 && a.HammerPrice > 0);

        // Get total count before pagination
        var totalCount = await query.CountAsync();

        // Calculate skip based on page number
        var skip = (page - 1) * MaxResults;

        // Get results ordered by sale date with skip and take
        var sales = await query
            .OrderByDescending(a => a.SaleDate)
            .Skip(skip)
            .Take(MaxResults)
            .Select(a => new
            {
                a.Name,
                a.Category,
                a.Technique,
                a.YearCreated,
                a.SaleDate,
                a.LowEstimate,
                a.HighEstimate,
                a.HammerPrice,
                a.Height,
                a.Width,
                a.Sold
            })
            .ToListAsync();

        // Transform into time series with performance factor
        var timeSeries = sales
            .Select(sale => new
            {
               // Title = sale.Name,
                Category = sale.Category,
                Technique = sale.Technique,
                YearCreated = sale.YearCreated,
                Time = sale.SaleDate,
                HammerPrice = sale.HammerPrice,
                sale.Height,
                sale.Width,
                Sold = sale.Sold,
                area = sale.Height * sale.Width,
                PerformanceFactor = PerformanceCalculator.CalculatePerformanceFactor(
                    sale.HammerPrice,
                    sale.LowEstimate,
                    sale.HighEstimate
                )
            })
            .Where(x => x.PerformanceFactor.HasValue)
            .ToList();

        var totalPages = (int)Math.Ceiling((double)totalCount / MaxResults);
        var hasMoreResults = page < totalPages;

        var result = new
        {
            timeSeries,
            count = timeSeries.Count,
            totalCount,
            totalPages,
            currentPage = page,
            hasMoreResults,
            description =
                "Performance factor: 0-1 if within estimate range, >1 if above high estimate, <0 if below low estimate"
        };

        // Add pagination instructions if there are more results
        if (hasMoreResults)
        {
            return new
            {
                result.timeSeries,
                result.count,
                result.totalCount,
                result.totalPages,
                result.currentPage,
                result.hasMoreResults,
                nextPageInstructions =
                    $"To get the next page of results, call this tool again with page={page + 1}.",
                result.description
            };
        }
        else
        {
            // Last page - include merge instructions if there were multiple pages
            var response = new
            {
                result.timeSeries,
                result.count,
                result.totalCount,
                result.totalPages,
                result.currentPage,
                result.hasMoreResults,
                result.description
            };

            if (totalPages > 1)
            {
                return new
                {
                    response.timeSeries,
                    response.count,
                    response.totalCount,
                    response.totalPages,
                    response.currentPage,
                    response.hasMoreResults,
                    mergeInstructions =
                        $"This is the final page (page {page} of {totalPages}). If you retrieved multiple pages, merge all timeSeries arrays from all pages into one large dataset for analysis. Do not leave out any items from any of the pages. Do not summarize or aggregate the data unless explicitly asked to do so.",
                    response.description
                };
            }

            return response;
        }
    }

    public object GetToolDefinition()
{
    // Category description without direct DB access (repository used only in Execute)
    var categoryDescription = "Filter by category (partial match)";

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
                    description = categoryDescription
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
                },
                page = new
                {
                    type = "integer",
                    description = $"Page number for pagination (default: 1). Maximum {MaxResults} results returned per page."
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
            content = new Dictionary<string, object>
            {
                ["application/json"] = new
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
                description = "Success",
                content = new Dictionary<string, object>
                {
                    ["application/json"] = new
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
                                                    title = new { type = "string" },
                                                    category = new { type = "string" },
                                                    technique = new { type = "string" },
                                                    yearCreated = new { type = "integer" },
                                                    time = new { type = "string", format = "date-time" },
                                                    performanceFactor = new { type = "number" },
                                                    hammerPrice = new { type = "number" },
                                                    height = new { type = "number" },
                                                    width = new { type = "number" },
                                                    area = new { type = "number" }
                                                }
                                            }
                                        },
                                        count = new { type = "integer" },
                                        totalCount = new { type = "integer" },
                                        currentSkip = new { type = "integer" },
                                        lastItemIndex = new { type = "integer" },
                                        totalPages = new { type = "integer" },
                                        currentPage = new { type = "integer" },
                                        hasMoreResults = new { type = "boolean" },
                                        nextPageInstructions = new { type = "string" },
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
    // IMcpTool implementation (delegates to ExecuteAsync)
    public Tool GetDefinition() => new Tool
    {
        Name = Name,
        Title = "Artwork sales performance timeseries",
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
