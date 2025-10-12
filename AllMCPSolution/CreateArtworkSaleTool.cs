using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;

namespace AllMCPSolution.Artworks;

[McpTool("create_artwork_sale", "Creates a new artwork sale in the database")]
public class CreateArtworkSaleTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public CreateArtworkSaleTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "create_artwork_sale";
    public string Description => "Creates a new artwork sale in the database";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        if (parameters == null || !parameters.ContainsKey("name") || !parameters.ContainsKey("artistId"))
        {
            throw new ArgumentException("Parameters 'name' and 'artistId' are required");
        }

        var name = parameters["name"]?.ToString();
        var artistIdString = parameters["artistId"]?.ToString();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be empty");
        }

        if (!Guid.TryParse(artistIdString, out var artistId))
        {
            throw new ArgumentException("Invalid artistId format. Must be a valid GUID");
        }

        // Optional parameters with defaults
        var height = parameters.ContainsKey("height") ? Convert.ToDecimal(parameters["height"]) : 0;
        var width = parameters.ContainsKey("width") ? Convert.ToDecimal(parameters["width"]) : 0;
        var yearCreated = parameters.ContainsKey("yearCreated") ? Convert.ToInt32(parameters["yearCreated"]) : 0;
        var saleDate = parameters.ContainsKey("saleDate") ? Convert.ToDateTime(parameters["saleDate"]) : DateTime.Now;
        var technique = parameters.ContainsKey("technique") ? parameters["technique"]?.ToString() : "";
        var category = parameters.ContainsKey("category") ? parameters["category"]?.ToString() : "";
        var currency = parameters.ContainsKey("currency") ? parameters["currency"]?.ToString() : "USD";
        var lowEstimate = parameters.ContainsKey("lowEstimate") ? Convert.ToDecimal(parameters["lowEstimate"]) : 0m;
        var highEstimate = parameters.ContainsKey("highEstimate") ? Convert.ToDecimal(parameters["highEstimate"]) : 0m;
        var hammerPrice = parameters.ContainsKey("hammerPrice") ? Convert.ToDecimal(parameters["hammerPrice"]) : 0m;
        var sold = parameters.ContainsKey("sold") ? Convert.ToBoolean(parameters["sold"]) : false;

        var artworkSale = new ArtworkSale
        {
            Id = Guid.NewGuid(),
            Name = name,
            Height = height,
            Width = width,
            YearCreated = yearCreated,
            SaleDate = saleDate,
            Technique = technique,
            Category = category,
            Currency = currency,
            LowEstimate = lowEstimate,
            HighEstimate = highEstimate,
            HammerPrice = hammerPrice,
            Sold = sold,
            ArtistId = artistId
        };

        _dbContext.ArtworkSales.Add(artworkSale);
        await _dbContext.SaveChangesAsync();

        return new
        {
            success = true,
            message = "Artwork sale created successfully",
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
                    name = new
                    {
                        type = "string",
                        description = "The name of the artwork"
                    },
                    artistId = new
                    {
                        type = "string",
                        description = "The GUID of the artist who created the artwork"
                    },
                    height = new
                    {
                        type = "integer",
                        description = "The height of the artwork in centimeters (optional)"
                    },
                    width = new
                    {
                        type = "integer",
                        description = "The width of the artwork in centimeters (optional)"
                    },
                    yearCreated = new
                    {
                        type = "integer",
                        description = "The year the artwork was created (optional)"
                    },
                    saleDate = new
                    {
                        type = "string",
                        description = "The date of the sale in ISO 8601 format (optional)"
                    },
                    technique = new
                    {
                        type = "string",
                        description = "The technique used for the artwork (optional)"
                    },
                    category = new
                    {
                        type = "string",
                        description = "The category of the artwork (optional)"
                    },
                    currency = new
                    {
                        type = "string",
                        description = "The currency code (e.g., USD, EUR) (optional, default: USD)"
                    },
                    lowEstimate = new
                    {
                        type = "number",
                        description = "The low estimate price (optional)"
                    },
                    highEstimate = new
                    {
                        type = "number",
                        description = "The high estimate price (optional)"
                    },
                    hammerPrice = new
                    {
                        type = "number",
                        description = "The final hammer price (optional)"
                    },
                    sold = new
                    {
                        type = "boolean",
                        description = "Whether the artwork was sold (optional, default: false)"
                    }
                },
                required = new[] { "name", "artistId" }
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
                                ["name"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string"
                                },
                                ["artistId"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string",
                                    ["format"] = "uuid"
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
                                }
                            },
                            ["required"] = new[] { "name", "artistId" }
                        }
                    }
                }
            },
            ["responses"] = new Dictionary<string, object>
            {
                ["200"] = new Dictionary<string, object>
                {
                    ["description"] = "Artwork sale created successfully",
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
