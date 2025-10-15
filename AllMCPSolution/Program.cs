using AllMCPSolution.Artists;
using AllMCPSolution.Artworks;
using AllMCPSolution.Data;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;
using AllMCPSolution.Tools;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

builder.Services.AddScoped<GetAllArtistsTool>();
builder.Services.AddScoped<GetArtistByIdTool>();
builder.Services.AddScoped<SearchArtistsTool>();


//builder.Services.AddScoped<GetArtworkSalesPerformanceTool>();
builder.Services.AddScoped<GetArtworkSalesHammerPriceTool>();
builder.Services.AddScoped<GetArtworkSalesPriceVsEstimateTool>();
builder.Services.AddScoped<GetArtworkSalesHammerPerAreaTool>();
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




app.Run();
