using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Artworks;

[McpTool("update_artwork_sale", "Updates an existing artwork sale in the database")]
public class UpdateArtworkSaleTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public UpdateArtworkSaleTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "update_artwork_sale";
    public string Description => "Updates an existing artwork sale in the database";
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

        var artworkSale = await _dbContext.ArtworkSales.FirstOrDefaultAsync(a => a.Id == id);

        if (artworkSale == null)
        {
            return new
            {
                success = false,
                message = "Artwork sale not found"
            };
        }

        // Update name if provided
        if (parameters.ContainsKey("name"))
        {
            var name = parameters["name"]?.ToString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                artworkSale.Name = name;
            }
        }

        // Update height if provided
        if (parameters.ContainsKey("height"))
        {
            artworkSale.Height = Convert.ToInt32(parameters["height"]);
        }

        // Update width if provided
        if (parameters.ContainsKey("width"))
        {
            artworkSale.Width = Convert.ToInt32(parameters["width"]);
        }

        // Update yearCreated if provided
        if (parameters.ContainsKey("yearCreated"))
        {
            artworkSale.YearCreated = Convert.ToInt32(parameters["yearCreated"]);
        }

        // Update saleDate if provided
        if (parameters.ContainsKey("saleDate"))
        {
            artworkSale.SaleDate = Convert.ToDateTime(parameters["saleDate"]);
        }

        // Update technique if provided
        if (parameters.ContainsKey("technique"))
        {
            artworkSale.Technique = parameters["technique"]?.ToString();
        }

        // Update category if provided
        if (parameters.ContainsKey("category"))
        {
            artworkSale.Category = parameters["category"]?.ToString();
        }

        // Update currency if provided
        if (parameters.ContainsKey("currency"))
        {
            artworkSale.Currency = parameters["currency"]?.ToString();
        }

        // Update lowEstimate if provided
        if (parameters.ContainsKey("lowEstimate"))
        {
            artworkSale.LowEstimate = Convert.ToDecimal(parameters["lowEstimate"]);
        }

        // Update highEstimate if provided
        if (parameters.ContainsKey("highEstimate"))
        {
            artworkSale.HighEstimate = Convert.ToDecimal(parameters["highEstimate"]);
        }

        // Update hammerPrice if provided
        if (parameters.ContainsKey("hammerPrice"))
        {
            artworkSale.HammerPrice = Convert.ToDecimal(parameters["hammerPrice"]);
        }

        // Update sold if provided
        if (parameters.ContainsKey("sold"))
        {
            artworkSale.Sold = Convert.ToBoolean(parameters["sold"]);
        }

        // Update artistId if provided
        if (parameters.ContainsKey("artistId"))
        {
            var artistIdString = parameters["artistId"]?.ToString();
            if (Guid.TryParse(artistIdString, out var artistId))
            {
                artworkSale.ArtistId = artistId;
            }
        }

        await _dbContext.SaveChangesAsync();

        return new
        {
            success = true,
            message = "Artwork sale updated successfully",
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
                artistId = artworkSale.ArtistId
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
                        description = "The GUID of the artwork sale to update"
                    },
                    name = new
                    {
                        type = "string",
                        description = "The new name of the artwork (optional)"
                    },
                    height = new
                    {
                        type = "integer",
                        description = "The new height of the artwork in centimeters (optional)"
                    },
                    width = new
                    {
                        type = "integer",
                        description = "The new width of the artwork in centimeters (optional)"
                    },
                    yearCreated = new
                    {
                        type = "integer",
                        description = "The new year the artwork was created (optional)"
                    },
                    saleDate = new
                    {
                        type = "string",
                        description = "The new sale date in ISO 8601 format (optional)"
                    },
                    technique = new
                    {
                        type = "string",
                        description = "The new technique (optional)"
                    },
                    category = new
                    {
                        type = "string",
                        description = "The new category (optional)"
                    },
                    currency = new
                    {
                        type = "string",
                        description = "The new currency code (optional)"
                    },
                    lowEstimate = new
                    {
                        type = "number",
                        description = "The new low estimate (optional)"
                    },
                    highEstimate = new
                    {
                        type = "number",
                        description = "The new high estimate (optional)"
                    },
                    hammerPrice = new
                    {
                        type = "number",
                        description = "The new hammer price (optional)"
                    },
                    sold = new
                    {
                        type = "boolean",
                        description = "Whether the artwork was sold (optional)"
                    },
                    artistId = new
                    {
                        type = "string",
                        description = "The new GUID of the artist (optional)"
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
            ["requestBody"] = new Dictionary<string, object>
            {
                ["required"] = true,
                ["content"] = new Dictionary<string, object>
                {
                    ["application/json"] = new Dictionary<string, object>
                    {
                        ["schema"] = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["properties"] = new Dictionary<string, object>
                            {
                                ["id"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string",
                                    ["format"] = "uuid"
                                },
                                ["name"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string"
                                },
                                ["height"] = new Dictionary<string, object>
                                {
                                    ["type"] = "integer"
                                },
                                ["width"] = new Dictionary<string, object>
                                {
                                    ["type"] = "integer"
                                },
                                ["yearCreated"] = new Dictionary<string, object>
                                {
                                    ["type"] = "integer"
                                },
                                ["saleDate"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string",
                                    ["format"] = "date-time"
                                },
                                ["technique"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string"
                                },
                                ["category"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string"
                                },
                                ["currency"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string"
                                },
                                ["lowEstimate"] = new Dictionary<string, object>
                                {
                                    ["type"] = "number"
                                },
                                ["highEstimate"] = new Dictionary<string, object>
                                {
                                    ["type"] = "number"
                                },
                                ["hammerPrice"] = new Dictionary<string, object>
                                {
                                    ["type"] = "number"
                                },
                                ["sold"] = new Dictionary<string, object>
                                {
                                    ["type"] = "boolean"
                                },
                                ["artistId"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string",
                                    ["format"] = "uuid"
                                }
                            },
                            ["required"] = new[] { "id" }
                        }
                    }
                }
            },
            ["responses"] = new Dictionary<string, object>
            {
                ["200"] = new Dictionary<string, object>
                {
                    ["description"] = "Artwork sale updated successfully",
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
                                    ["message"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "string"
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
