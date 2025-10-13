using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using System.Text.Json;

namespace AllMCPSolution.Artworks;

[McpTool("batch_create_artwork_sales", "Creates multiple artwork sales in a single batch operation. you can use up to 30 sales per batch.")]
public class BatchCreateArtworkSalesTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public BatchCreateArtworkSalesTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "batch_create_artwork_sales";
    public string Description => "Creates multiple artwork sales in a single batch operation";
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

        var createdSales = new List<object>();
        var errors = new List<object>();

        foreach (var saleData in artworkSalesData)
        {
            try
            {
                if (!saleData.ContainsKey("name") || !saleData.ContainsKey("artistId"))
                {
                    errors.Add(new
                    {
                        data = saleData,
                        error = "Parameters 'name' and 'artistId' are required"
                    });
                    continue;
                }

                var name = saleData["name"]?.ToString();
                var artistIdString = saleData["artistId"]?.ToString();

                if (string.IsNullOrWhiteSpace(name))
                {
                    errors.Add(new
                    {
                        data = saleData,
                        error = "Name cannot be empty"
                    });
                    continue;
                }

                if (!Guid.TryParse(artistIdString, out var artistId))
                {
                    errors.Add(new
                    {
                        data = saleData,
                        error = "Invalid artistId format. Must be a valid GUID"
                    });
                    continue;
                }

                var height = GetDecimalValue(saleData, "height", 0);
                var width = GetDecimalValue(saleData, "width", 0);
                var yearCreated = GetIntValue(saleData, "yearCreated", 0);
                var saleDate = GetDateTimeValue(saleData, "saleDate", DateTime.Now);
                var technique = GetStringValue(saleData, "technique", "");
                var category = GetStringValue(saleData, "category", "");
                var currency = GetStringValue(saleData, "currency", "USD");
                var lowEstimate = GetDecimalValue(saleData, "lowEstimate", 0m);
                var highEstimate = GetDecimalValue(saleData, "highEstimate", 0m);
                var hammerPrice = GetDecimalValue(saleData, "hammerPrice", 0m);
                var sold = GetBoolValue(saleData, "sold", false);

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

                createdSales.Add(new
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

        if (createdSales.Count > 0)
        {
            await _dbContext.SaveChangesAsync();
        }

        return new
        {
            success = true,
            message = $"Batch create completed. Created: {createdSales.Count}, Failed: {errors.Count}",
            created = createdSales,
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
                        description = "Array of artwork sales to create",
                        items = new
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
                                        ["required"] = new[] { "name", "artistId" }
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
                    ["description"] = "Batch create completed",
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
                                    ["created"] = new Dictionary<string, object> { ["type"] = "array" },
                                    ["errors"] = new Dictionary<string, object> { ["type"] = "array" }
                                }
                            }
                        }
                    }
                }
            }
        };
    }
    private static int GetIntValue(Dictionary<string, object> data, string key, int defaultValue)
    {
        if (!data.ContainsKey(key)) return defaultValue;
        
        var value = data[key];
        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Number)
                return jsonElement.GetInt32();
            if (jsonElement.ValueKind == JsonValueKind.String && int.TryParse(jsonElement.GetString(), out var intValue))
                return intValue;
        }
        return Convert.ToInt32(value);
    }

    private static decimal GetDecimalValue(Dictionary<string, object> data, string key, decimal defaultValue)
    {
        if (!data.ContainsKey(key)) return defaultValue;
        
        var value = data[key];
        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Number)
                return jsonElement.GetDecimal();
            if (jsonElement.ValueKind == JsonValueKind.String && decimal.TryParse(jsonElement.GetString(), out var decimalValue))
                return decimalValue;
        }
        return Convert.ToDecimal(value);
    }

    private static bool GetBoolValue(Dictionary<string, object> data, string key, bool defaultValue)
    {
        if (!data.ContainsKey(key)) return defaultValue;
        
        var value = data[key];
        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.True) return true;
            if (jsonElement.ValueKind == JsonValueKind.False) return false;
            if (jsonElement.ValueKind == JsonValueKind.String && bool.TryParse(jsonElement.GetString(), out var boolValue))
                return boolValue;
        }
        return Convert.ToBoolean(value);
    }

    private static string GetStringValue(Dictionary<string, object> data, string key, string defaultValue)
    {
        if (!data.ContainsKey(key)) return defaultValue;
        
        var value = data[key];
        if (value is JsonElement jsonElement)
        {
            return jsonElement.GetString() ?? defaultValue;
        }
        return value?.ToString() ?? defaultValue;
    }

    private static DateTime GetDateTimeValue(Dictionary<string, object> data, string key, DateTime defaultValue)
    {
        if (!data.ContainsKey(key)) return defaultValue;
        
        var value = data[key];
        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                var dateString = jsonElement.GetString();
                if (DateTime.TryParse(dateString, out var dateValue))
                    return dateValue;
            }
        }
        return Convert.ToDateTime(value);
    }

}
