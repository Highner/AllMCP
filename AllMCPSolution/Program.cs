using System.Text.Json;
using System.Text.Json.Nodes;
using AllMCPSolution.Artists;
using AllMCPSolution.Artworks;
using AllMCPSolution.Data;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;
using AllMCPSolution.Tools;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IArtistRepository, ArtistRepository>();
builder.Services.AddScoped<IArtworkRepository, ArtworkRepository>();
builder.Services.AddScoped<IArtworkSaleRepository, ArtworkSaleRepository>();
builder.Services.AddScoped<IInflationIndexRepository, InflationIndexRepository>();

// Register all tools (auto-discovered by ToolRegistry)

//builder.Services.AddScoped<GetAllArtistsTool>();
//builder.Services.AddScoped<GetArtistByIdTool>();
builder.Services.AddScoped<SearchArtistsTool>();


//builder.Services.AddScoped<GetArtworkSalesPerformanceTool>();
builder.Services.AddScoped<GetArtworkSalesHammerPriceTool>();
//builder.Services.AddScoped<GetArtworkSalesPriceVsEstimateTool>();
//builder.Services.AddScoped<GetArtworkSalesHammerPerAreaTool>();
builder.Services.AddScoped<IHammerPerAreaAnalyticsService, HammerPerAreaAnalyticsService>();

builder.Services.AddScoped<GetArtworkSalesHammerPerAreaRolling12mTool>();
builder.Services.AddScoped<GetArtworkSalesHammerPriceRolling12mTool>();
builder.Services.AddScoped<GetArtworkSalesPriceVsEstimateRolling12mTool>();

//builder.Services.AddScoped<RenderLineChartTool>();


// Inflation and related services
builder.Services.AddScoped<IInflationService, EcbInflationService>();

// Register MCP services
builder.Services.AddSingleton<ToolRegistry>();
builder.Services.AddSingleton<ManifestGenerator>();
builder.Services.AddSingleton<AllMCPSolution.Services.McpServer>();



// Add CORS for AI agent access
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAIAgents", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});


// Register the MCP server and handlers
builder.Services.AddMcpServer(options =>
{
    // 1. Describe the server
    options.ServerInfo = new Implementation
    {
        Name = "hello-mcp-csharp",
        Version = "1.0.0"
    };

    // 2. Handlers
    options.Handlers = new McpServerHandlers
    {
        ListToolsHandler = (req, ct) => ValueTask.FromResult(new ListToolsResult
        {
            Tools =
            [
                new Tool
                {
                    Name = "hello_world",
                    Title = "Hello World",
                    Description = "Greets the user and renders a card UI.",
                    InputSchema = JsonSerializer.Deserialize<JsonElement>(
                        """
                        {
                          "type": "object",
                          "properties": {
                            "name": { "type": "string", "description": "Name to greet" }
                          }
                        }
                        """
                    ),
                    Meta = new JsonObject
                    {
                        ["openai/outputTemplate"] = "ui://widget/hello.html",
                        ["openai/toolInvocation/invoking"] = "Saying hello…",
                        ["openai/toolInvocation/invoked"] = "Hello sent!"
                    },

                }
            ]
        }),

        CallToolHandler = (req, ct) =>
        {
            if (req.Params?.Name != "hello_world")
                throw new McpException($"Unknown tool: '{req.Params?.Name}'", McpErrorCode.InvalidRequest);

            var name = "World";
            if (req.Params?.Arguments is { } args &&
                args.TryGetValue("name", out var el) &&
                el.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(el.GetString()))
            {
                name = el.GetString()!;
            }

            var structured = new JsonObject
            {
                ["message"] = $"Hello, {name}!"
            };

            return ValueTask.FromResult(new CallToolResult
            {
                Content = [ new TextContentBlock { Type = "text", Text = $"Hello, {name}!" } ],
                StructuredContent = structured
            });

        },

        ListResourcesHandler = (req, ct) => ValueTask.FromResult(new ListResourcesResult
        {
            Resources =
            [
                new Resource
                {
                    Name = "hello-ui",
                    Title = "Hello UI",
                    Uri = "ui://widget/hello.html",
                    MimeType = "text/html+skybridge",
                    Description = "Card UI for hello_world"
                }
            ]
        }),

        ReadResourceHandler = (req, ct) =>
        {
            if (req.Params?.Uri != "ui://widget/hello.html")
                throw new McpException("Missing required argument 'name'", McpErrorCode.InvalidParams);

            const string html = """
            <!doctype html>
            <html lang="en">
              <head>
                <meta charset="UTF-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>Hello World ✨</title>
                <style>
                  :root {
                    --bg: linear-gradient(135deg, #89f7fe 0%, #66a6ff 100%);
                    --card-bg: rgba(255, 255, 255, 0.9);
                    --text-color: #333;
                    --accent: #4e8cff;
                  }
            
                  body {
                    font-family: system-ui, sans-serif;
                    background: var(--bg);
                    height: 100vh;
                    margin: 0;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                  }
            
                  .card {
                    background: var(--card-bg);
                    border-radius: 16px;
                    padding: 2rem 3rem;
                    box-shadow: 0 10px 30px rgba(0, 0, 0, 0.1);
                    text-align: center;
                    animation: float 3s ease-in-out infinite;
                    max-width: 400px;
                    transition: transform 0.3s ease, box-shadow 0.3s ease;
                  }
            
                  .card:hover {
                    transform: translateY(-5px);
                    box-shadow: 0 15px 40px rgba(0, 0, 0, 0.15);
                  }
            
                  h2 {
                    color: var(--accent);
                    margin-bottom: 0.5rem;
                  }
            
                  #msg {
                    color: var(--text-color);
                    font-size: 1.2rem;
                  }
            
                  @keyframes float {
                    0%, 100% { transform: translateY(0); }
                    50% { transform: translateY(-6px); }
                  }
                </style>
              </head>
              <body>
                <div class="card">
                  <h2>Hello World 👋</h2>
                  <p id="msg">Loading message...</p>
                </div>
            
                <script type="module">
                  const data = (window.openai && window.openai.toolOutput) || {};
                  document.getElementById("msg").textContent = data.message || "Hello!";
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
                        Uri = "ui://widget/hello.html",
                        MimeType = "text/html+skybridge",
                        Text = html
                    }
                ]
            });
        }
    };
}).WithHttpTransport();


var app = builder.Build();

// Ensure database is migrated to latest on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors("AllowAIAgents");
app.UseAuthorization();
app.MapControllers();

// Map MVC routes for server-rendered views
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Get services
var mcpServer = app.Services.GetRequiredService<AllMCPSolution.Services.McpServer>();
var manifestGenerator = app.Services.GetRequiredService<ManifestGenerator>();
var toolRegistry = app.Services.GetRequiredService<ToolRegistry>();







// MCP endpoint
//app.MapPost("/mcp", async (HttpContext context) =>
//{
 //   var request = await context.Request.ReadFromJsonAsync<Dictionary<string, object>>();
 //   var response = await mcpServer.HandleRequestAsync(request);
 //   return Results.Json(response);
//});

// Explicit OPTIONS handler for CORS preflight on /mcp (some environments require it)
//app.MapMethods("/mcp", new[] { "OPTIONS" }, () => Results.Ok());



app.MapGet("/", async (HttpContext context) =>
{
    var filePath = Path.Combine(app.Environment.WebRootPath, "index.html");
    if (File.Exists(filePath))
    {
        var html = await File.ReadAllTextAsync(filePath);
        context.Response.ContentType = "text/html";
        await context.Response.WriteAsync(html);
    }
    else
    {
        await context.Response.WriteAsync("MCP Server is running!");
    }
});


// MCP Manifest endpoint
app.MapGet("/.well-known/mcp-manifest", () =>
{
    return Results.Json(manifestGenerator.GenerateMcpManifest());
});

// OpenAPI manifest for ChatGPT Custom GPT
app.MapGet("/openapi.json", (IServiceProvider serviceProvider) =>
{
    using var scope = serviceProvider.CreateScope();
    var manifestGenerator = scope.ServiceProvider.GetRequiredService<ManifestGenerator>();
    return Results.Json(manifestGenerator.GenerateOpenApiManifest(scope.ServiceProvider));
});

app.MapGet("/.well-known/anthropic-manifest", (ManifestGenerator manifestGenerator, IServiceProvider serviceProvider) =>
{
    using var scope = serviceProvider.CreateScope();
    return Results.Ok(manifestGenerator.GenerateAnthropicManifest(scope.ServiceProvider));
});

// OpenAI Agent Builder MCP discovery endpoint
//app.MapGet("/.well-known/mcp", (IServiceProvider serviceProvider) =>
//{
//    using var scope = serviceProvider.CreateScope();
 //   var manifestGenerator = scope.ServiceProvider.GetRequiredService<ManifestGenerator>();
 //   return Results.Json(manifestGenerator.GenerateOpenAIMcpDiscovery(scope.ServiceProvider));
//});

// Tools discovery endpoint
app.MapGet("/tools", (IServiceProvider serviceProvider) =>
{
    var toolRegistry = serviceProvider.GetRequiredService<ToolRegistry>();
    var tools = toolRegistry.GetAllTools(serviceProvider)
        .Select(t => t.GetToolDefinition())
        .ToArray();
    
    return Results.Json(new { tools });
});


// Dynamic tool endpoints for direct access
using (var scope = app.Services.CreateScope())
{
    var toolsForMetadata = toolRegistry.GetAllTools(scope.ServiceProvider);
    
    foreach (var tool in toolsForMetadata)
    {
        var toolName = tool.Name;
        var toolDescription = tool.Description;
        
        app.MapPost($"/tools/{toolName}", async (HttpContext context, IServiceProvider serviceProvider) =>
        {
            var toolRegistry = serviceProvider.GetRequiredService<ToolRegistry>();
            var t = toolRegistry.GetTool(toolName, serviceProvider);
            if (t == null)
            {
                return Results.NotFound();
            }

            Dictionary<string, object>? parameters = new();
            
            // Read from request body if present
            if (context.Request.ContentLength > 0)
            {
                var bodyParams = await context.Request.ReadFromJsonAsync<Dictionary<string, object>>();
                if (bodyParams != null)
                {
                    foreach (var kvp in bodyParams)
                    {
                        parameters[kvp.Key] = kvp.Value;
                    }
                }
            }
            
            // Also read from query parameters
            foreach (var queryParam in context.Request.Query)
            {
                parameters[queryParam.Key] = queryParam.Value.ToString() ?? "";
            }

            try
            {
                var result = await t.ExecuteAsync(parameters);
                return Results.Ok(new { result });
            }
            catch (Exception e)
            {
                return Results.Problem(e.Message + e.StackTrace);
            }

        })
        .WithName(toolName)
        .WithDescription(toolDescription);
    }
}


app.MapMcp("/mcp");

app.Run();
