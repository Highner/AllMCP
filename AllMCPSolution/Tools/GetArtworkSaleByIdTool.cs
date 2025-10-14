using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Artworks;

[McpTool("get_artwork_sale_by_id", "Retrieves a specific artwork sale by its ID")]
public class GetArtworkSaleByIdTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public GetArtworkSaleByIdTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "get_artwork_sale_by_id";
    public string Description => "Retrieves a specific artwork sale by its ID";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        if (parameters == null || !parameters.ContainsKey("id"))
        {
            throw new ArgumentException("Parameter 'id' is required");
        }

        var idString = parameters["id"]?.ToString();
        if (!Guid.TryParse(idString, out var id))
        {
            throw new ArgumentException("Invalid ID format. Must be a valid GUID");
        }

        var artworkSale = await _dbContext.ArtworkSales
            .Include(a => a.Artist)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (artworkSale == null)
        {
            return new
            {
                success = false,
                message = "Artwork sale not found"
            };
        }

        return new
        {
            success = true,
            artworkSale = new
            {
                id = artworkSale.Id,
                name = artworkSale.Name,
                height = artworkSale.Height,
                width = artworkSale.Width,
                yearCreated = artworkSale.YearCreated,
                saleDate = artworkSale.SaleDate,
                technique = artworkSale.Technique,
                category = artworkSale.Category,
                currency = artworkSale.Currency,
                lowEstimate = artworkSale.LowEstimate,
                highEstimate = artworkSale.HighEstimate,
                hammerPrice = artworkSale.HammerPrice,
                sold = artworkSale.Sold,
                artistId = artworkSale.ArtistId,
                artist = artworkSale.Artist != null ? new
                {
                    id = artworkSale.Artist.Id,
                    firstName = artworkSale.Artist.FirstName,
                    lastName = artworkSale.Artist.LastName
                } : null
            }
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
                    id = new
                    {
                        type = "string",
                        description = "The GUID of the artwork sale"
                    }
                },
                required = new[] { "id" }
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
                    ["name"] = "id",
                    ["in"] = "query",
                    ["required"] = true,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["format"] = "uuid"
                    }
                }
            },
            ["responses"] = new Dictionary<string, object>
            {
                ["200"] = new Dictionary<string, object>
                {
                    ["description"] = "Artwork sale details",
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
                                    ["artworkSale"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "object"
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
