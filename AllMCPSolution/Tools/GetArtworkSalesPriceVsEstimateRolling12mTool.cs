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

        if (!artistId.HasValue)
        {
            return new { timeSeries = Array.Empty<object>(), count = 0, description = "Artist ID is required." };
        }

        var query = _dbContext.ArtworkSales.Include(a => a.Artist).AsQueryable();

        query = query.Where(a => a.ArtistId == artistId.Value);

        var sales = await query
            .Where(a => a.Sold)
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
                //CountInWindow = totalSales,  // Changed to show total sales, not just contributing months
                Value = rolling
            });
        }

        return new
        {
            timeSeries = series,
            count = series.Count,
            description = "Each monthly point represents the 12-month rolling average of the average monthly Value (normalized within estimate band: <0 below low, 0..1 within, >1 above high)."
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
                properties = new
                {
                    artist_id = new
                    {
                        type = "string",
                        format = "uuid",
                        description = "The unique identifier of the artist"
                    }
                },
                required = new[] { "artist_id" }
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
                required = true,
                content = new
                {
                    application__json = new
                    {
                        schema = new
                        {
                            type = "object",
                            properties = new
                            {
                                artist_id = new
                                {
                                    type = "string",
                                    format = "uuid",
                                    description = "The unique identifier of the artist"
                                }
                            },
                            required = new[] { "artist_id" }
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