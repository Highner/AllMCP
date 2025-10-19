using System;
using System.Linq;
using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Repositories;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AllMCPSolution.Tools;

[McpTool("wine_inventory_overview", "Summarizes undrunk bottles by geography and vintage for quick inventory insights.")]
public sealed class WineInventoryOverviewTool : IToolBase, IMcpTool, IResourceProvider
{
    private readonly IBottleRepository _bottles;
    private const string UiUri = "ui://wine/inventory-overview.html";

    public WineInventoryOverviewTool(IBottleRepository bottles)
    {
        _bottles = bottles;
    }

    public string Name => "wine_inventory_overview";
    public string Description => "Summarizes undrunk bottles by region/appellation and vintage.";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        var locations = await _bottles.GetActiveBottleLocationsAsync();
        var capturedAt = DateTimeOffset.UtcNow;

        var totalBottles = locations.Count;

        var regions = locations
            .GroupBy(l => new
            {
                l.RegionId,
                Name = string.IsNullOrWhiteSpace(l.RegionName) ? "Unknown region" : l.RegionName!.Trim()
            })
            .Select(regionGroup => new
            {
                regionId = regionGroup.Key.RegionId,
                regionName = regionGroup.Key.Name,
                count = regionGroup.Count(),
                appellations = regionGroup
                    .GroupBy(a => new
                    {
                        a.AppellationId,
                        Name = string.IsNullOrWhiteSpace(a.AppellationName) ? "Unknown appellation" : a.AppellationName!.Trim()
                    })
                    .Select(appGroup => new
                    {
                        appellationId = appGroup.Key.AppellationId,
                        appellationName = appGroup.Key.Name,
                        count = appGroup.Count()
                    })
                    .OrderByDescending(a => a.count)
                    .ThenBy(a => a.appellationName)
                    .ToList()
            })
            .OrderByDescending(r => r.count)
            .ThenBy(r => r.regionName)
            .ToList();

        var totalAppellations = regions.Sum(r => r.appellations.Count);

        var vintages = locations
            .GroupBy(l => l.Vintage)
            .Select(group => new
            {
                vintage = group.Key,
                count = group.Count()
            })
            .OrderByDescending(v => v.count)
            .ThenBy(v => v.vintage ?? int.MaxValue)
            .ToList();

        return new
        {
            success = true,
            generatedAt = capturedAt,
            totalBottles,
            totals = new
            {
                regions = regions.Count,
                appellations = totalAppellations,
                vintages = vintages.Count
            },
            regions,
            vintages
        };
    }

    public object GetToolDefinition() => new
    {
        name = Name,
        description = Description,
        safety = new
        {
            level = SafetyLevel
        },
        inputSchema = new
        {
            type = "object",
            properties = new { },
            required = Array.Empty<string>()
        }
    };

    public object GetOpenApiSchema() => new
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
                        type = "object"
                    }
                }
            }
        },
        responses = new
        {
            _200 = new
            {
                description = "Wine inventory overview grouped by geography and vintage.",
                content = new
                {
                    application__json = new
                    {
                        schema = new
                        {
                            type = "object"
                        }
                    }
                }
            }
        }
    };

    public Tool GetDefinition() => new()
    {
        Name = Name,
        Title = "Wine inventory overview",
        Description = Description,
        InputSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {},
          "required": []
        }
        """).RootElement,
        Meta = new JsonObject
        {
            ["openai/outputTemplate"] = UiUri,
            ["openai/toolInvocation/invoking"] = "Summarizing wine inventory…",
            ["openai/toolInvocation/invoked"] = "Wine inventory overview ready"
        }
    };

    public async ValueTask<CallToolResult> RunAsync(CallToolRequestParams request, CancellationToken ct)
    {
        Dictionary<string, object?>? dict = null;
        if (request?.Arguments is not null)
        {
            dict = new Dictionary<string, object?>();
            foreach (var kv in request.Arguments)
            {
                dict[kv.Key] = kv.Value.ValueKind switch
                {
                    JsonValueKind.String => kv.Value.GetString(),
                    JsonValueKind.Number => kv.Value.TryGetInt32(out var i)
                        ? i
                        : kv.Value.TryGetInt64(out var l)
                            ? l
                            : kv.Value.TryGetDouble(out var d)
                                ? d
                                : null,
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null
                };
            }
        }

        var result = await ExecuteAsync(dict);
        var node = JsonSerializer.SerializeToNode(result) as JsonObject;
        var message = "Wine inventory overview ready.";
        if (node?.TryGetPropertyValue("totalBottles", out var totalNode) == true && totalNode is JsonValue totalValue && totalValue.TryGetValue<int>(out var total))
        {
            message = total == 1 ? "1 undrunk bottle remaining." : $"{total} undrunk bottles remaining.";
        }

        return new CallToolResult
        {
            Content = new[]
            {
                new TextContentBlock { Type = "text", Text = message }
            },
            StructuredContent = node
        };
    }

    public IEnumerable<Resource> ListResources() => new[]
    {
        new Resource
        {
            Name = "wine-inventory-overview-ui",
            Title = "Wine inventory overview",
            Uri = UiUri,
            MimeType = "text/html+skybridge",
            Description = "Interactive inventory summary for wine bottles"
        }
    };

    public ValueTask<ReadResourceResult> ReadResourceAsync(ReadResourceRequestParams request, CancellationToken ct)
    {
        if (!string.Equals(request.Uri, UiUri, StringComparison.Ordinal))
        {
            throw new McpException("Resource not found", McpErrorCode.InvalidParams);
        }

        const string html = """
<!doctype html>
<html>
  <head>
    <meta charset="utf-8" />
    <title>Wine inventory overview</title>
    <style>
      :root {
        color-scheme: light dark;
        font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
      }
      body {
        margin: 0;
        padding: 16px;
        background: transparent;
        color: rgb(31 41 55);
      }
      .card {
        border: 1px solid rgb(229 231 235 / 0.7);
        border-radius: 14px;
        padding: 20px;
        background: rgb(255 255 255 / 0.85);
        backdrop-filter: blur(10px);
        box-shadow: 0 12px 40px rgb(15 23 42 / 0.08);
      }
      header h1 {
        margin: 0 0 0.25rem;
        font-size: 1.35rem;
      }
      header p {
        margin: 0;
        color: rgb(75 85 99);
        font-size: 0.95rem;
      }
      .meta {
        display: flex;
        gap: 12px;
        flex-wrap: wrap;
        margin-top: 12px;
      }
      .meta span {
        font-size: 0.85rem;
        color: rgb(107 114 128);
        background: rgb(241 245 249 / 0.8);
        border-radius: 999px;
        padding: 4px 10px;
      }
      .grid {
        display: grid;
        gap: 20px;
        margin-top: 20px;
      }
      @media (min-width: 900px) {
        .grid {
          grid-template-columns: 1fr 1fr;
        }
      }
      .panel {
        border: 1px solid rgb(226 232 240 / 0.8);
        border-radius: 12px;
        padding: 16px;
        background: rgb(255 255 255 / 0.9);
        min-height: 280px;
        display: flex;
        flex-direction: column;
        gap: 12px;
      }
      .panel h2 {
        margin: 0;
        font-size: 1.05rem;
      }
      canvas {
        width: 100%;
        flex: 1;
      }
      #regionHeader {
        display: flex;
        justify-content: space-between;
        align-items: center;
        gap: 12px;
      }
      #resetRegion {
        border: none;
        border-radius: 999px;
        padding: 6px 12px;
        font-size: 0.8rem;
        cursor: pointer;
        background: rgb(37 99 235 / 0.12);
        color: rgb(37 99 235);
      }
      #resetRegion[hidden] {
        display: none;
      }
      .empty {
        text-align: center;
        padding: 48px 0;
        color: rgb(107 114 128);
        font-size: 0.95rem;
      }
    </style>
  </head>
  <body>
    <div class="card" role="region" aria-label="Wine inventory overview">
      <header>
        <h1>Wine inventory overview</h1>
        <p id="summary">Loading wine inventory…</p>
        <div class="meta" id="totalsList"></div>
      </header>
      <div id="emptyState" class="empty" hidden>No undrunk bottles found.</div>
      <div class="grid" id="charts">
        <section class="panel" aria-live="polite">
          <div id="regionHeader">
            <h2 id="regionTitle">By region</h2>
            <button id="resetRegion" type="button" hidden>Back to regions</button>
          </div>
          <canvas id="regionChart" role="img" aria-label="Distribution of undrunk bottles by region"></canvas>
        </section>
        <section class="panel">
          <h2>By vintage</h2>
          <canvas id="vintageChart" role="img" aria-label="Distribution of undrunk bottles by vintage"></canvas>
        </section>
      </div>
    </div>

    <script src="https://cdn.jsdelivr.net/npm/chart.js" defer id="chartjs"></script>
    <script type="module">
      const summary = document.getElementById('summary');
      const totalsList = document.getElementById('totalsList');
      const emptyState = document.getElementById('emptyState');
      const chartsContainer = document.getElementById('charts');
      const regionCanvas = document.getElementById('regionChart');
      const vintageCanvas = document.getElementById('vintageChart');
      const regionTitle = document.getElementById('regionTitle');
      const resetRegionBtn = document.getElementById('resetRegion');

      const regionCtx = regionCanvas?.getContext?.('2d') ?? regionCanvas;
      const vintageCtx = vintageCanvas?.getContext?.('2d') ?? vintageCanvas;

      const safeStringify = (value) => {
        const seen = new WeakSet();
        return JSON.stringify(value, (key, val) => {
          if (typeof val === 'object' && val !== null) {
            if (seen.has(val)) {
              return;
            }
            seen.add(val);
          }
          return val;
        });
      };

      const tryParse = (value) => {
        if (typeof value !== 'string') return value;
        try { return JSON.parse(value); } catch { return value; }
      };

      const normalizeList = (value) => {
        if (Array.isArray(value)) return value;
        if (!value || typeof value !== 'object') return [];
        if (Array.isArray(value.$values)) return value.$values;
        return Object.values(value);
      };

      const resolvePayload = (candidate) => {
        if (candidate == null) return null;
        if (typeof candidate === 'string') return resolvePayload(tryParse(candidate));
        if (candidate.success !== undefined && (candidate.regions !== undefined || candidate.vintages !== undefined)) {
          return candidate;
        }

        const keys = [
          'structuredContent', 'structured_content', 'data', 'payload', 'result',
          'toolOutput', 'output', 'detail', 'response', 'message'
        ];
        for (const key of keys) {
          if (candidate && typeof candidate === 'object' && key in candidate) {
            const resolved = resolvePayload(candidate[key]);
            if (resolved) return resolved;
          }
        }
        return candidate;
      };

      const palette = [
        '#0ea5e9', '#3b82f6', '#6366f1', '#a855f7', '#ec4899',
        '#f97316', '#facc15', '#22c55e', '#14b8a6', '#8b5cf6',
        '#10b981', '#ef4444', '#93c5fd', '#f472b6', '#fcd34d'
      ];

      const getColors = (count) => {
        const colors = [];
        for (let i = 0; i < count; i += 1) {
          colors.push(palette[i % palette.length]);
        }
        return colors;
      };

      let regionChart = null;
      let vintageChart = null;
      let latestPayload = null;
      let chartReady = typeof window.Chart !== 'undefined';
      let currentRegionKey = null;

      const computeRegionKey = (region) => {
        const id = region?.regionId ?? region?.RegionId ?? 'null';
        const name = region?.regionName ?? region?.RegionName ?? '';
        return `${id}|${name}`;
      };

      const updateSummary = (payload) => {
        const total = typeof payload?.totalBottles === 'number' ? payload.totalBottles : 0;
        summary.textContent = total === 0
          ? 'No undrunk bottles in inventory.'
          : `${total} undrunk bottle${total === 1 ? '' : 's'} in inventory.`;

        const totals = payload?.totals ?? {};
        totalsList.innerHTML = '';
        const entries = [
          ['Regions', totals.regions],
          ['Appellations', totals.appellations],
          ['Distinct vintages', totals.vintages]
        ];
        for (const [label, value] of entries) {
          if (typeof value === 'number') {
            const chip = document.createElement('span');
            chip.textContent = `${value} ${label.toLowerCase()}`;
            totalsList.append(chip);
          }
        }
      };

      const updateRegionChart = () => {
        if (!chartReady || !window.Chart || !latestPayload) return;
        const regions = normalizeList(latestPayload.regions);
        let slices = [];
        let title = 'By region';
        resetRegionBtn.hidden = true;

        if (currentRegionKey) {
          const region = regions.find(r => computeRegionKey(r) === currentRegionKey);
          const label = region?.regionName ?? region?.RegionName ?? 'Selected region';
          const appellations = normalizeList(region?.appellations);
          if (region && appellations.length > 0) {
            slices = appellations.map(app => ({
              label: app?.appellationName ?? app?.AppellationName ?? 'Unknown appellation',
              value: typeof app?.count === 'number' ? app.count : app?.Count ?? 0
            }));
            title = `Appellations in ${label}`;
            resetRegionBtn.hidden = false;
          } else {
            currentRegionKey = null;
          }
        }

        if (!currentRegionKey) {
          slices = regions.map(r => {
            const label = r?.regionName ?? r?.RegionName ?? 'Unknown region';
            const value = typeof r?.count === 'number' ? r.count : r?.Count ?? 0;
            const key = computeRegionKey(r);
            const hasAppellations = normalizeList(r?.appellations).length > 0;
            return { label, value, key, hasAppellations };
          });
        }

        const labels = slices.map(s => s.label ?? 'Unknown');
        const data = slices.map(s => typeof s.value === 'number' ? s.value : 0);
        const colors = getColors(labels.length);

        if (!regionChart) {
          regionChart = new Chart(regionCtx, {
            type: 'pie',
            data: {
              labels,
              datasets: [{
                data,
                backgroundColor: colors,
                borderWidth: 1,
                borderColor: '#ffffff'
              }]
            },
            options: {
              responsive: true,
              plugins: {
                legend: { position: 'bottom' }
              }
            }
          });
        } else {
          regionChart.data.labels = labels;
          regionChart.data.datasets[0].data = data;
          regionChart.data.datasets[0].backgroundColor = colors;
          regionChart.update();
        }

        regionTitle.textContent = title;

        if (currentRegionKey) {
          regionChart.options.onClick = null;
        } else {
          regionChart.options.onClick = (_evt, elements) => {
            if (!elements?.length) return;
            const index = elements[0].index;
            const selected = slices[index];
            if (!selected || !selected.hasAppellations) return;
            currentRegionKey = selected.key;
            updateRegionChart();
          };
        }
      };

      const updateVintageChart = () => {
        if (!chartReady || !window.Chart || !latestPayload) return;
        const vintages = normalizeList(latestPayload.vintages).map(v => ({
          label: v?.vintage ?? v?.Vintage ?? null,
          count: typeof v?.count === 'number' ? v.count : v?.Count ?? 0
        }));

        const labels = vintages.map(v => v.label === null ? 'Unknown' : `${v.label}`);
        const data = vintages.map(v => v.count);
        const colors = getColors(labels.length);

        if (!vintageChart) {
          vintageChart = new Chart(vintageCtx, {
            type: 'bar',
            data: {
              labels,
              datasets: [{
                label: 'Bottles',
                data,
                backgroundColor: colors,
                borderRadius: 6
              }]
            },
            options: {
              responsive: true,
              scales: {
                y: {
                  beginAtZero: true,
                  ticks: {
                    precision: 0
                  }
                }
              }
            }
          });
        } else {
          vintageChart.data.labels = labels;
          vintageChart.data.datasets[0].data = data;
          vintageChart.data.datasets[0].backgroundColor = colors;
          vintageChart.update();
        }
      };

      const render = (payload) => {
        latestPayload = payload;
        const total = typeof payload?.totalBottles === 'number' ? payload.totalBottles : 0;
        const hasData = total > 0;

        chartsContainer.hidden = !hasData;
        emptyState.hidden = hasData;

        updateSummary(payload);

        if (!hasData) {
          if (regionChart) { regionChart.destroy(); regionChart = null; }
          if (vintageChart) { vintageChart.destroy(); vintageChart = null; }
          currentRegionKey = null;
          regionTitle.textContent = 'By region';
          resetRegionBtn.hidden = true;
          return;
        }

        if (!chartReady) return;

        updateRegionChart();
        updateVintageChart();
      };

      const onChartReady = () => {
        chartReady = true;
        if (latestPayload) {
          updateRegionChart();
          updateVintageChart();
        }
      };

      if (!chartReady) {
        const script = document.getElementById('chartjs');
        if (script) script.addEventListener('load', onChartReady, { once: true });
        const poll = setInterval(() => {
          if (typeof window.Chart !== 'undefined') {
            clearInterval(poll);
            onChartReady();
          }
        }, 100);
        setTimeout(() => clearInterval(poll), 5000);
      }

      resetRegionBtn.addEventListener('click', () => {
        currentRegionKey = null;
        updateRegionChart();
      });

      const handlePayload = (raw) => {
        const resolved = resolvePayload(raw);
        if (!resolved) return;
        if (resolved.success === false) {
          summary.textContent = resolved.error ?? 'Unable to load wine inventory.';
          chartsContainer.hidden = true;
          emptyState.hidden = false;
          if (regionChart) { regionChart.destroy(); regionChart = null; }
          if (vintageChart) { vintageChart.destroy(); vintageChart = null; }
          currentRegionKey = null;
          return;
        }
        currentRegionKey = null;
        render(resolved);
      };

      const attachListeners = () => {
        const openai = window.openai;
        if (!openai) return false;

        if (openai.toolOutput) handlePayload(openai.toolOutput);
        if (openai.message?.toolOutput) handlePayload(openai.message.toolOutput);

        if (typeof openai.subscribeToToolOutput === 'function') {
          openai.subscribeToToolOutput(handlePayload);
        } else if (typeof openai.onToolOutput === 'function') {
          openai.onToolOutput(handlePayload);
        }
        return true;
      };

      (() => {
        let lastStamp = null;
        const readCandidate = () =>
          window.openai?.toolOutput ??
          window.openai?.message?.toolOutput ??
          null;

        const tick = () => {
          const candidate = readCandidate();
          if (!candidate) return;
          const stamp = `${candidate?.id ?? candidate?.$id ?? ''}|${candidate?.timestamp ?? candidate?.time ?? ''}|${safeStringify(candidate)?.slice(0, 2048)}`;
          if (stamp && stamp !== lastStamp) {
            lastStamp = stamp;
            handlePayload(candidate);
          }
        };

        const interval = setInterval(tick, 300);
        window.addEventListener('beforeunload', () => clearInterval(interval));
      })();

      let attached = attachListeners();
      if (!attached) {
        window.addEventListener('openai:set_globals', () => {
          if (!attached) attached = attachListeners();
        });

        window.addEventListener('openai:tool-output', (evt) => handlePayload(evt?.detail));
        window.addEventListener('message', (evt) => {
          const data = evt?.data;
          if (!data) return;
          if (data.type === 'openai-tool-output' || data.type === 'tool-output') {
            handlePayload(data.detail ?? data.payload ?? data.data ?? data);
          }
        });
      }

      const initial = window.openai?.toolOutput ?? window.openai?.message?.toolOutput;
      if (initial) handlePayload(initial);
    </script>
  </body>
</html>
        """;

        return ValueTask.FromResult(new ReadResourceResult
        {
            Contents = new[]
            {
                new TextResourceContents
                {
                    Uri = UiUri,
                    MimeType = "text/html+skybridge",
                    Text = html
                }
            }
        });
    }
}
