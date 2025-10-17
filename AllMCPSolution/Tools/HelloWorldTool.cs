// File: Tools/HelloWorldTool.cs
using System.Text.Json.Nodes;
using AllMCPSolution.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;


using AllMCPSolution.Attributes;

namespace AllMCPSolution.Tools;

[McpTool("hello_world", "Greets the user and renders a simple UI card.")]
public sealed class HelloWorldTool : IMcpTool, IResourceProvider, IToolBase
{
    public string Name => "hello_world";
    public string Description => "Greets the user and renders a card UI.";
    public string? SafetyLevel => "non_critical";
    public static HelloWorldTool Instance { get; } = new();
    private const string UiUri = "ui://widget/hello.html";

    public Tool GetDefinition() => new()
    {
        Name = Name,
        Title = "Hello World",
        Description = Description,
        InputSchema = JsonDocument.Parse("""
                                         {
                                           "type": "object",
                                           "properties": {
                                             "name": { "type": "string", "description": "Name to greet" }
                                           },
                                           "required": []
                                         }
                                         """).RootElement,
        Meta = new JsonObject
        {
            ["openai/outputTemplate"] = UiUri,
            ["openai/toolInvocation/invoking"] = "Saying hello…",
            ["openai/toolInvocation/invoked"] = "Hello sent!"
        }
    };

    public object GetToolDefinition() => new
    {
        name = Name,
        description = Description,
        inputSchema = new
        {
            type = "object",
            properties = new
            {
                name = new { type = "string", description = "Name to greet" }
            },
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
                        type = "object",
                        properties = new
                        {
                            name = new { type = "string", description = "Name to greet" }
                        }
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

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        parameters ??= new Dictionary<string, object>();
        parameters.TryGetValue("name", out var raw);
        var name = raw as string;
        if (string.IsNullOrWhiteSpace(name)) name = "World";
        return new { message = $"Hello, {name}!" };
    }

    public async ValueTask<CallToolResult> RunAsync(CallToolRequestParams request, CancellationToken ct)
    {
        Dictionary<string, object?>? dict = null;
        if (request?.Arguments is not null)
        {
            dict = new Dictionary<string, object?>();
            foreach (var kv in request.Arguments)
            {
                dict[kv.Key] = kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() : null;
            }
        }

        var result = await ExecuteAsync(dict);
        var msg = (result?.GetType().GetProperty("message")?.GetValue(result) as string) ?? "Hello, World!";
        return new CallToolResult
        {
            Content = [ new TextContentBlock { Type = "text", Text = msg } ],
            StructuredContent = JsonSerializer.SerializeToNode(result) as JsonObject
        };
    }

    public IEnumerable<Resource> ListResources() =>
        new[]
        {
            new Resource
            {
                Name = "hello-ui",
                Title = "Hello UI",
                Uri = UiUri,
                MimeType = "text/html+skybridge",
                Description = "Card UI for hello_world"
            }
        };

    public ValueTask<ReadResourceResult> ReadResourceAsync(ReadResourceRequestParams request, CancellationToken ct)
    {
        if (request.Uri != UiUri)
            throw new McpException("Resource not found", McpErrorCode.InvalidParams);

        const string html = """
        <!doctype html><html><body style="font-family:system-ui;padding:16px">
          <div style="border:1px solid #e5e7eb;border-radius:12px;padding:16px">
            <h2>Hello World</h2><p id="msg">Hello!</p>
          </div>
          <script type="module">
            const data = (window.openai && window.openai.toolOutput) || {};
            document.getElementById("msg").textContent = data.message || "Hello!";
          </script>
        </body></html>
        """;

        return ValueTask.FromResult(new ReadResourceResult
        {
            Contents = [
                new TextResourceContents {
                    Uri = UiUri,
                    MimeType = "text/html+skybridge",
                    Text = html
                }
            ]
        });
    }
    
    private static string? GetStringArg(CallToolRequestParams req, string key)
    {
        if (req.Arguments is null) return null;
        if (!req.Arguments.TryGetValue(key, out var el)) return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

}
