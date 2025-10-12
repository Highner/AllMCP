using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Artworks;

[McpTool("get_all_artwork_sales", "Retrieves all artwork sales from the database")]
public class GetAllArtworkSalesTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public GetAllArtworkSalesTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "get_all_artwork_sales";
    public string Description => "Retrieves all artwork sales from the database";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        var artworkSales = await _dbContext.ArtworkSales
            .Include(a => a.Artist)
            .ToListAsync();

        return new
        {
            success = true,
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
                properties = new { },
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
            ["responses"] = new Dictionary<string, object>
            {
                ["200"] = new Dictionary<string, object>
                {
                    ["description"] = "List of all artwork sales",
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
