using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AllMCPSolution.Artworks;

[McpTool("batch_update_artwork_sales", "Updates multiple artwork sales in a single batch operation")]
public class BatchUpdateArtworkSalesTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public BatchUpdateArtworkSalesTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "batch_update_artwork_sales";
    public string Description => "Updates multiple artwork sales in a single batch operation";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        if (parameters == null || !parameters.ContainsKey("artworkSales"))
        {
            throw new ArgumentException("Parameter 'artworkSales' is required");
        }

        var artworkSalesParam = parameters["artworkSales"];
        var artworkSalesJson = JsonSerializer.Serialize(artworkSalesParam);
        var artworkSalesData = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(artworkSalesJson);

        if (artworkSalesData == null || artworkSalesData.Count == 0)
        {
            throw new ArgumentException("At least one artwork sale must be provided");
        }

        var updatedSales = new List<object>();
        var errors = new List<object>();

        foreach (var saleData in artworkSalesData)
        {
            try
            {
                if (!saleData.ContainsKey("id"))
                {
                    errors.Add(new
                    {
                        data = saleData,
                        error = "Parameter 'id' is required"
                    });
                    continue;
                }

                var idString = saleData["id"]?.ToString();
                if (!Guid.TryParse(idString, out var id))
                {
                    errors.Add(new
                    {
                        data = saleData,
                        error = "Invalid ID format. Must be a valid GUID"
                    });
                    continue;
                }

                var artworkSale = await _dbContext.ArtworkSales.FirstOrDefaultAsync(a => a.Id == id);

                if (artworkSale == null)
                {
                    errors.Add(new
                    {
                        data = saleData,
                        error = $"Artwork sale with ID {id} not found"
                    });
                    continue;
                }

                // Update fields if provided
                if (saleData.ContainsKey("name"))
                {
                    artworkSale.Name = saleData["name"]?.ToString() ?? artworkSale.Name;
                }
                if (saleData.ContainsKey("artistId"))
                {
                    var artistIdString = saleData["artistId"]?.ToString();
                    if (Guid.TryParse(artistIdString, out var artistId))
                    {
                        artworkSale.ArtistId = artistId;
                    }
                }
                if (saleData.ContainsKey("height"))
                {
                    artworkSale.Height = Convert.ToDecimal(saleData["height"]);
                }
                if (saleData.ContainsKey("width"))
                {
                    artworkSale.Width = Convert.ToDecimal(saleData["width"]);
                }
                if (saleData.ContainsKey("yearCreated"))
                {
                    artworkSale.YearCreated = Convert.ToInt32(saleData["yearCreated"]);
                }
                if (saleData.ContainsKey("saleDate"))
                {
                    artworkSale.SaleDate = Convert.ToDateTime(saleData["saleDate"]);
                }
                if (saleData.ContainsKey("technique"))
                {
                    artworkSale.Technique = saleData["technique"]?.ToString() ?? artworkSale.Technique;
                }
                if (saleData.ContainsKey("category"))
                {
                    artworkSale.Category = saleData["category"]?.ToString() ?? artworkSale.Category;
                }
                if (saleData.ContainsKey("currency"))
                {
                    artworkSale.Currency = saleData["currency"]?.ToString() ?? artworkSale.Currency;
                }
                if (saleData.ContainsKey("lowEstimate"))
                {
                    artworkSale.LowEstimate = Convert.ToDecimal(saleData["lowEstimate"]);
                }
                if (saleData.ContainsKey("highEstimate"))
                {
                    artworkSale.HighEstimate = Convert.ToDecimal(saleData["highEstimate"]);
                }
                if (saleData.ContainsKey("hammerPrice"))
                {
                    artworkSale.HammerPrice = Convert.ToDecimal(saleData["hammerPrice"]);
                }
                if (saleData.ContainsKey("sold"))
                {
                    artworkSale.Sold = Convert.ToBoolean(saleData["sold"]);
                }

                _dbContext.ArtworkSales.Update(artworkSale);

                updatedSales.Add(new
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
                });
            }
            catch (Exception ex)
            {
                errors.Add(new
                {
                    data = saleData,
                    error = ex.Message
                });
            }
        }

        if (updatedSales.Count > 0)
        {
            await _dbContext.SaveChangesAsync();
        }

        return new
        {
            success = true,
            message = $"Batch update completed. Updated: {updatedSales.Count}, Failed: {errors.Count}",
            updated = updatedSales,
            errors = errors
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
                    artworkSales = new
                    {
                        type = "array",
                        description = "Array of artwork sales to update. Each must have an 'id' field.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                id = new
                                {
                                    type = "string",
                                    description = "The GUID of the artwork sale to update (required)"
                                },
                                name = new
                                {
                                    type = "string",
                                    description = "The name of the artwork (optional)"
                                },
                                artistId = new
                                {
                                    type = "string",
                                    description = "The GUID of the artist who created the artwork (optional)"
                                },
                                height = new
                                {
                                    type = "number",
                                    description = "The height of the artwork in centimeters (optional)"
                                },
                                width = new
                                {
                                    type = "number",
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
                                    description = "The currency code (e.g., USD, EUR) (optional)"
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
                                    description = "Whether the artwork was sold (optional)"
                                }
                            },
                            required = new[] { "id" }
                        }
                    }
                },
                required = new[] { "artworkSales" }
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
                                ["artworkSales"] = new Dictionary<string, object>
                                {
                                    ["type"] = "array",
                                    ["items"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "object",
                                        ["properties"] = new Dictionary<string, object>
                                        {
                                            ["id"] = new Dictionary<string, object> { ["type"] = "string", ["format"] = "uuid" },
                                            ["name"] = new Dictionary<string, object> { ["type"] = "string" },
                                            ["artistId"] = new Dictionary<string, object> { ["type"] = "string", ["format"] = "uuid" },
                                            ["height"] = new Dictionary<string, object> { ["type"] = "number" },
                                            ["width"] = new Dictionary<string, object> { ["type"] = "number" },
                                            ["yearCreated"] = new Dictionary<string, object> { ["type"] = "integer" },
                                            ["saleDate"] = new Dictionary<string, object> { ["type"] = "string", ["format"] = "date-time" },
                                            ["technique"] = new Dictionary<string, object> { ["type"] = "string" },
                                            ["category"] = new Dictionary<string, object> { ["type"] = "string" },
                                            ["currency"] = new Dictionary<string, object> { ["type"] = "string" },
                                            ["lowEstimate"] = new Dictionary<string, object> { ["type"] = "number" },
                                            ["highEstimate"] = new Dictionary<string, object> { ["type"] = "number" },
                                            ["hammerPrice"] = new Dictionary<string, object> { ["type"] = "number" },
                                            ["sold"] = new Dictionary<string, object> { ["type"] = "boolean" }
                                        },
                                        ["required"] = new[] { "id" }
                                    }
                                }
                            },
                            ["required"] = new[] { "artworkSales" }
                        }
                    }
                }
            },
            ["responses"] = new Dictionary<string, object>
            {
                ["200"] = new Dictionary<string, object>
                {
                    ["description"] = "Batch update completed",
                    ["content"] = new Dictionary<string, object>
                    {
                        ["application/json"] = new Dictionary<string, object>
                        {
                            ["schema"] = new Dictionary<string, object>
                            {
                                ["type"] = "object",
                                ["properties"] = new Dictionary<string, object>
                                {
                                    ["success"] = new Dictionary<string, object> { ["type"] = "boolean" },
                                    ["message"] = new Dictionary<string, object> { ["type"] = "string" },
                                    ["updated"] = new Dictionary<string, object> { ["type"] = "array" },
                                    ["errors"] = new Dictionary<string, object> { ["type"] = "array" }
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}
