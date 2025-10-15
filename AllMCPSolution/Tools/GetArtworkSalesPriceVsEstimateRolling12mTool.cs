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
    public string Description => @"Each monthly point represents the 12-month rolling average of the 'position-in-estimate-range' value.

The 'position-in-estimate-range' value represents the normalized position of the hammer price within the auction's estimate band.";
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
            .Select(a => new { a.SaleDate, a.LowEstimate, a.HighEstimate, a.HammerPrice, a.Height, a.Width })
            .ToListAsync();

        if (sales.Count == 0)
        {
            return new { timeSeries = Array.Empty<object>(), count = 0, description = "No data found for the specified filters." };
        }

        // Calculate areas and determine size brackets for the entire series
        var salesWithArea = sales
            .Select(s => new
            {
                s.SaleDate,
                s.LowEstimate,
                s.HighEstimate,
                s.HammerPrice,
                Area = (s.Height > 0 && s.Width > 0) ? s.Height * s.Width : 0m
            })
            .Where(s => s.Area > 0)
            .ToList();

        if (salesWithArea.Count == 0)
        {
            return new { timeSeries = Array.Empty<object>(), count = 0, description = "No sales with valid dimensions found." };
        }

        var sortedAreas = salesWithArea.Select(s => s.Area).OrderBy(a => a).ToList();
        var thirdIndex = sortedAreas.Count / 3;
        var twoThirdIndex = (sortedAreas.Count * 2) / 3;

        var smallestThirdMax = sortedAreas[thirdIndex - 1];
        var middleThirdMax = sortedAreas[twoThirdIndex - 1];
        var largestThirdMin = sortedAreas[twoThirdIndex];

        var firstMonth = new DateTime(salesWithArea.First().SaleDate.Year, salesWithArea.First().SaleDate.Month, 1);
        var lastMonth = new DateTime(salesWithArea.Last().SaleDate.Year, salesWithArea.Last().SaleDate.Month, 1);

        // Compute monthly average of PositionInRange where defined, grouped by size bracket
        var indexByMonth = new Dictionary<DateTime, (
            decimal sumPosSmall, int countSmall,
            decimal sumPosMedium, int countMedium,
            decimal sumPosLarge, int countLarge
        )>();

        foreach (var s in salesWithArea)
        {
            var low = s.LowEstimate;
            var high = s.HighEstimate;
            decimal? pos = (high > low) ? (s.HammerPrice - low) / (high - low) : (decimal?)null;
            if (pos == null) continue;

            var m = new DateTime(s.SaleDate.Year, s.SaleDate.Month, 1);
            if (!indexByMonth.TryGetValue(m, out var agg))
                agg = (0m, 0, 0m, 0, 0m, 0);

            if (s.Area <= smallestThirdMax)
            {
                agg.sumPosSmall += pos.Value;
                agg.countSmall += 1;
            }
            else if (s.Area <= middleThirdMax)
            {
                agg.sumPosMedium += pos.Value;
                agg.countMedium += 1;
            }
            else
            {
                agg.sumPosLarge += pos.Value;
                agg.countLarge += 1;
            }

            indexByMonth[m] = agg;
        }

        var months = new List<DateTime>();
        for (var dt = firstMonth; dt <= lastMonth; dt = dt.AddMonths(1)) months.Add(dt);

        var monthly = new List<(
            DateTime Month,
            decimal AvgPosSmall, int CountSmall,
            decimal AvgPosMedium, int CountMedium,
            decimal AvgPosLarge, int CountLarge
        )>();

        foreach (var m in months)
        {
            if (indexByMonth.TryGetValue(m, out var agg))
            {
                monthly.Add((
                    m,
                    agg.countSmall > 0 ? agg.sumPosSmall / agg.countSmall : 0m, agg.countSmall,
                    agg.countMedium > 0 ? agg.sumPosMedium / agg.countMedium : 0m, agg.countMedium,
                    agg.countLarge > 0 ? agg.sumPosLarge / agg.countLarge : 0m, agg.countLarge
                ));
            }
            else
            {
                monthly.Add((m, 0m, 0, 0m, 0, 0m, 0));
            }
        }

        var series = new List<object>(monthly.Count);
        for (int i = 0; i < monthly.Count; i++)
        {
            int start = Math.Max(0, i - 11);
            
            decimal weightedSumSmall = 0m, weightedSumMedium = 0m, weightedSumLarge = 0m;
            int totalSmall = 0, totalMedium = 0, totalLarge = 0;

            for (int j = start; j <= i; j++)
            {
                if (monthly[j].CountSmall > 0)
                {
                    weightedSumSmall += monthly[j].AvgPosSmall * monthly[j].CountSmall;
                    totalSmall += monthly[j].CountSmall;
                }
                if (monthly[j].CountMedium > 0)
                {
                    weightedSumMedium += monthly[j].AvgPosMedium * monthly[j].CountMedium;
                    totalMedium += monthly[j].CountMedium;
                }
                if (monthly[j].CountLarge > 0)
                {
                    weightedSumLarge += monthly[j].AvgPosLarge * monthly[j].CountLarge;
                    totalLarge += monthly[j].CountLarge;
                }
            }

            decimal? rollingSmall = totalSmall > 0 ? weightedSumSmall / totalSmall : (decimal?)null;
            decimal? rollingMedium = totalMedium > 0 ? weightedSumMedium / totalMedium : (decimal?)null;
            decimal? rollingLarge = totalLarge > 0 ? weightedSumLarge / totalLarge : (decimal?)null;

            series.Add(new
            {
                Time = monthly[i].Month,
                SmallestThird = new
                {
                    Value = rollingSmall,
                    Count = totalSmall,
                    SizeRange = $"0 - {smallestThirdMax:F2}"
                },
                MiddleThird = new
                {
                    Value = rollingMedium,
                    Count = totalMedium,
                    SizeRange = $"{(smallestThirdMax + 0.01m):F2} - {middleThirdMax:F2}"
                },
                LargestThird = new
                {
                    Value = rollingLarge,
                    Count = totalLarge,
                    SizeRange = $"{largestThirdMin:F2}+"
                }
            });
        }

        return new
        {
            timeSeries = series,
            count = series.Count,
            sizeBreakdown = new
            {
                smallestThird = new { range = $"0 - {smallestThirdMax:F2}", description = "Smallest third by area (Height × Width)" },
                middleThird = new { range = $"{(smallestThirdMax + 0.01m):F2} - {middleThirdMax:F2}", description = "Middle third by area (Height × Width)" },
                largestThird = new { range = $"{largestThirdMin:F2}+", description = "Largest third by area (Height × Width)" }
            },
            description = @"Each monthly point represents the 12-month rolling average of the 'position-in-estimate-range' value, broken down into three size brackets (smallest, middle, and largest thirds by area).

The 'position-in-estimate-range' value represents the normalized position of the hammer price within the auction's estimate band.

It is defined as:
(Hammer – LowEstimate) / (HighEstimate – LowEstimate)

A value of:
• 0.0 → hammer equals the low estimate
• 1.0 → hammer equals the high estimate
• values <0 mean below low estimate
• values >1 mean above high estimate

Example: a value of 0.34 means the hammer was 34% of the way from the low to the high estimate — i.e., slightly above the low estimate but below the midpoint.

Size brackets are calculated from the entire series and represent equal thirds of artworks by area (Height × Width)."
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