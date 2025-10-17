using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using AllMCPSolution.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AllMCPSolution.Artworks;

[McpTool("get_artwork_sales_price_vs_estimate_rolling_12m", "Returns 12-month rolling averages for price vs estimate metrics, one data point per month.")]
public class GetArtworkSalesPriceVsEstimateRolling12mTool : IToolBase, IMcpTool, IResourceProvider
{
    private readonly IArtworkSaleQueryRepository _repo;
    private const string UiUri = "ui://artworks/price-vs-estimate-rolling-12m.html";

    public GetArtworkSalesPriceVsEstimateRolling12mTool(IArtworkSaleQueryRepository repo)
    {
        _repo = repo;
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

        const string html = """
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

   <script type="module" defer>
  const container = document.getElementById('chartContainer');
  const emptyState = document.getElementById('emptyState');
  const ctx = document.getElementById('trendChart');
  let chart;

  const resolveOutputPayload = (payload) => {
    if (!payload || typeof payload !== 'object') return null;
    if (payload.timeSeries || Array.isArray(payload)) return payload;
    const nestedKeys = ['toolOutput','output','detail','data','payload','result','structuredContent','structured_output','structured'];
    for (const k of nestedKeys) if (payload[k]) {
      const r = resolveOutputPayload(payload[k]); if (r) return r;
    }
    return payload;
  };

  const normalizePoints = (output) => {
    const raw = output && output.timeSeries;
    const arr = Array.isArray(raw) ? raw
              : (raw && typeof raw === 'object') ? Object.values(raw.$values || raw) : [];
    return arr.filter(p => p && typeof p === 'object');
  };

  const render = (output = {}) => {
    const points = normalizePoints(output);

    if (typeof window.Chart === 'undefined') {
      container.hidden = true;
      emptyState.hidden = false;
      emptyState.textContent = (output && output.description) || 'Chart library unavailable.';
      return;
    }

    if (!points.length) {
      if (chart) { chart.destroy(); chart = null; }
      container.hidden = true;
      emptyState.hidden = false;
      emptyState.textContent = output.description || 'No results available.';
      return;
    }

    const labels = points.map(p => new Date(p.Time).toLocaleDateString(undefined, { year: 'numeric', month: 'short' }));
    const values = points.map(p => (typeof p.Value === 'number' ? p.Value : null));

    container.hidden = false;
    emptyState.hidden = true;

    if (!chart) {
      chart = new window.Chart(ctx, {
        type: 'line',
        data: { labels, datasets: [{ label: 'Position in estimate range', data: values, tension: 0.35,
          borderColor: '#2563eb', backgroundColor: 'rgba(37,99,235,0.2)', fill: true, pointRadius: 2, pointHoverRadius: 4 }]},
        options: {
          responsive: true, maintainAspectRatio: false,
          scales: {
            y: { title: { display: true, text: 'Position in estimate range' }, suggestedMin: 0, suggestedMax: 1,
                 ticks: { callback: v => Number(v).toFixed(2) } },
            x: { title: { display: true, text: 'Month' } }
          }
        }
      });
    } else {
      chart.data.labels = labels;
      chart.data.datasets[0].data = values;
      chart.update();
    }
  };

  // --- CRUCIAL: subscribe to the actual host event that carries toolOutput updates
  window.addEventListener('openai:set_globals', (evt) => {
    const payload = evt?.detail?.toolOutput ?? (window.openai && window.openai.toolOutput);
    const resolved = resolveOutputPayload(payload) || {};
    render(resolved);
  });

  // Fallbacks: attach ASAP once window.openai exists, and also handle tool calls from inside the UI
  const tryInitial = () => {
    const payload = (window.openai && window.openai.toolOutput) || {};
    const resolved = resolveOutputPayload(payload) || {};
    render(resolved);
  };

  // Try immediately; if host injects `window.openai` a tick later, catch it here
  if (window.openai) {
    tryInitial();
  } else {
    const t = setInterval(() => {
      if (window.openai) { clearInterval(t); tryInitial(); }
    }, 100);
    // safety stop after ~5s
    setTimeout(() => clearInterval(t), 5000);
  }

  // If you ever call tools from within the component, this event delivers those results
  window.addEventListener('openai:tool_response', (evt) => {
    const payload = evt?.detail?.result ?? evt?.detail;
    const resolved = resolveOutputPayload(payload) || {};
    render(resolved);
  });
</script>

  </body>
</html>
""";

        // IMPORTANT: add widget CSP so the sandbox can load external assets (Chart.js here).
        // If you host assets elsewhere, list that domain instead. :contentReference[oaicite:5]{index=5}
        var meta = new JsonObject
        {
            ["openai/widgetCSP"] = new JsonObject
            {
                ["resource_domains"] = new JsonArray("https://cdn.jsdelivr.net"), // allow <script src="...">
                ["connect_domains"]  = new JsonArray() // add APIs if you later fetch()
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
