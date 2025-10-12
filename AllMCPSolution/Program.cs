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

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    //app.UseSwagger();
    //app.UseSwaggerUI();
}

app.UseHttpsRedirection();
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

app.Run();