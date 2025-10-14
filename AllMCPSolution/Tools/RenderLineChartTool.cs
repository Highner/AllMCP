using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;

namespace AllMCPSolution.Charts;

[McpTool("render_line_chart", "Returns a deterministic HTML line chart for inline display in ChatGPT")]
public class RenderLineChartTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public RenderLineChartTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    public string Name => "render_line_chart";
    public string Description => "Returns a deterministic HTML line chart (SVG) with your data, styled to always look the same";
    public string? SafetyLevel => "non_critical";

    public class SeriesInput
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "Series";

        [JsonPropertyName("data")]
        public double[] Data { get; set; } = Array.Empty<double>();
    }

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        // Basic validation
        if (parameters == null)
            throw new ArgumentException("Parameters are required");

        var title = parameters.TryGetValue("title", out var tObj) ? tObj?.ToString() ?? "Chart" : "Chart";

        // labels
        string[] labels = Array.Empty<string>();
        if (parameters.TryGetValue("labels", out var labelsObj) && labelsObj is JsonElement lblEl && lblEl.ValueKind == JsonValueKind.Array)
        {
            labels = lblEl.EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
        }

        // series (array of { name, data: number[] })
        var seriesList = new List<SeriesInput>();
        if (parameters.TryGetValue("series", out var seriesObj) && seriesObj is JsonElement sEl && sEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in sEl.EnumerateArray())
            {
                var si = new SeriesInput();
                if (item.TryGetProperty("name", out var nm)) si.Name = nm.GetString() ?? "Series";
                if (item.TryGetProperty("data", out var dt) && dt.ValueKind == JsonValueKind.Array)
                    si.Data = dt.EnumerateArray().Select(v => v.GetDouble()).ToArray();
                seriesList.Add(si);
            }
        }

        if (labels.Length == 0)
            throw new ArgumentException("`labels` (string[]) is required and cannot be empty.");
        if (seriesList.Count == 0)
            throw new ArgumentException("`series` (array) is required and must contain at least one series.");
        foreach (var s in seriesList)
        {
            if (s.Data.Length != labels.Length)
                throw new ArgumentException("Each series.data must be the same length as labels.");
        }

        // Optional fixed Y range
        double? fixedYMin = null, fixedYMax = null;
        if (parameters.TryGetValue("fixedYMin", out var fymin) && fymin is JsonElement fyminEl && fyminEl.ValueKind is JsonValueKind.Number)
            fixedYMin = fyminEl.GetDouble();
        if (parameters.TryGetValue("fixedYMax", out var fymax) && fymax is JsonElement fymaxEl && fymaxEl.ValueKind is JsonValueKind.Number)
            fixedYMax = fymaxEl.GetDouble();
        if (fixedYMin.HasValue ^ fixedYMax.HasValue)
            throw new ArgumentException("Provide both fixedYMin and fixedYMax, or neither.");

        // Build the HTML
        string html = BuildDeterministicChartHtml(title, labels, seriesList, fixedYMin, fixedYMax);

        // Also return as data URL for fallback/opening in a new tab
        var bytes = Encoding.UTF8.GetBytes(html);
        string dataUrl = "data:text/html;base64," + Convert.ToBase64String(bytes);

        // Return a shape optimized for ChatGPT rendering:
        return new
        {
            success = true,
            display = new object[]
            {
                new {
                    type = "html",
                    mime = "text/html",
                    html
                },
                new {
                    type = "resource",
                    name = "chart.html",
                    uri = dataUrl,
                    mime = "text/html"
                }
            },
            // Keep a plain JSON echo of the input for debugging
            meta = new
            {
                title,
                labels,
                series = seriesList,
                fixedYMin,
                fixedYMax
            }
        };
    }

    public object GetToolDefinition()
    {
        return new
        {
            name = Name,
            description = Description,
            safety = new { level = SafetyLevel },
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    title = new { type = "string", description = "Chart title" },
                    labels = new { type = "array", items = new { type = "string" }, description = "X-axis labels" },
                    series = new
                    {
                        type = "array",
                        description = "Array of series; each has a name and numeric data[] matching labels length",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                name = new { type = "string" },
                                data = new { type = "array", items = new { type = "number" } }
                            },
                            required = new[] { "data" }
                        }
                    },
                    fixedYMin = new { type = "number", description = "Optional fixed Y-axis min (must pair with fixedYMax)" },
                    fixedYMax = new { type = "number", description = "Optional fixed Y-axis max (must pair with fixedYMin)" }
                },
                required = new[] { "labels", "series" }
            }
        };
    }

    public object GetOpenApiSchema()
    {
        return new Dictionary<string, object>
        {
            ["operationId"] = Name,
            ["summary"] = Description,
            ["requestBody"] = new Dictionary<string, object>
            {
                ["required"] = true,
                ["content"] = new Dictionary<string, object>
                {
                    ["application/json"] = new Dictionary<string, object>
                    {
                        ["schema"] = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["properties"] = new Dictionary<string, object>
                            {
                                ["title"] = new Dictionary<string, object> { ["type"] = "string" },
                                ["labels"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = new Dictionary<string, object> { ["type"] = "string" } },
                                ["series"] = new Dictionary<string, object>
                                {
                                    ["type"] = "array",
                                    ["items"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "object",
                                        ["properties"] = new Dictionary<string, object>
                                        {
                                            ["name"] = new Dictionary<string, object> { ["type"] = "string" },
                                            ["data"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = new Dictionary<string, object> { ["type"] = "number" } }
                                        },
                                        ["required"] = new[] { "data" }
                                    }
                                },
                                ["fixedYMin"] = new Dictionary<string, object> { ["type"] = "number" },
                                ["fixedYMax"] = new Dictionary<string, object> { ["type"] = "number" }
                            },
                            ["required"] = new[] { "labels", "series" }
                        }
                    }
                }
            },
            ["responses"] = new Dictionary<string, object>
            {
                ["200"] = new Dictionary<string, object>
                {
                    ["description"] = "HTML chart payload suitable for inline display",
                    ["content"] = new Dictionary<string, object>
                    {
                        ["application/json"] = new Dictionary<string, object>
                        {
                            ["schema"] = new Dictionary<string, object>
                            {
                                ["type"] = "object",
                                ["properties"] = new Dictionary<string, object>
                                {
                                    ["success"] = new Dictionary<string, object> { ["type"] = "boolean" },
                                    ["display"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "array",
                                        ["items"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "object"
                                        }
                                    },
                                    ["meta"] = new Dictionary<string, object> { ["type"] = "object" }
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    private static string BuildDeterministicChartHtml(
    string title,
    string[] labels,
    List<SeriesInput> series,
    double? fixedYMin,
    double? fixedYMax)
{
    // --- Config (match the React/JS version) ---
    const int width = 800, height = 400;
    var padding = new { top = 20, right = 24, bottom = 40, left = 56 };
    const int gridLinesY = 6;
    const double dotRadius = 3.0;
    string[] palette = { "#2563eb", "#22c55e", "#ef4444", "#a855f7", "#f59e0b" };

    // --- Helpers ---
    static double NiceNumber(double range, bool round)
    {
        double exponent = Math.Floor(Math.Log10(range));
        double fraction = range / Math.Pow(10, exponent);
        double niceFraction = round
            ? (fraction < 1.5 ? 1 : fraction < 3 ? 2 : fraction < 7 ? 5 : 10)
            : (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10);
        return niceFraction * Math.Pow(10, exponent);
    }

    static (List<double> ticks, double niceMin, double niceMax) GetNiceTicks(double min, double max, int maxTicks)
    {
        double range = NiceNumber(max - min, false);
        double tickSpacing = NiceNumber(range / (maxTicks - 1), true);
        double niceMin = Math.Floor(min / tickSpacing) * tickSpacing;
        double niceMax = Math.Ceiling(max / tickSpacing) * tickSpacing;
        var ticks = new List<double>();
        for (double t = niceMin; t <= niceMax + 1e-9; t += tickSpacing)
            ticks.Add(Math.Round(t, 10));
        return (ticks, niceMin, niceMax);
    }

    static string FormatTick(double v)
    {
        double a = Math.Abs(v);
        if (a >= 1e9) return (v / 1e9).ToString("0.#") + "B";
        if (a >= 1e6) return (v / 1e6).ToString("0.#") + "M";
        if (a >= 1e3) return (v / 1e3).ToString("0.#") + "k";
        if (a >= 1)   return Math.Round(v).ToString();
        return v.ToString("0.##");
    }

    // --- Y scale bounds ---
    var all = series.SelectMany(s => s.Data).ToArray();
    double yMin = all.Min();
    double yMax = all.Max();
    if (yMin == yMax) { yMin -= 1; yMax += 1; }

    List<double> yTicks;
    if (fixedYMin.HasValue && fixedYMax.HasValue)
    {
        yMin = fixedYMin.Value;
        yMax = fixedYMax.Value;
        double yStep = (yMax - yMin) / (gridLinesY - 1); // renamed from step → yStep
        yTicks = Enumerable.Range(0, gridLinesY).Select(i => yMin + i * yStep).ToList();
    }
    else
    {
        var info = GetNiceTicks(yMin, yMax, gridLinesY);
        yTicks = info.ticks;
        yMin   = info.niceMin;
        yMax   = info.niceMax;
    }

    double innerW = width - padding.left - padding.right;
    double innerH = height - padding.top - padding.bottom;

    Func<int, double> xScale = i =>
    {
        int n = labels.Length;
        if (n <= 1) return padding.left + innerW / 2.0;
        return padding.left + (i * innerW) / (n - 1.0);
    };
    Func<double, double> yScale = v =>
        padding.top + innerH * (1.0 - (v - yMin) / (yMax - yMin));

    // Build grid + y labels
    var sb = new StringBuilder();
    sb.AppendLine($@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>{System.Net.WebUtility.HtmlEncode(title)}</title>
  <style>
    html, body {{ margin:0; padding:0; background:#ffffff; }}
    :root {{ --bg:#ffffff; --fg:#0f172a; --muted:#64748b; --grid:#e2e8f0; --axis:#94a3b8; }}
    body {{ font-family: Inter, Segoe UI, Roboto, Helvetica Neue, Arial, sans-serif; color:var(--fg); }}
    .wrap {{ width:820px; padding:10px 10px 18px 10px; box-sizing:border-box; }}
    .title {{ font-size:18px; font-weight:600; margin:6px 0 12px 6px; letter-spacing:.2px; }}
    .chart {{ width:{width}px; height:{height}px; border:1px solid #e5e7eb; border-radius:12px; background:#fff; }}
    text {{ font-size:12px; fill:var(--muted); shape-rendering:crispEdges; }}
  </style>
</head>
<body>
  <div class=""wrap"">
    <div class=""title"">{System.Net.WebUtility.HtmlEncode(title)}</div>
    <div class=""chart"">
      <svg width=""{width}"" height=""{height}"" viewBox=""0 0 {width} {height}"" xmlns=""http://www.w3.org/2000/svg"" aria-label=""Line chart"">");

    // Horizontal grid lines + y tick labels
    foreach (var t in yTicks)
    {
        var y = yScale(t);
        sb.AppendLine($@"<line x1=""{padding.left:F2}"" x2=""{(padding.left + innerW):F2}"" y1=""{y:F2}"" y2=""{y:F2}"" stroke=""#e2e8f0"" stroke-width=""1"" shape-rendering=""crispEdges"" />");
        sb.AppendLine($@"<text x=""{(padding.left - 8):F2}"" y=""{(y + 4):F2}"" text-anchor=""end"" style=""font-size:12px; fill:#64748b; shape-rendering:crispEdges"">{FormatTick(t)}</text>");
    }

    // Axes
    sb.AppendLine($@"<line x1=""{padding.left}"" x2=""{padding.left + innerW}"" y1=""{padding.top + innerH + 1}"" y2=""{padding.top + innerH + 1}"" stroke=""#94a3b8"" stroke-width=""1"" />");
    sb.AppendLine($@"<line x1=""{padding.left}"" x2=""{padding.left}"" y1=""{padding.top}"" y2=""{padding.top + innerH}"" stroke=""#94a3b8"" stroke-width=""1"" />");

    // X labels (sparse ~8)
    int step = Math.Max(1, (int)Math.Ceiling(labels.Length / 8.0));
    for (int i = 0; i < labels.Length; i++)
    {
        if (i % step != 0 && i != labels.Length - 1) continue;
        double x = xScale(i);
        double y = padding.top + innerH + 16;
        string anchor = i == 0 ? "start" : (i == labels.Length - 1 ? "end" : "middle");
        sb.AppendLine($@"<text x=""{x:F2}"" y=""{y:F2}"" text-anchor=""{anchor}"" style=""font-size:12px; fill:#64748b; shape-rendering:crispEdges"">{System.Net.WebUtility.HtmlEncode(labels[i] ?? "")}</text>");
    }

    // Series paths + dots (all static)
    for (int sIdx = 0; sIdx < series.Count; sIdx++)
    {
        var s = series[sIdx];
        string stroke = palette[sIdx % palette.Length];
        var d = new StringBuilder();
        for (int i = 0; i < s.Data.Length; i++)
        {
            double x = xScale(i);
            double y = yScale(s.Data[i]);
            d.Append(i == 0 ? $"M {x:F2} {y:F2}" : $" L {x:F2} {y:F2}");
        }

        sb.AppendLine($@"<path d=""{d}"" fill=""none"" stroke=""{stroke}"" stroke-width=""2"" />");

        for (int i = 0; i < s.Data.Length; i++)
        {
            double x = xScale(i);
            double y = yScale(s.Data[i]);
            sb.AppendLine($@"<circle cx=""{x:F2}"" cy=""{y:F2}"" r=""{dotRadius}"" fill=""#fff"" stroke=""{stroke}"" stroke-width=""1.5"" />");
        }
    }

    sb.AppendLine(@"</svg>
    </div>
  </div>
</body>
</html>");

    return sb.ToString();
}

}
