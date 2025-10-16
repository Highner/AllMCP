using System.Text.Json;
using System.Text.Json.Nodes;
using AllMCPSolution.Tools;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;
using ModelContextProtocol.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace AllMCPSolution.Artworks;

public class GetArtworkSalesHammerPerAreaRolling12mTool : IMcpTool
{
    public Tool GetDefinition() => new()
    {
        Name = "get_artwork_sales_hammer_per_area_rolling_12m",
        Title = "Hammer Price per Area (12m Rolling)",
        Description = "Returns 12-month rolling averages of hammer price per area (height*width), using inflation-adjusted prices, one data point per month.",
        InputSchema = JsonDocument.Parse("""
{
  "type": "object",
  "properties": {
    "artist_id": { "type": "string", "format": "uuid", "description": "The unique identifier of the artist" },
    "category": { "type": "string", "description": "Filter by artwork category" }
  },
  "required": ["artist_id"]
}
""").RootElement
    };

    public async ValueTask<CallToolResult> RunAsync(CallToolRequestParams request, CancellationToken ct)
    {
        // Resolve required services via a scoped provider (Repository pattern, no direct DbContext access)
        using var scope = AllMCPSolution.Services.ServiceLocator.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IArtworkSaleRepository>();
        var inflation = scope.ServiceProvider.GetRequiredService<IInflationService>();

        var artistIdStr = GetStringArg(request, "artist_id") ?? GetStringArg(request, "artistId");
        if (!Guid.TryParse(artistIdStr, out var artistId))
        {
            return new CallToolResult
            {
                Content = [ new TextContentBlock { Type = "text", Text = "Artist ID is required (uuid)." } ],
                StructuredContent = new JsonObject { ["timeSeries"] = new JsonArray(), ["count"] = 0, ["description"] = "Artist ID is required." }
            };
        }

        var category = GetStringArg(request, "category");

        // Fetch sales via repository
        var categories = string.IsNullOrWhiteSpace(category) ? new List<string>() : new List<string> { category! };
        var sales = await repo.GetSalesAsync(artistId, null, null, categories, ct);
        sales = sales.Where(a => a.Sold && a.SaleDate != default && a.HammerPrice > 0 && a.Height > 0 && a.Width > 0)
                     .OrderBy(a => a.SaleDate)
                     .ToList();

        if (sales.Count == 0)
        {
            return new CallToolResult
            {
                Content = [ new TextContentBlock { Type = "text", Text = "No data found for the specified filters." } ],
                StructuredContent = new JsonObject { ["timeSeries"] = new JsonArray(), ["count"] = 0, ["description"] = "No data found for the specified filters." }
            };
        }

        // Calculate area and thresholds
        var salesWithArea = sales
            .Select(s => new { s.SaleDate, s.HammerPrice, Area = (s.Height * s.Width) })
            .ToList();

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
            var adj = await inflation.AdjustAmountAsync(s.HammerPrice, s.SaleDate);
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

        var structured = new JsonObject
        {
            ["timeSeries"] = new JsonObject
            {
                ["Small"] = ToJsonArray(seriesSmall),
                ["Medium"] = ToJsonArray(seriesMedium),
                ["Large"] = ToJsonArray(seriesLarge)
            },
            ["count"] = new JsonObject
            {
                ["Small"] = seriesSmall.Count,
                ["Medium"] = seriesMedium.Count,
                ["Large"] = seriesLarge.Count
            },
            ["description"] = $"Three size brackets based on area (height×width). Small: ≤{smallThreshold:F2}, Medium: {smallThreshold:F2}-{largeThreshold:F2}, Large: >{largeThreshold:F2}. Each series shows 12-month rolling averages of inflation-adjusted hammer price per area.",
            ["brackets"] = new JsonObject
            {
                ["Small"] = $"Area ≤ {smallThreshold:F2}",
                ["Medium"] = $"Area {smallThreshold:F2} - {largeThreshold:F2}",
                ["Large"] = $"Area > {largeThreshold:F2}"
            }
        };

        return new CallToolResult
        {
            Content = [ new TextContentBlock { Type = "text", Text = "Computed 12-month rolling series for hammer price per area." } ],
            StructuredContent = structured
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