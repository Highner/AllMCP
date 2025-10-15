using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Artworks;

[McpTool("get_artwork_sales_price_vs_estimate_rolling_12m", "Returns 12-month rolling averages for price vs estimate metrics, one data point per month.")]
public class GetArtworkSalesPriceVsEstimateRolling12mTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public GetArtworkSalesPriceVsEstimateRolling12mTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "get_artwork_sales_price_vs_estimate_rolling_12m";
    public string Description => "Returns a monthly series where each point is the 12-month rolling average of the average monthly PositionInRange (normalized position of hammer price within estimate band).";
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

        var sales = await query
            .OrderBy(a => a.SaleDate)
            .Select(a => new { a.SaleDate, a.LowEstimate, a.HighEstimate, a.HammerPrice })
            .ToListAsync();

        if (sales.Count == 0)
        {
            return new { timeSeries = Array.Empty<object>(), count = 0, description = "No data found for the specified filters." };
        }

        var firstMonth = new DateTime(sales.First().SaleDate.Year, sales.First().SaleDate.Month, 1);
        var lastMonth = new DateTime(sales.Last().SaleDate.Year, sales.Last().SaleDate.Month, 1);

        // Compute monthly average of PositionInRange where defined
        var monthly = new List<(DateTime Month, decimal AvgPositionInRange, int Count)>();
        var indexByMonth = new Dictionary<DateTime, (decimal sumPos, int count)>();

        foreach (var s in sales)
        {
            var low = s.LowEstimate;
            var high = s.HighEstimate;
            decimal? pos = (high > low) ? (s.HammerPrice - low) / (high - low) : (decimal?)null;
            if (pos == null) continue;
            var m = new DateTime(s.SaleDate.Year, s.SaleDate.Month, 1);
            if (!indexByMonth.TryGetValue(m, out var agg)) agg = (0m, 0);
            agg.sumPos += pos.Value;
            agg.count += 1;
            indexByMonth[m] = agg;
        }

        var months = new List<DateTime>();
        for (var dt = firstMonth; dt <= lastMonth; dt = dt.AddMonths(1)) months.Add(dt);

        foreach (var m in months)
        {
            if (indexByMonth.TryGetValue(m, out var agg))
            {
                monthly.Add((m, agg.sumPos / agg.count, agg.count));
            }
            else
            {
                monthly.Add((m, 0m, 0));
            }
        }

        var series = new List<object>(monthly.Count);
        for (int i = 0; i < monthly.Count; i++)
        {
            int start = Math.Max(0, i - 11);
            decimal weightedSum = 0m;
            int totalSales = 0;
            for (int j = start; j <= i; j++)
            {
                if (monthly[j].Count > 0)
                {
                    // Weight each month's average by the number of sales in that month
                    weightedSum += monthly[j].AvgPositionInRange * monthly[j].Count;
                    totalSales += monthly[j].Count;
                }
            }

            decimal? rolling = totalSales > 0 ? weightedSum / totalSales : (decimal?)null;

            series.Add(new
            {
                Time = monthly[i].Month,
                CountInWindow = totalSales,  // Changed to show total sales, not just contributing months
                Rolling12mAvgPositionInRange = rolling
            });
        }

        return new
        {
            timeSeries = series,
            count = series.Count,
            description = "Each monthly point represents the 12-month rolling average of the average monthly PositionInRange (normalized within estimate band: <0 below low, 0..1 within, >1 above high)."
        };
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
                            schema = new { type = "object" }
                        }
                    }
                }
            }
        };
    }
}