using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AllMCPSolution.Artists;
using AllMCPSolution.Artworks;
using AllMCPSolution.Models;
using AllMCPSolution.Tools;
using AllMCPSolution.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure();
        }));

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services
    .AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddRazorPages();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IArtistRepository, ArtistRepository>();
builder.Services.AddScoped<IArtworkRepository, ArtworkRepository>();
builder.Services.AddScoped<IArtworkSaleRepository, ArtworkSaleRepository>();
builder.Services.AddScoped<IArtworkSaleQueryRepository, ArtworkSaleQueryRepository>();
builder.Services.AddScoped<IInflationIndexRepository, InflationIndexRepository>();
builder.Services.AddScoped<ICountryRepository, CountryRepository>();
builder.Services.AddScoped<IRegionRepository, RegionRepository>();
builder.Services.AddScoped<IAppellationRepository, AppellationRepository>();
builder.Services.AddScoped<ISubAppellationRepository, SubAppellationRepository>();
builder.Services.AddScoped<ISuggestedAppellationRepository, SuggestedAppellationRepository>();
builder.Services.AddScoped<IWineRepository, WineRepository>();
builder.Services.AddScoped<IWineVintageRepository, WineVintageRepository>();
builder.Services.AddScoped<IWineVintageEvolutionScoreRepository, WineVintageEvolutionScoreRepository>();
builder.Services.AddScoped<IBottleRepository, BottleRepository>();
builder.Services.AddScoped<IBottleLocationRepository, BottleLocationRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ITastingNoteRepository, TastingNoteRepository>();
builder.Services.AddScoped<ISisterhoodRepository, SisterhoodRepository>();
builder.Services.AddScoped<ISisterhoodInvitationRepository, SisterhoodInvitationRepository>();
builder.Services.AddScoped<ISipSessionRepository, SipSessionRepository>();
builder.Services.AddScoped<IWineSurferNotificationDismissalRepository, WineSurferNotificationDismissalRepository>();
builder.Services.AddScoped<InventoryIntakeService>();
builder.Services.AddScoped<IWineSurferTopBarService, WineSurferTopBarService>();
builder.Services.AddScoped<IWineImportService, WineImportService>();
builder.Services.AddScoped<IChatGptService, ChatGptService>();
builder.Services.AddScoped<IChatGptPromptService, ChatGptPromptService>();
builder.Services.AddScoped<IWineCatalogService, WineCatalogService>();

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
});

builder.Services.AddAuthorization();

// Register all tools (auto-discovered by ToolRegistry)
builder.Services.AddScoped<SearchArtistsTool>();
builder.Services.AddScoped<GetArtworkSalesHammerPriceTool>();
builder.Services.AddScoped<IHammerPerAreaAnalyticsService, HammerPerAreaAnalyticsService>();
builder.Services.AddScoped<GetArtworkSalesHammerPerAreaRolling12mTool>();
builder.Services.AddScoped<GetArtworkSalesHammerPriceRolling12mTool>();
builder.Services.AddScoped<GetArtworkSalesPriceVsEstimateRolling12mTool>();
builder.Services.AddScoped<RecordInventoryActionTool>();

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


var apiKey = builder.Configuration["OpenAI:ApiKey"];
 if (string.IsNullOrEmpty(apiKey))
 {
     builder.Logging.AddConsole();
     Console.WriteLine("⚠️ OpenAI API key not found in configuration");
 }
 else
 {
     Console.WriteLine("✅ OpenAI API key detected"); 
 }
 

// Scan and register all IMcpTool implementers so they can receive DI (scoped)
var asm = Assembly.GetExecutingAssembly();
var mcpToolTypes = asm.GetTypes()
    .Where(t => !t.IsAbstract && !t.IsInterface && typeof(AllMCPSolution.Tools.IMcpTool).IsAssignableFrom(t))
    .ToArray();
foreach (var t in mcpToolTypes)
{
    builder.Services.AddScoped(t);
}

// Register McpToolRegistry as singleton using the root provider
builder.Services.AddSingleton<McpToolRegistry>(sp => new McpToolRegistry(sp, asm));

// Register MCP server + handlers using DI-backed registry
builder.Services.AddMcpServer(options =>
{
    options.ServerInfo = new Implementation { Name = "hello-mcp-csharp", Version = "1.0.0" };
    options.Handlers = new McpServerHandlers
    {
        // ctx: RequestContext<ListToolsRequestParams>
        ListToolsHandler     = (ctx, ct) => ctx.Services.GetRequiredService<McpToolRegistry>().ListToolsAsync(ct),

        // ctx: RequestContext<CallToolRequestParams>
        CallToolHandler      = (ctx, ct) => ctx.Services.GetRequiredService<McpToolRegistry>().CallToolAsync(ctx.Params, ct),

        // ctx: RequestContext<ListResourcesRequestParams>
        ListResourcesHandler = (ctx, ct) => ctx.Services.GetRequiredService<McpToolRegistry>().ListResourcesAsync(ct),

        // ctx: RequestContext<ReadResourceRequestParams>
        ReadResourceHandler  = (ctx, ct) => ctx.Services.GetRequiredService<McpToolRegistry>().ReadResourceAsync(ctx.Params, ct)
    };
}).WithHttpTransport();

var app = builder.Build();

// Initialize ServiceLocator for tools to resolve scoped services
AllMCPSolution.Services.ServiceLocator.Provider = app.Services;

 app.UseForwardedHeaders(new ForwardedHeadersOptions {
     ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
 });
 app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseRouting();
app.UseCors("AllowAIAgents");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();
app.MapControllers();

// Get services
var mcpServer = app.Services.GetRequiredService<AllMCPSolution.Services.McpServer>();
var manifestGenerator = app.Services.GetRequiredService<ManifestGenerator>();
var toolRegistry = app.Services.GetRequiredService<ToolRegistry>();

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


app.MapMcp("/mcp");

app.Run();
