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
        // Serialize the data block safely for inlining
        var dataObj = new
        {
            title,
            labels,
            series = series.Select(s => new { name = s.Name, data = s.Data }).ToArray()
        };
        var jsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };
        string dataJson = JsonSerializer.Serialize(dataObj, jsonOptions);

        string fixedYConfig = (fixedYMin.HasValue && fixedYMax.HasValue)
            ? $"fixedY: {{ min: {fixedYMin.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}, max: {fixedYMax.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} }},"
            : "fixedY: null,";

        // Inline, dependency-free SVG/JS (same deterministic renderer as discussed)
        var html = $@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>{System.Net.WebUtility.HtmlEncode(title)}</title>
  <style>
    html, body {{ margin:0; padding:0; background:#ffffff; }}
    :root {{
      --bg:#ffffff; --fg:#0f172a; --muted:#64748b; --grid:#e2e8f0; --axis:#94a3b8;
      --accent:#2563eb; --accent2:#22c55e;
    }}
    body {{ font-family: Inter, Segoe UI, Roboto, Helvetica Neue, Arial, sans-serif; color:var(--fg); }}
    .wrap {{ width:820px; padding:10px 10px 18px 10px; box-sizing:border-box; }}
    .title {{ font-size:18px; font-weight:600; margin:6px 0 12px 6px; letter-spacing:.2px; }}
    .chart {{ width:800px; height:400px; border:1px solid #e5e7eb; border-radius:12px; background:#fff; }}
    text {{ font-size:12px; fill:var(--muted); shape-rendering:crispEdges; }}
  </style>
</head>
<body>
  <div class=""wrap"">
    <div class=""title"" id=""chart-title""></div>
    <div class=""chart"">
      <svg id=""chart"" width=""800"" height=""400"" viewBox=""0 0 800 400"" xmlns=""http://www.w3.org/2000/svg"" aria-label=""Line chart""></svg>
    </div>
  </div>
  <script>
  (function() {{
    'use strict';
    const CONFIG = {{
      width:800, height:400,
      padding: {{ top:20, right:24, bottom:40, left:56 }},
      gridLinesY:6, showDots:true, dotRadius:3,
      {fixedYConfig}
    }};
    function niceNumber(range, round) {{
      const exponent = Math.floor(Math.log10(range));
      const fraction = range / Math.pow(10, exponent);
      let f;
      if (round) {{ if (fraction < 1.5) f=1; else if (fraction<3) f=2; else if (fraction<7) f=5; else f=10; }}
      else {{ if (fraction<=1) f=1; else if (fraction<=2) f=2; else if (fraction<=5) f=5; else f=10; }}
      return f * Math.pow(10, exponent);
    }}
    function getNiceTicks(min, max, maxTicks) {{
      const range = niceNumber(max - min, false);
      const tickSpacing = niceNumber(range / (maxTicks - 1), true);
      const niceMin = Math.floor(min / tickSpacing) * tickSpacing;
      const niceMax = Math.ceil(max / tickSpacing) * tickSpacing;
      const ticks = [];
      for (let t = niceMin; t <= niceMax + 1e-9; t += tickSpacing) ticks.push(Number(t.toFixed(10)));
      return {{ ticks, niceMin, niceMax, tickSpacing }};
    }}
    function formatTick(v) {{
      const a = Math.abs(v);
      if (a>=1e9) return (v/1e9).toFixed(1).replace(/\\.0$/,'')+'B';
      if (a>=1e6) return (v/1e6).toFixed(1).replace(/\\.0$/,'')+'M';
      if (a>=1e3) return (v/1e3).toFixed(1).replace(/\\.0$/,'')+'k';
      if (a>=1) return String(Math.round(v));
      return v.toFixed(2).replace(/\\.00$/,'');
    }}
    function render({{ title, labels, series }}) {{
      const svg = document.getElementById('chart');
      const chartTitle = document.getElementById('chart-title');
      chartTitle.textContent = title || '';
      while (svg.firstChild) svg.removeChild(svg.firstChild);
      const {{ width, height, padding, gridLinesY }} = CONFIG;
      const innerW = width - padding.left - padding.right;
      const innerH = height - padding.top - padding.bottom;
      const all = series.flatMap(s => s.data);
      let yMin = Math.min.apply(null, all), yMax = Math.max.apply(null, all);
      if (yMin === yMax) {{ yMin -= 1; yMax += 1; }}
      let ticksInfo;
      if (CONFIG.fixedY && isFinite(CONFIG.fixedY.min) && isFinite(CONFIG.fixedY.max)) {{
        yMin = CONFIG.fixedY.min; yMax = CONFIG.fixedY.max;
        const step = (yMax - yMin) / (gridLinesY - 1);
        const ticks = Array.from({{length:gridLinesY}}, (_, i) => yMin + i*step);
        ticksInfo = {{ ticks, niceMin: yMin, niceMax: yMax, tickSpacing: step }};
      }} else {{
        ticksInfo = getNiceTicks(yMin, yMax, gridLinesY);
        yMin = ticksInfo.niceMin; yMax = ticksInfo.niceMax;
      }}
      const xScale = i => labels.length<=1 ? padding.left + innerW/2 : padding.left + (i*innerW)/(labels.length-1);
      const yScale = v => padding.top + innerH * (1 - (v - yMin)/(yMax - yMin));
      // grid + y labels
      const grid = document.createElementNS('http://www.w3.org/2000/svg','g');
      ticksInfo.ticks.forEach(t => {{
        const y = yScale(t);
        const line = document.createElementNS('http://www.w3.org/2000/svg','line');
        line.setAttribute('x1', padding.left); line.setAttribute('x2', padding.left+innerW);
        line.setAttribute('y1', y); line.setAttribute('y2', y);
        line.setAttribute('stroke', '#e2e8f0'); line.setAttribute('stroke-width', '1');
        line.setAttribute('shape-rendering','crispEdges');
        grid.appendChild(line);
        const label = document.createElementNS('http://www.w3.org/2000/svg','text');
        label.setAttribute('x', padding.left-8); label.setAttribute('y', y+4); label.setAttribute('text-anchor','end');
        label.setAttribute('style','font-size:12px; fill:#64748b; shape-rendering:crispEdges;');
        label.textContent = formatTick(t);
        grid.appendChild(label);
      }});
      svg.appendChild(grid);
      // axes
      [['line', {{x1:padding.left, x2:padding.left+innerW, y1:padding.top+innerH+1, y2:padding.top+innerH+1}}],
       ['line', {{x1:padding.left, x2:padding.left, y1:padding.top, y2:padding.top+innerH}}]]
      .forEach(([tag, attrs]) => {{
        const el = document.createElementNS('http://www.w3.org/2000/svg', tag);
        Object.entries(attrs).forEach(([k,v]) => el.setAttribute(k, v));
        el.setAttribute('stroke','#94a3b8'); el.setAttribute('stroke-width','1');
        svg.appendChild(el);
      }});
      // x labels (sparse)
      const step = Math.ceil(labels.length/8);
      labels.forEach((lbl, i) => {{
        if (i % step !== 0 && i !== labels.length-1) return;
        const x = xScale(i), y = padding.top+innerH+16;
        const tx = document.createElementNS('http://www.w3.org/2000/svg','text');
        tx.setAttribute('x', x); tx.setAttribute('y', y);
        tx.setAttribute('text-anchor', i===0?'start':(i===labels.length-1?'end':'middle'));
        tx.setAttribute('style','font-size:12px; fill:#64748b; shape-rendering:crispEdges;');
        tx.textContent = String(lbl); svg.appendChild(tx);
      }});
      // series
      const palette = ['#2563eb','#22c55e','#ef4444','#a855f7','#f59e0b'];
      series.forEach((s, si) => {{
        let d = '';
        s.data.forEach((v,i) => {{
          const x = xScale(i), y = yScale(v);
          d += i === 0 ? (""M "" + x + "" "" + y) : ("" L "" + x + "" "" + y);
        }});
        const path = document.createElementNS('http://www.w3.org/2000/svg','path');
        const col = palette[si % palette.length];
        path.setAttribute('d', d); path.setAttribute('fill','none'); path.setAttribute('stroke', col); path.setAttribute('stroke-width','2');
        svg.appendChild(path);
        s.data.forEach((v,i) => {{
          const dot = document.createElementNS('http://www.w3.org/2000/svg','circle');
          dot.setAttribute('cx', xScale(i)); dot.setAttribute('cy', yScale(v));
          dot.setAttribute('r', 3); dot.setAttribute('fill','#fff'); dot.setAttribute('stroke', col); dot.setAttribute('stroke-width','1.5');
          svg.appendChild(dot);
        }});
      }});
    }}
    const DATA = {dataJson};
    render(DATA);
  }})();
  </script>
</body>
</html>";
        return html;
    }
}
