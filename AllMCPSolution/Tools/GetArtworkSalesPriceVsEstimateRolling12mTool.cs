using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using AllMCPSolution.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Configuration;
using System.Linq;

namespace AllMCPSolution.Artworks;

[McpTool("get_artwork_sales_price_vs_estimate_rolling_12m", "Returns 12-month rolling averages for price vs estimate metrics, one data point per month.")]
public class GetArtworkSalesPriceVsEstimateRolling12mTool : IToolBase, IMcpTool, IResourceProvider
{
    private readonly IArtworkSaleQueryRepository _repo;
    private readonly IConfiguration _config;
    private const string UiUri = "ui://artworks/price-vs-estimate-rolling-12m.html";

    public GetArtworkSalesPriceVsEstimateRolling12mTool(IArtworkSaleQueryRepository repo, IConfiguration config)
    {
        _repo = repo;
        _config = config;
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

        var query = _repo.ArtworkSales.Where(a => a.ArtistId == artistId.Value);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(a => a.Category.Contains(category));

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
        var lastMonth  = new DateTime(sales.Last().SaleDate.Year,  sales.Last().SaleDate.Month,  1);

        // monthly average of PositionInRange where defined
        var monthly = new List<(DateTime Month, decimal AvgPositionInRange, int Count)>();
        var indexByMonth = new Dictionary<DateTime, (decimal sumPos, int count)>();

        foreach (var s in sales)
        {
            var pos = AllMCPSolution.Services.EstimatePositionHelper.PositionInEstimateRange(
                s.HammerPrice, s.LowEstimate, s.HighEstimate);
            if (pos == null) continue;

            var m = new DateTime(s.SaleDate.Year, s.SaleDate.Month, 1);

            if (indexByMonth.TryGetValue(m, out var agg))
            {
                var (sumPos, count) = agg;
                sumPos += pos.Value;
                count += 1;
                indexByMonth[m] = (sumPos, count);
            }
            else
            {
                indexByMonth[m] = (pos.Value, 1);
            }
        }


        var months = new List<DateTime>();
        for (var dt = firstMonth; dt <= lastMonth; dt = dt.AddMonths(1)) months.Add(dt);

        foreach (var m in months)
        {
            if (indexByMonth.TryGetValue(m, out var agg) && agg.count > 0)
                monthly.Add((m, agg.sumPos / agg.count, agg.count));
            else
                monthly.Add((m, 0m, 0));
        }

        var rolling = AllMCPSolution.Services.RollingAverageHelper.RollingAverage(
            monthly.ConvertAll(x => (x.Month, x.AvgPositionInRange, x.Count)),
            windowMonths: 12,
            weightByCount: true);

        var series = new List<object>(rolling.Count);
        foreach (var p in rolling) series.Add(new { Time = p.Time, Value = p.Value });

        var result = new
        {
            timeSeries = series,
            count = series.Count,
            description = @"Each monthly point represents the 12-month rolling average of the 'position-in-estimate-range' value.

The 'position-in-estimate-range' value represents the normalized position of the hammer price within the auction's estimate band.

It is defined as:
(Hammer – LowEstimate) / (HighEstimate – LowEstimate)

A value of:
• 0.0 → hammer equals the low estimate
• 1.0 → hammer equals the high estimate
• values <0 mean below low estimate
• values >1 mean above high estimate

Example: a value of 0.34 means the hammer was 34% of the way from the low to the high estimate — i.e., slightly above the low estimate but below the midpoint."
        };

        return result;
    }

    // IMcpTool: descriptor with output template & statuses
    public Tool GetDefinition() => new Tool
    {
        Name = Name,
        Title = "Price vs estimate (12m rolling)",
        Description = Description,
        InputSchema = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            type = "object",
            properties = ParameterHelpers.CreateOpenApiProperties(null),
            required = Array.Empty<string>()
        })).RootElement,
        Meta = new JsonObject
        {
            ["openai/outputTemplate"] = UiUri, // Skybridge UI template to render. :contentReference[oaicite:2]{index=2}
            ["openai/toolInvocation/invoking"] = "Computing price vs estimate trend…",
            ["openai/toolInvocation/invoked"]  = "Price vs estimate trend ready!"
        }
    };

    public async ValueTask<CallToolResult> RunAsync(CallToolRequestParams request, CancellationToken ct)
    {
        Dictionary<string, object?>? dict = null;
        if (request?.Arguments is not null)
        {
            dict = new Dictionary<string, object?>();
            foreach (var kvp in request.Arguments)
                dict[kvp.Key] = JsonElementToNet(kvp.Value);
        }

        var result = await ExecuteAsync(dict);
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Type = "text",
                    Text = $"Generated {GetResultCount(result)} monthly points for the 12-month rolling price vs estimate trend."
                }
            ],
            // This is what your UI reads as window.openai.toolOutput. :contentReference[oaicite:3]{index=3}
            StructuredContent = JsonSerializer.SerializeToNode(result) as JsonObject
        };
    }

    private static int GetResultCount(object result)
    {
        var countProp = result.GetType().GetProperty("count");
        if (countProp?.GetValue(result) is int count) return count;
        return 0;
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
                    artist_id = new { type = "string", format = "uuid", description = "The unique identifier of the artist" },
                    category  = new { type = "string", description = "Filter by artwork category" }
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
                                artist_id = new { type = "string", format = "uuid", description = "The unique identifier of the artist" },
                                category  = new { type = "string", description = "Filter by artwork category" }
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
                        application__json = new { schema = new { type = "object" } }
                    }
                }
            }
        };
    }

    public IEnumerable<Resource> ListResources() =>
        new[]
        {
            new Resource
            {
                Name = "artwork-sales-price-vs-estimate-rolling-12m",
                Title = "Artwork price vs estimate (12m rolling)",
                Uri = UiUri,
                MimeType = "text/html+skybridge", // required for Skybridge UIs. :contentReference[oaicite:4]{index=4}
                Description = "Interactive chart of the 12-month rolling price vs estimate trend"
            }
        };

    public ValueTask<ReadResourceResult> ReadResourceAsync(ReadResourceRequestParams request, CancellationToken ct)
    {
        if (request.Uri != UiUri)
            throw new McpException("Resource not found", McpErrorCode.InvalidParams);

        // Build module URL from config (fallback to placeholder if not set)
        var moduleUrl = _config["WidgetAssets:PriceVsEstimateModuleUrl"] ?? "https://cdn.jsdelivr.net/gh/YOUR_ORG/YOUR_REPO@TAG/wwwroot/widgets/price-vs-estimate-widget.js";
        Uri? moduleUri = null;
        if (!Uri.TryCreate(moduleUrl, UriKind.Absolute, out moduleUri)) moduleUri = null;

        // Prepare CSP resource domains: Chart.js origin + module origin
        var resourceDomains = new List<string> { "https://cdn.jsdelivr.net" };
        if (moduleUri != null)
        {
            var origin = $"{moduleUri.Scheme}://{moduleUri.Host}";
            if (!resourceDomains.Contains(origin)) resourceDomains.Add(origin);
        }

        const string htmlTemplate = """
<!doctype html>
<html>
  <head>
    <meta charset="utf-8" />
    <title>Price vs Estimate Rolling 12M</title>
    <script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
    <style>
      :root { color-scheme: light dark; }
      body { font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; margin: 0; padding: 16px; background: var(--openai-body-bg, #fff); color: var(--openai-body-fg, #111827); }
      .card { border: 1px solid rgba(15, 23, 42, 0.1); border-radius: 12px; padding: 20px; background: var(--openai-card-bg, #fff); box-shadow: 0 10px 30px rgba(15, 23, 42, 0.08); }
      h1 { font-size: 1.25rem; margin: 0 0 0.5rem 0; }
      p { margin: 0 0 1rem 0; line-height: 1.5; }
      canvas { width: 100%; height: 360px; }
      .empty { text-align: center; padding: 48px 0; font-size: 1rem; color: rgba(71, 85, 105, 0.8); }
    </style>
  </head>
  <body>
    <div class="card">
      <h1>Price vs Estimate (Rolling 12 Months)</h1>
      <p>Each point represents the 12-month rolling average of the hammer price position within the auction's estimate band.</p>
      <div id="chartContainer">
        <canvas id="trendChart" role="img" aria-label="Price vs estimate rolling 12 month trend"></canvas>
      </div>
      <div id="emptyState" class="empty" hidden>No results available for the selected filters.</div>
    </div>

   <!-- Load Chart.js, defer so it's predictable and ordered -->
<script src="https://cdn.jsdelivr.net/npm/chart.js" defer id="chartjs"></script>

<!-- Your widget module (host this file on your CDN or via jsDelivr) -->
<script type="module" defer src="{{MODULE_URL}}"></script>


  </body>
</html>
""";
        var html = htmlTemplate.Replace("{{MODULE_URL}}", moduleUrl);

        // IMPORTANT: add widget CSP so the sandbox can load external assets (Chart.js and the module).
        var resourceDomainsArray = new JsonArray();
        foreach (var d in resourceDomains) resourceDomainsArray.Add(d);

        var meta = new JsonObject
        {
            ["openai/widgetCSP"] = new JsonObject
            {
                ["resource_domains"] = resourceDomainsArray,
                ["connect_domains"]  = new JsonArray()
            }
        };

        return ValueTask.FromResult(new ReadResourceResult
        {
            Contents =
            [
                new TextResourceContents
                {
                    Uri = UiUri,
                    MimeType = "text/html+skybridge",
                    Text = html,
                    Meta = meta
                }
            ]
        });
    }
}
