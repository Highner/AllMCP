using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Services;

namespace AllMCPSolution.Artworks;

[McpTool("get_artwork_sales_hammer_per_area_rolling_12m", "Returns 12-month rolling averages of hammer price per area (height*width), using inflation-adjusted prices, one data point per month.")]
public class GetArtworkSalesHammerPerAreaRolling12mTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IInflationService _inflationService;

    public GetArtworkSalesHammerPerAreaRolling12mTool(ApplicationDbContext dbContext, IInflationService inflationService)
    {
        _dbContext = dbContext;
        _inflationService = inflationService;
    }

    public string Name => "get_artwork_sales_hammer_per_area_rolling_12m";
    public string Description => "Returns a monthly series where each point is the 12-month rolling average of inflation-adjusted hammer price per area (height*width).";
    public string? SafetyLevel => "non_critical";

  public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        parameters ??= new Dictionary<string, object>();

        var artistId = ParameterHelpers.GetGuidParameter(parameters, "artistId", "artist_id");
        var category = ParameterHelpers.GetStringParameter(parameters, "category", "category");

        if (!artistId.HasValue)
        {
            return new { timeSeries = Array.Empty<object>(), count = 0, description = "Artist ID is required." };
        }

        var query = _dbContext.ArtworkSales.Include(a => a.Artist).AsQueryable();

        query = query.Where(a => a.ArtistId == artistId.Value);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(a => a.Category.Contains(category));

        var sales = await query
            .Where(a => a.Sold)
            .OrderBy(a => a.SaleDate)
            .Select(a => new { a.SaleDate, a.HammerPrice, a.Height, a.Width })
            .ToListAsync();

        if (sales.Count == 0)
        {
            return new { timeSeries = Array.Empty<object>(), count = 0, description = "No data found for the specified filters." };
        }

        // Calculate areas and determine size brackets
        var salesWithArea = sales
            .Select(s => new { 
                s.SaleDate, 
                s.HammerPrice, 
                Area = (s.Height > 0 && s.Width > 0) ? (s.Height * s.Width) : 0m 
            })
            .Where(s => s.Area > 0)
            .ToList();

        if (salesWithArea.Count == 0)
        {
            return new { timeSeries = Array.Empty<object>(), count = 0, description = "No sales with valid area data found." };
        }

        var sortedAreas = salesWithArea.Select(s => s.Area).OrderBy(a => a).ToList();
        var smallThreshold = sortedAreas[sortedAreas.Count / 3];
        var largeThreshold = sortedAreas[(sortedAreas.Count * 2) / 3];

        var firstMonth = new DateTime(salesWithArea.First().SaleDate.Year, salesWithArea.First().SaleDate.Month, 1);
        var lastMonth = new DateTime(salesWithArea.Last().SaleDate.Year, salesWithArea.Last().SaleDate.Month, 1);

        var monthlySmall = new Dictionary<DateTime, (decimal sumPerAreaAdj, int count)>();
        var monthlyMedium = new Dictionary<DateTime, (decimal sumPerAreaAdj, int count)>();
        var monthlyLarge = new Dictionary<DateTime, (decimal sumPerAreaAdj, int count)>();

        foreach (var s in salesWithArea)
        {
            var adj = await _inflationService.AdjustAmountAsync(s.HammerPrice, s.SaleDate);
            var perAreaAdj = adj / s.Area;
            var m = new DateTime(s.SaleDate.Year, s.SaleDate.Month, 1);

            var targetDict = s.Area <= smallThreshold ? monthlySmall 
                           : s.Area <= largeThreshold ? monthlyMedium 
                           : monthlyLarge;

            if (!targetDict.TryGetValue(m, out var agg)) agg = (0m, 0);
            agg.sumPerAreaAdj += perAreaAdj;
            agg.count += 1;
            targetDict[m] = agg;
        }

        var months = new List<DateTime>();
        for (var dt = firstMonth; dt <= lastMonth; dt = dt.AddMonths(1)) months.Add(dt);

        var seriesSmall = BuildRollingSeries(months, monthlySmall);
        var seriesMedium = BuildRollingSeries(months, monthlyMedium);
        var seriesLarge = BuildRollingSeries(months, monthlyLarge);

        return new
        {
            timeSeries = new
            {
                Small = seriesSmall,
                Medium = seriesMedium,
                Large = seriesLarge
            },
            count = new
            {
                Small = seriesSmall.Count,
                Medium = seriesMedium.Count,
                Large = seriesLarge.Count
            },
            description = $"Three size brackets based on area (height×width). Small: ≤{smallThreshold:F2}, Medium: {smallThreshold:F2}-{largeThreshold:F2}, Large: >{largeThreshold:F2}. Each series shows 12-month rolling averages of inflation-adjusted hammer price per area.",
            brackets = new
            {
                Small = $"Area ≤ {smallThreshold:F2}",
                Medium = $"Area {smallThreshold:F2} - {largeThreshold:F2}",
                Large = $"Area > {largeThreshold:F2}"
            }
        };
    }

    private List<object> BuildRollingSeries(List<DateTime> months, Dictionary<DateTime, (decimal sumPerAreaAdj, int count)> monthlyData)
    {
        var monthly = new List<(DateTime Month, decimal AvgPerAreaAdj, int Count)>();
        
        foreach (var m in months)
        {
            if (monthlyData.TryGetValue(m, out var agg))
            {
                monthly.Add((m, agg.sumPerAreaAdj / agg.count, agg.count));
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
                    weightedSum += monthly[j].AvgPerAreaAdj * monthly[j].Count;
                    totalSales += monthly[j].Count;
                }
            }

            decimal? rolling = totalSales > 0 ? weightedSum / totalSales : (decimal?)null;

            series.Add(new
            {
                Time = monthly[i].Month,
                //CountInWindow = totalSales,
                Value = rolling
            });
        }

        return series;
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
                    },
                    category = new
                    {
                        type = "string",
                        description = "Filter by artwork category"
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
                                },
                                category = new
                                {
                                    type = "string",
                                    description = "Filter by artwork category"
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