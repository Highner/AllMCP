using AllMCPSolution.Services;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Register all tools (auto-discovered by ToolRegistry)
builder.Services.AddSingleton<HelloWorldTool>();

// Register MCP services
builder.Services.AddSingleton<ToolRegistry>();
builder.Services.AddSingleton<ManifestGenerator>();
builder.Services.AddSingleton<McpServer>();

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

var app = builder.Build();

app.UseHttpsRedirection();
app.UseCors("AllowAIAgents");
app.UseAuthorization();

// Get services
var mcpServer = app.Services.GetRequiredService<McpServer>();
var manifestGenerator = app.Services.GetRequiredService<ManifestGenerator>();
var toolRegistry = app.Services.GetRequiredService<ToolRegistry>();

// MCP endpoint
app.MapPost("/mcp", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<Dictionary<string, object>>();
    var response = await mcpServer.HandleRequestAsync(request);
    return Results.Json(response);
});

app.MapGet("/", () => "MCP Server is running!");

// MCP Manifest endpoint
app.MapGet("/.well-known/mcp-manifest", () =>
{
    return Results.Json(manifestGenerator.GenerateMcpManifest());
});

// OpenAPI manifest for ChatGPT Custom GPT
app.MapGet("/openapi.json", () =>
{
    return Results.Json(manifestGenerator.GenerateOpenApiManifest());
});

// Tools discovery endpoint
app.MapGet("/tools", () =>
{
    var tools = toolRegistry.GetAllTools()
        .Select(t => t.GetToolDefinition())
        .ToArray();
    
    return Results.Json(new { tools });
});

// Dynamic tool endpoints for direct access
foreach (var tool in toolRegistry.GetAllTools())
{
    var toolName = tool.Name;
    app.MapPost($"/tools/{toolName}", async (HttpContext context) =>
    {
        var t = toolRegistry.GetTool(toolName);
        if (t == null)
        {
            return Results.NotFound();
        }

        Dictionary<string, object>? parameters = null;
        if (context.Request.ContentLength > 0)
        {
            parameters = await context.Request.ReadFromJsonAsync<Dictionary<string, object>>();
        }

        var result = await t.ExecuteAsync(parameters);
        return Results.Ok(new { result });
    })
    .WithName(toolName)
    .WithDescription(tool.Description);
}

app.Run();