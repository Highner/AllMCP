// File: Tools/HelloWorldTool.cs
using System.Text.Json.Nodes;
using AllMCPSolution.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;


namespace AllMCPSolution.Tools;

public sealed class HelloWorldTool : IMcpTool, IResourceProvider
{
    public static HelloWorldTool Instance { get; } = new();
    private const string UiUri = "ui://widget/hello.html";

    public Tool GetDefinition() => new()
    {
        Name = "hello_world",
        Title = "Hello World",
        Description = "Greets the user and renders a card UI.",
        InputSchema = JsonDocument.Parse("""
                                         {
                                           "type": "object",
                                           "properties": {
                                             "name": { "type": "string", "description": "Name to greet" }
                                           }
                                         }
                                         """).RootElement,

        Meta = new JsonObject
        {
            ["openai/outputTemplate"] = UiUri,
            ["openai/toolInvocation/invoking"] = "Saying hello…",
            ["openai/toolInvocation/invoked"] = "Hello sent!"
        }
    };

    public ValueTask<CallToolResult> RunAsync(CallToolRequestParams request, CancellationToken ct)
    {
        
        var name = GetStringArg(request, "name") is { Length: > 0 } s ? s : "World";


        var structured = new JsonObject { ["message"] = $"Hello, {name}!" };

        return ValueTask.FromResult(new CallToolResult
        {
            Content = [ new TextContentBlock { Type = "text", Text = $"Hello, {name}!" } ],
            StructuredContent = structured
        });
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
