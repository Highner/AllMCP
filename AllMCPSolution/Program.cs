using AllMCPSolution;
using AllMCPSolution.Artists;
using AllMCPSolution.Artworks;
using AllMCPSolution.Charts;
using AllMCPSolution.Services;
using AllMCPSolution.Tools;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Register all tools (auto-discovered by ToolRegistry)
builder.Services.AddSingleton<HelloWorldTool>();

builder.Services.AddScoped<CreateArtistTool>();
builder.Services.AddScoped<GetAllArtistsTool>();
builder.Services.AddScoped<GetArtistByIdTool>();
builder.Services.AddScoped<UpdateArtistTool>();
builder.Services.AddScoped<DeleteArtistTool>();
builder.Services.AddScoped<SearchArtistsTool>();

//builder.Services.AddScoped<CreateArtworkTool>();
//builder.Services.AddScoped<GetAllArtworksTool>();
//builder.Services.AddScoped<GetArtworkByIdTool>();
//builder.Services.AddScoped<UpdateArtworkTool>();
//builder.Services.AddScoped<DeleteArtworkTool>();
//builder.Services.AddScoped<SearchArtworksTool>();

builder.Services.AddScoped<CreateArtworkSaleTool>();
//builder.Services.AddScoped<GetAllArtworkSalesTool>();
builder.Services.AddScoped<GetArtworkSaleByIdTool>();
//builder.Services.AddScoped<UpdateArtworkSaleTool>();
//builder.Services.AddScoped<DeleteArtworkSaleTool>();
//builder.Services.AddScoped<SearchArtworkSalesTool>();
builder.Services.AddScoped<ListArtworkSalesTool>();
builder.Services.AddScoped<GetArtworkSalesPerformanceTool>();


//builder.Services.AddScoped<BatchCreateArtworkSalesTool>();
//builder.Services.AddScoped<BatchUpdateArtworkSalesTool>();
//builder.Services.AddScoped<BatchDeleteArtworkSalesTool>();

//builder.Services.AddScoped<RenderLineChartTool>();


// Register MCP services
builder.Services.AddSingleton<ToolRegistry>();
builder.Services.AddSingleton<ManifestGenerator>();
builder.Services.AddSingleton<McpServer>();
builder.Services.AddScoped<IArtworkSaleRepository, ArtworkSaleRepository>();


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
app.UseStaticFiles();
app.UseCors("AllowAIAgents");
app.UseAuthorization();

// Get services
var mcpServer = app.Services.GetRequiredService<McpServer>();
var manifestGenerator = app.Services.GetRequiredService<ManifestGenerator>();
var toolRegistry = app.Services.GetRequiredService<ToolRegistry>();

// File upload endpoint
app.MapPost("/api/upload", async (HttpContext context, IArtworkSaleRepository repo, CancellationToken ct) =>
{
    try
    {
        var form = await context.Request.ReadFormAsync();
        var files = form.Files.GetFiles("files"); // Changed from "file" to "files"
        
        // Get the selected artist ID from the form
        var artistIdString = form["artistId"].ToString();
        
        if (string.IsNullOrEmpty(artistIdString) || !Guid.TryParse(artistIdString, out var artistId))
            return Results.BadRequest(new { message = "Valid Artist ID is required" });

        if (files == null || files.Count == 0)
            return Results.BadRequest(new { message = "No files uploaded" });

        var allSales = new List<ArtworkSale>();
        var fileResults = new List<object>();
        
        // Process each file
        foreach (var file in files)
        {
            try
            {
                using var stream = file.OpenReadStream();
                
                // Parse directly from stream
                var sales = ArtworkSaleParser.ParseFromStream(stream);
                
                // Set the artist ID for all sales
                foreach (var sale in sales)
                {
                    sale.ArtistId = artistId;
                }
                
                allSales.AddRange(sales);
                
                fileResults.Add(new
                {
                    fileName = file.FileName,
                    parsed = sales.Count,
                    success = true
                });
            }
            catch (Exception ex)
            {
                fileResults.Add(new
                {
                    fileName = file.FileName,
                    parsed = 0,
                    success = false,
                    error = ex.Message
                });
            }
        }
        
        // Insert all sales from all files
        var inserted = await repo.AddRangeIfNotExistsAsync(allSales, ct);

        return Results.Ok(new
        {
            message = "Files processed and saved",
            filesProcessed = files.Count,
            fileDetails = fileResults,
            totalParsed = allSales.Count,
            inserted,
            skipped = allSales.Count - inserted
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error uploading files: {ex.Message} {ex.StackTrace}");
    }
});

app.MapGet("/api/artists", async (ApplicationDbContext db, CancellationToken ct) =>
{
    var artists = await db.Artists
        .Select(a => new { a.Id, a.FirstName, a.LastName })
        .ToListAsync(ct);
    return Results.Ok(artists);
});

// Add new artist endpoint with validation
app.MapPost("/api/artists", async (
    [FromBody] CreateArtistRequest request,
    ApplicationDbContext db,
    CancellationToken ct) =>
{
    try
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            return Results.BadRequest(new { message = "First name and last name are required" });
        }

        // Check if artist already exists (case-insensitive)
        var existingArtist = await db.Artists
            .FirstOrDefaultAsync(a => 
                a.FirstName.ToLower() == request.FirstName.ToLower() && 
                a.LastName.ToLower() == request.LastName.ToLower(), ct);

        if (existingArtist != null)
        {
            return Results.Conflict(new 
            { 
                message = $"Artist '{request.FirstName} {request.LastName}' already exists",
                artistId = existingArtist.Id
            });
        }

        // Create new artist
        var newArtist = new Artist
        {
            Id = Guid.NewGuid(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim()
        };

        db.Artists.Add(newArtist);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new 
        { 
            message = $"Artist '{newArtist.FirstName} {newArtist.LastName}' added successfully",
            artistId = newArtist.Id,
            firstName = newArtist.FirstName,
            lastName = newArtist.LastName
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error adding artist: {ex.Message}");
    }
});

// Add endpoint to get distinct categories
app.MapGet("/api/categories", async (ApplicationDbContext db, CancellationToken ct) =>
{
    var categories = await db.ArtworkSales
        .Select(a => a.Category)
        .Distinct()
        .OrderBy(c => c)
        .ToListAsync(ct);
    return Results.Ok(categories);
});

// Add endpoint to get chart data
app.MapGet("/api/chart-data", async (
    ApplicationDbContext db,
    HttpContext context,
    CancellationToken ct) =>
{
    // Manual parameter extraction
    var query = context.Request.Query;
    
    if (!query.TryGetValue("artistId", out var artistIdStr) || 
        !Guid.TryParse(artistIdStr.ToString(), out var artistId) ||
        artistId == Guid.Empty)
    {
        return Results.BadRequest(new { message = "Valid Artist ID is required" });
    }

    var dateFrom = query.TryGetValue("dateFrom", out var dateFromStr) ? dateFromStr.ToString() : null;
    var dateTo = query.TryGetValue("dateTo", out var dateToStr) ? dateToStr.ToString() : null;
    var categories = query.TryGetValue("categories", out var categoriesStr) 
        ? categoriesStr.ToList() 
        : new List<string>();

    var salesQuery = db.ArtworkSales
        .Where(a => a.ArtistId == artistId);

    // Apply date filters
    if (!string.IsNullOrEmpty(dateFrom) && DateTime.TryParse(dateFrom, out var fromDate))
    {
        salesQuery = salesQuery.Where(a => a.SaleDate >= fromDate);
    }

    if (!string.IsNullOrEmpty(dateTo) && DateTime.TryParse(dateTo, out var toDate))
    {
        // Include the entire end date
        toDate = toDate.AddDays(1).AddSeconds(-1);
        salesQuery = salesQuery.Where(a => a.SaleDate <= toDate);
    }

    // Apply category filter
    if (categories.Any())
    {
        salesQuery = salesQuery.Where(a => categories.Contains(a.Category));
    }

    var sales = await salesQuery
        .OrderBy(a => a.SaleDate)
        .Select(a => new
        {
            a.Id,
            a.Name,
            a.Category,
            a.SaleDate,
            a.LowEstimate,
            a.HighEstimate,
            a.HammerPrice,
            a.Currency
        })
        .ToListAsync(ct);

    return Results.Ok(new { sales });
});

app.MapGet("/api/performance-data", async (
    ApplicationDbContext db,
    HttpContext context,
    CancellationToken ct) =>
{
    var query = context.Request.Query;
    
    if (!query.TryGetValue("artistId", out var artistIdStr) || 
        !Guid.TryParse(artistIdStr.ToString(), out var artistId) ||
        artistId == Guid.Empty)
    {
        return Results.BadRequest(new { message = "Valid Artist ID is required" });
    }

    var dateFrom = query.TryGetValue("dateFrom", out var dateFromStr) ? dateFromStr.ToString() : null;
    var dateTo = query.TryGetValue("dateTo", out var dateToStr) ? dateToStr.ToString() : null;
    var categories = query.TryGetValue("categories", out var categoriesStr) 
        ? categoriesStr.ToList() 
        : new List<string>();

    var salesQuery = db.ArtworkSales
        .Where(a => a.ArtistId == artistId && a.Sold == true && 
                    a.LowEstimate > 0 && a.HighEstimate > 0 && a.HammerPrice > 0);

    if (!string.IsNullOrEmpty(dateFrom) && DateTime.TryParse(dateFrom, out var fromDate))
    {
        salesQuery = salesQuery.Where(a => a.SaleDate >= fromDate);
    }

    if (!string.IsNullOrEmpty(dateTo) && DateTime.TryParse(dateTo, out var toDate))
    {
        toDate = toDate.AddDays(1).AddSeconds(-1);
        salesQuery = salesQuery.Where(a => a.SaleDate <= toDate);
    }

    if (categories.Any())
    {
        salesQuery = salesQuery.Where(a => categories.Contains(a.Category));
    }

    var sales = await salesQuery
        .OrderBy(a => a.SaleDate)
        .Take(1000)
        .Select(a => new
        {
            a.SaleDate,
            a.LowEstimate,
            a.HighEstimate,
            a.HammerPrice
        })
        .ToListAsync(ct);

    var timeSeries = sales.Select(sale => new
    {
        Time = sale.SaleDate,
        PerformanceFactor = CalculatePerformanceFactor(
            sale.HammerPrice,
            sale.LowEstimate,
            sale.HighEstimate
        )
    }).ToList();

    return Results.Ok(new { timeSeries });
});

static double CalculatePerformanceFactor(decimal hammerPrice, decimal lowEstimate, decimal highEstimate)
{
    if (hammerPrice < lowEstimate)
    {
        return (double)((hammerPrice - lowEstimate) / lowEstimate);
    }

    if (hammerPrice > highEstimate)
    {
        return (double)(hammerPrice / highEstimate);
    }

    var range = highEstimate - lowEstimate;
    if (range == 0)
        return 0.5;

    return (double)((hammerPrice - lowEstimate) / range);
}


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
foreach (var toolType in toolRegistry.GetAllToolTypes())
{
    var tempTool = Activator.CreateInstance(toolType, new object[] { null! }) as IToolBase;
    if (tempTool == null) continue;
    
    var toolName = tempTool.Name;
    var toolDescription = tempTool.Description;
    
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

            var result = await t.ExecuteAsync(parameters);
            return Results.Ok(new { result });
        })
        .WithName(toolName)
        .WithDescription(toolDescription);
}




app.Run();

// Request models
record CreateArtistRequest(string FirstName, string LastName);