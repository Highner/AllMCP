using System.Text.Json;
using System.Text.Json.Nodes;
using AllMCPSolution.Tools;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using AllMCPSolution.Attributes;

namespace AllMCPSolution.Artworks;

[McpTool("get_artwork_sales_hammer_per_area_rolling_12m", "Returns 12-month rolling averages of hammer price per area (height*width), using inflation-adjusted prices, one data point per month.")]
public class GetArtworkSalesHammerPerAreaRolling12mTool //: IToolBase, IMcpTool
{
    private readonly IArtworkSaleRepository _repo;
    private readonly IInflationService _inflation;

    public GetArtworkSalesHammerPerAreaRolling12mTool(IArtworkSaleRepository repo, IInflationService inflation)
    {
        _repo = repo;
        _inflation = inflation;
    }

    public string Name => "get_artwork_sales_hammer_per_area_rolling_12m";
    public string Description => "Returns 12-month rolling averages of hammer price per area (height*width), using inflation-adjusted prices, one data point per month.";
    public string? SafetyLevel => "non_critical";
    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        parameters ??= new Dictionary<string, object>();
        var artistId = ParameterHelpers.GetGuidParameter(parameters, "artistId", "artist_id");
        var category = ParameterHelpers.GetStringParameter(parameters, "category", "category");
        if (!artistId.HasValue)
        {
            return new { timeSeries = new { Small = Array.Empty<object>(), Medium = Array.Empty<object>(), Large = Array.Empty<object>() }, count = 0, description = "Artist ID is required." };
        }

        var categories = string.IsNullOrWhiteSpace(category) ? new List<string>() : new List<string> { category! };
        var sales = await _repo.GetSalesAsync(artistId.Value, null, null, categories, CancellationToken.None);
        sales = sales.Where(a => a.Sold && a.SaleDate != default && a.HammerPrice > 0 && a.Height > 0 && a.Width > 0)
                     .OrderBy(a => a.SaleDate)
                     .ToList();
        if (sales.Count == 0)
        {
            return new { timeSeries = new { Small = Array.Empty<object>(), Medium = Array.Empty<object>(), Large = Array.Empty<object>() }, count = 0, description = "No data found for the specified filters." };
        }

        var salesWithArea = sales.Select(s => new { s.SaleDate, s.HammerPrice, Area = (s.Height * s.Width) }).ToList();
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
            var adj = await _inflation.AdjustAmountAsync(s.HammerPrice, s.SaleDate);
            var perAreaAdj = adj / s.Area;
            var m = new DateTime(s.SaleDate.Year, s.SaleDate.Month, 1);
            var targetDict = s.Area <= smallThreshold ? monthlySmall : s.Area <= largeThreshold ? monthlyMedium : monthlyLarge;
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

        var result = new
        {
            timeSeries = new { Small = seriesSmall, Medium = seriesMedium, Large = seriesLarge },
            count = new { Small = seriesSmall.Count, Medium = seriesMedium.Count, Large = seriesLarge.Count },
            description = $"Three size brackets based on area (height×width). Small: ≤{smallThreshold:F2}, Medium: {smallThreshold:F2}-{largeThreshold:F2}, Large: >{largeThreshold:F2}. Each series shows 12-month rolling averages of inflation-adjusted hammer price per area.",
            brackets = new { Small = $"Area ≤ {smallThreshold:F2}", Medium = $"Area {smallThreshold:F2} - {largeThreshold:F2}", Large = $"Area > {largeThreshold:F2}" }
        };
        return result;
    }

    public Tool GetDefinition() => new()
    {
        Name = Name,
        Title = "Hammer Price per Area (12m Rolling)",
        Description = Description,
        InputSchema = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            type = "object",
            properties = ParameterHelpers.CreateOpenApiProperties(null),
            required = Array.Empty<string>()
        })).RootElement
    };

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
                            schema = new { type = "object" }
                        }
                    }
                }
            }
        };
    }

    public async ValueTask<CallToolResult> RunAsync(CallToolRequestParams request, CancellationToken ct)
    {
        Dictionary<string, object?>? dict = null;
        if (request?.Arguments is not null)
        {
            dict = new Dictionary<string, object?>();
            foreach (var kvp in request.Arguments)
            {
                dict[kvp.Key] = kvp.Value.ValueKind == JsonValueKind.String ? (object?)kvp.Value.GetString() :
                                 kvp.Value.ValueKind == JsonValueKind.Number ? (object?)(kvp.Value.TryGetDecimal(out var d) ? d : null) :
                                 kvp.Value.ValueKind == JsonValueKind.True ? true :
                                 kvp.Value.ValueKind == JsonValueKind.False ? false : null;
            }
        }

        var result = await ExecuteAsync(dict);
        return new CallToolResult
        {
            StructuredContent = JsonSerializer.SerializeToNode(result) as JsonObject
        };
    }

    private static string? GetStringArg(CallToolRequestParams req, string key)
    {
        if (req.Arguments is null) return null;
        if (!req.Arguments.TryGetValue(key, out var el)) return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    private static JsonArray ToJsonArray(List<object> list)
    {
        var arr = new JsonArray();
        foreach (var item in list)
        {
            // item is anonymous object with Time and Value
            var t = (DateTime)item.GetType().GetProperty("Time")!.GetValue(item)!;
            var v = (decimal)item.GetType().GetProperty("Value")!.GetValue(item)!;
            arr.Add(new JsonObject { ["time"] = t, ["value"] = v });
        }
        return arr;
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

        var rolling = RollingAverageHelper.RollingAverage(
            monthly.ConvertAll(x => (x.Month, x.AvgPerAreaAdj, x.Count)),
            windowMonths: 12,
            weightByCount: true);

        var series = new List<object>(rolling.Count);
        foreach (var p in rolling)
        {
            series.Add(new { Time = p.Time, Value = p.Value });
        }

        return series;
    }

}