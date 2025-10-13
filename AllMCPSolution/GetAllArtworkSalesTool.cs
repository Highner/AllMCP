using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Artworks;

[McpTool("get_all_artwork_sales", "Retrieves all artwork sales from the database with pagination")]
public class GetAllArtworkSalesTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public GetAllArtworkSalesTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "get_all_artwork_sales";
    public string Description => "Retrieves all artwork sales from the database with pagination. Use page and pageSize parameters to control results.";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        // Default pagination values
        int page = 1;
        int pageSize = 50;

        if (parameters != null)
        {
            if (parameters.ContainsKey("page") && int.TryParse(parameters["page"]?.ToString(), out int p))
                page = Math.Max(1, p);
            
            if (parameters.ContainsKey("pageSize") && int.TryParse(parameters["pageSize"]?.ToString(), out int ps))
                pageSize = Math.Clamp(ps, 1, 100);
        }

        var totalCount = await _dbContext.ArtworkSales.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var artworkSales = await _dbContext.ArtworkSales
            .Include(a => a.Artist)
            .OrderBy(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new
        {
            success = true,
            pagination = new
            {
                currentPage = page,
                pageSize = pageSize,
                totalItems = totalCount,
                totalPages = totalPages,
                hasNextPage = page < totalPages,
                hasPreviousPage = page > 1
            },
            count = artworkSales.Count,
            artworkSales = artworkSales.Select(a => new
            {
                id = a.Id,
                name = a.Name,
                height = a.Height,
                width = a.Width,
                yearCreated = a.YearCreated,
                saleDate = a.SaleDate,
                technique = a.Technique,
                category = a.Category,
                currency = a.Currency,
                lowEstimate = a.LowEstimate,
                highEstimate = a.HighEstimate,
                hammerPrice = a.HammerPrice,
                sold = a.Sold,
                artistId = a.ArtistId,
                artist = a.Artist != null ? new
                {
                    id = a.Artist.Id,
                    firstName = a.Artist.FirstName,
                    lastName = a.Artist.LastName
                } : null
            }).ToList()
        };
    }

    public object GetToolDefinition()
    {
        return new
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
                properties = new
                {
                    page = new
                    {
                        type = "integer",
                        description = "Page number to retrieve (default: 1)",
                        minimum = 1
                    },
                    pageSize = new
                    {
                        type = "integer",
                        description = "Number of items per page (default: 50, max: 100)",
                        minimum = 1,
                        maximum = 100
                    }
                },
                required = new string[] { }
            }
        };
    }

    public object GetOpenApiSchema()
    {
        return new Dictionary<string, object>
        {
            ["operationId"] = Name,
            ["summary"] = Description,
            ["parameters"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["name"] = "page",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "integer",
                        ["default"] = 1,
                        ["minimum"] = 1
                    },
                    ["description"] = "Page number to retrieve"
                },
                new Dictionary<string, object>
                {
                    ["name"] = "pageSize",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "integer",
                        ["default"] = 50,
                        ["minimum"] = 1,
                        ["maximum"] = 100
                    },
                    ["description"] = "Number of items per page"
                }
            },
            ["responses"] = new Dictionary<string, object>
            {
                ["200"] = new Dictionary<string, object>
                {
                    ["description"] = "Paginated list of artwork sales",
                    ["content"] = new Dictionary<string, object>
                    {
                        ["application/json"] = new Dictionary<string, object>
                        {
                            ["schema"] = new Dictionary<string, object>
                            {
                                ["type"] = "object",
                                ["properties"] = new Dictionary<string, object>
                                {
                                    ["success"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "boolean"
                                    },
                                    ["pagination"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "object"
                                    },
                                    ["count"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "integer"
                                    },
                                    ["artworkSales"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "array"
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}