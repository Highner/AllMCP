using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Services;

namespace AllMCPSolution.Artworks;

[McpTool("get_artwork_sales_hammer_price_timeseries", "Returns a time series of hammer prices, including inflation-adjusted hammer prices using ECB HICP.")]
public class GetArtworkSalesHammerPriceTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IInflationService _inflationService;
    private const int MaxResults = 300;

    public GetArtworkSalesHammerPriceTool(ApplicationDbContext dbContext, IInflationService inflationService)
    {
        _dbContext = dbContext;
        _inflationService = inflationService;
    }

    public string Name => "get_artwork_sales_hammer_price_timeseries";
    public string Description => "Returns a time series of artwork hammer prices with inflation-adjusted values (to today's prices) using ECB HICP.";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        parameters ??= new Dictionary<string, object>();

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

        var query = _dbContext.ArtworkSales.Include(a => a.Artist).AsQueryable();

        if (artistId.HasValue) query = query.Where(a => a.ArtistId == artistId.Value);
        if (!string.IsNullOrWhiteSpace(name)) query = query.Where(a => a.Name.Contains(name));
        if (minHeight.HasValue) query = query.Where(a => a.Height >= minHeight.Value);
        if (maxHeight.HasValue) query = query.Where(a => a.Height <= maxHeight.Value);
        if (minWidth.HasValue) query = query.Where(a => a.Width >= minWidth.Value);
        if (maxWidth.HasValue) query = query.Where(a => a.Width <= maxWidth.Value);
        if (yearCreatedFrom.HasValue) query = query.Where(a => a.YearCreated >= yearCreatedFrom.Value);
        if (yearCreatedTo.HasValue) query = query.Where(a => a.YearCreated <= yearCreatedTo.Value);
        if (saleDateFrom.HasValue) query = query.Where(a => a.SaleDate >= saleDateFrom.Value);
        if (saleDateTo.HasValue) query = query.Where(a => a.SaleDate <= saleDateTo.Value);
        if (!string.IsNullOrWhiteSpace(technique)) query = query.Where(a => a.Technique.Contains(technique));
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(a => a.Category.Contains(category));
        if (!string.IsNullOrWhiteSpace(currency)) query = query.Where(a => a.Currency == currency);
        if (minLowEstimate.HasValue) query = query.Where(a => a.LowEstimate >= minLowEstimate.Value);
        if (maxLowEstimate.HasValue) query = query.Where(a => a.LowEstimate <= maxLowEstimate.Value);
        if (minHighEstimate.HasValue) query = query.Where(a => a.HighEstimate >= minHighEstimate.Value);
        if (maxHighEstimate.HasValue) query = query.Where(a => a.HighEstimate <= maxHighEstimate.Value);
        if (minHammerPrice.HasValue) query = query.Where(a => a.HammerPrice >= minHammerPrice.Value);
        if (maxHammerPrice.HasValue) query = query.Where(a => a.HammerPrice <= maxHammerPrice.Value);
        if (sold.HasValue) query = query.Where(a => a.Sold == sold.Value);

        var totalCount = await query.CountAsync();
        var skip = (page - 1) * MaxResults;

        var sales = await query
            .OrderByDescending(a => a.SaleDate)
            .Skip(skip)
            .Take(MaxResults)
            .Select(a => new { a.Name, a.Category, a.Technique, a.YearCreated, a.SaleDate, a.HammerPrice, a.Sold, a.Height, a.Width })
            .ToListAsync();

        var list = new List<object>(sales.Count);
        foreach (var s in sales)
        {
            var area = (s.Height > 0 && s.Width > 0) ? (s.Height * s.Width) : 0m;
            decimal? perArea = area > 0 ? s.HammerPrice / area : null;
            var adj = await _inflationService.AdjustAmountAsync(s.HammerPrice, s.SaleDate);
            decimal? perAreaAdj = area > 0 ? adj / area : null;
            
            list.Add(new
            {
                Title = s.Name,
                Category = s.Category,
                Technique = s.Technique,
                YearCreated = s.YearCreated,
                Time = s.SaleDate,
                Sold = s.Sold,
                HammerPrice = s.HammerPrice,
                HammerPriceInflationAdjusted = adj,
                HammerPricePerArea = perArea,
                HammerPricePerAreaInflationAdjusted = perAreaAdj,
            });
        }

        var totalPages = (int)Math.Ceiling((double)totalCount / MaxResults);
        var hasMoreResults = page < totalPages;

        var result = new
        {
            timeSeries = list,
            count = list.Count,
            totalCount,
            totalPages,
            currentPage = page,
            hasMoreResults,
            description = "HammerPrice is nominal at sale date. HammerPriceInflationAdjusted uses ECB HICP monthly index to adjust to today's prices."
        };

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
                nextPageInstructions = $"To get the next page of results, call this tool again with page={page + 1}.",
                result.description
            };
        }

        if (totalPages > 1)
        {
            return new
            {
                result.timeSeries,
                result.count,
                result.totalCount,
                result.totalPages,
                result.currentPage,
                result.hasMoreResults,
                mergeInstructions = $"This is the final page (page {page} of {totalPages}). If you retrieved multiple pages, merge all timeSeries arrays from all pages into one dataset.",
                result.description
            };
        }

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
