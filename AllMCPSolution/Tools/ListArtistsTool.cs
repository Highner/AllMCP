using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Repositories;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AllMCPSolution.Tools;

[McpTool("list_artists", "Lists all artists and exposes a reusable UI resource.")]
public sealed class ListArtistsTool //: IToolBase, IMcpTool, IResourceProvider
{
    private readonly IArtistRepository _artists;
    private const string UiUri = "ui://artists/list.html";

    public ListArtistsTool(IArtistRepository artists)
    {
        _artists = artists;
    }

    public string Name => "list_artists";
    public string Description => "Lists all artists from the catalog.";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        var artists = await _artists.GetAllAsync();
        var payload = artists
            .Select(a => new
            {
                id = a.Id,
                firstName = a.FirstName,
                lastName = a.LastName
            })
            .ToList();

        return new
        {
            success = true,
            count = payload.Count,
            artists = payload
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
                description = "Successful response containing the artist list.",
                content = new
                {
                    application__json = new
                    {
                        schema = new
                        {
                            type = "object",
                            properties = new
                            {
                                success = new { type = "boolean" },
                                count = new { type = "integer" },
                                artists = new
                                {
                                    type = "array",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            id = new { type = "string", format = "uuid" },
                                            firstName = new { type = "string" },
                                            lastName = new { type = "string" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    };

    public Tool GetDefinition() => new()
    {
        Name = Name,
        Title = "List artists",
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
            ["openai/toolInvocation/invoking"] = "Loading artists…",
            ["openai/toolInvocation/invoked"] = "Artists loaded"
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
        var count = node?.TryGetPropertyValue("count", out var countNode) == true
            && countNode is JsonValue countValue
            && countValue.TryGetValue<int>(out var parsedCount)
            ? parsedCount
            : (int?)null;
        var message = count.HasValue
            ? $"Found {count.Value} artists."
            : "Artist list ready.";

        return new CallToolResult
        {
            Content = new[] { new TextContentBlock { Type = "text", Text = message } },
            StructuredContent = node
        };
    }

    public IEnumerable<Resource> ListResources() => new[]
    {
        new Resource
        {
            Name = "list-artists-ui",
            Title = "Artists",
            Uri = UiUri,
            MimeType = "text/html+skybridge",
            Description = "Widget that renders the artist list from list_artists"
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
    <title>Artists</title>
    <style>
      :root {
        color-scheme: light dark;
        font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
        background: transparent;
      }
      body {
        margin: 0;
        padding: 16px;
        background: transparent;
      }
      .panel {
        border: 1px solid rgb(229 231 235 / 0.6);
        border-radius: 12px;
        padding: 16px;
        background: rgb(255 255 255 / 0.75);
        backdrop-filter: blur(6px);
      }
      h1 {
        font-size: 1.25rem;
        margin: 0 0 12px;
      }
      table {
        width: 100%;
        border-collapse: collapse;
      }
      thead th {
        text-align: left;
        font-weight: 600;
        padding-bottom: 8px;
        border-bottom: 1px solid rgb(209 213 219 / 0.6);
        color: rgb(55 65 81);
        font-size: 0.85rem;
      }
      tbody td {
        padding: 8px 0;
        border-bottom: 1px solid rgb(229 231 235 / 0.4);
        font-size: 0.9rem;
      }
      tbody tr:last-child td {
        border-bottom: none;
      }
      .empty {
        padding: 12px 0;
        color: rgb(107 114 128);
        font-style: italic;
      }
      .meta {
        font-size: 0.8rem;
        color: rgb(107 114 128);
        margin-bottom: 8px;
      }
    </style>
  </head>
  <body>
    <div class="panel">
      <h1>Artists</h1>
      <div class="meta" id="meta">Loading…</div>
      <table role="grid" aria-label="Artists">
        <thead>
          <tr>
            <th scope="col">First name</th>
            <th scope="col">Last name</th>
          </tr>
        </thead>
        <tbody id="rows"></tbody>
      </table>
      <div class="empty" id="empty" hidden>No artists in the catalog.</div>
    </div>
    <script type="module">
      const rows = document.getElementById('rows');
      const meta = document.getElementById('meta');
      const emptyState = document.getElementById('empty');

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

      const resolvePayload = (candidate) => {
        if (!candidate) return null;
        if (candidate.success === false) return candidate;
        return candidate;
      };

      const render = (payload) => {
        if (!payload) {
          meta.textContent = 'No data received.';
          rows.innerHTML = '';
          emptyState.hidden = false;
          return;
        }

        const list = Array.isArray(payload.artists) ? payload.artists : [];
        rows.innerHTML = '';

        if (list.length === 0) {
          emptyState.hidden = false;
        } else {
          emptyState.hidden = true;
          for (const artist of list) {
            const tr = document.createElement('tr');
            const first = document.createElement('td');
            first.textContent = artist?.firstName ?? '';
            const last = document.createElement('td');
            last.textContent = artist?.lastName ?? '';
            tr.append(first, last);
            rows.append(tr);
          }
        }

        const count = typeof payload.count === 'number' ? payload.count : list.length;
        meta.textContent = `${count} artist${count === 1 ? '' : 's'}`;
      };

      let lastStamp = null;

      const handlePayload = (raw) => {
        const payload = resolvePayload(raw);
        if (!payload) return;
        render(payload);
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
        window.addEventListener('openai:set_globals', (evt) => {
          if (!attached) attached = attachListeners();
          const payload = evt?.detail?.toolOutput ?? window.openai?.toolOutput ?? window.openai?.message?.toolOutput;
          if (payload) handlePayload(payload);
        });

        window.addEventListener('openai:tool-output', (evt) => handlePayload(evt?.detail));
        window.addEventListener('message', (evt) => {
          const data = evt?.data;
          if (!data) return;
          if (data.type === 'openai-tool-output' || data.type === 'tool-output') {
            handlePayload(data.detail ?? data.payload ?? data.data ?? data);
          }
        });

        document.addEventListener('DOMContentLoaded', () => {
          const payload = window.openai?.toolOutput ?? window.openai?.message?.toolOutput;
          if (payload) handlePayload(payload);
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
            Contents =
            [
                new TextResourceContents
                {
                    Uri = UiUri,
                    MimeType = "text/html+skybridge",
                    Text = html
                }
            ]
        });
    }
}
