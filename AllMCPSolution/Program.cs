using AllMCPSolution.Services;
using AllMCPSolution.Tools;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

// Register MCP services
builder.Services.AddSingleton<McpServer>();
builder.Services.AddSingleton<HelloWorldTool>();

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

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    //app.UseSwagger();
    //app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAIAgents");
app.UseAuthorization();

// MCP endpoint
var mcpServer = app.Services.GetRequiredService<McpServer>();

app.MapPost("/mcp", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<Dictionary<string, object>>();
    var response = await mcpServer.HandleRequestAsync(request);
    return Results.Json(response);
});

app.MapGet("/", () => "MCP Server is running!");

// Discovery endpoint - returns server manifest and capabilities
app.MapGet("/.well-known/mcp-manifest", () =>
{
    var baseUrl = "https://allmcp-azfthzccbub7a3e5.northeurope-01.azurewebsites.net";
    
    return Results.Json(new
    {
        schemaVersion = "1.0",
        name = "AllMCPSolution",
        version = "1.0.0",
        description = "A simple MCP server with a Hello World test tool",
        protocol = "mcp",
        protocolVersion = "2024-11-05",
        endpoints = new
        {
            mcp = new
            {
                url = $"{baseUrl}/mcp",
                transport = "http",
                methods = new[] { "POST" }
            }
        },
        capabilities = new
        {
            tools = true,
            prompts = false,
            resources = false
        },
        metadata = new
        {
            author = "Your Name",
            homepage = baseUrl,
            documentation = $"{baseUrl}/docs"
        }
    });
});

// Tools discovery endpoint - lists all available tools
app.MapGet("/tools", async () =>
{
    var helloWorldTool = app.Services.GetRequiredService<HelloWorldTool>();
    
    return Results.Json(new
    {
        tools = new[]
        {
            helloWorldTool.GetToolDefinition()
        }
    });
});

app.Run();