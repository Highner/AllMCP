using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Artworks;

[McpTool("search_artwork_sales", "Searches for artwork sales using filters")]
public class SearchArtworkSalesTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;
    private const int PageSize = 20; // Fixed page size

    public SearchArtworkSalesTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "search_artwork_sales";
    public string Description => "Searches for artwork sales using optional filters and pagination. Returns 20 results per page ordered by sale date.";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        if (parameters == null)
        {
            throw new ArgumentException("Parameters are required");
        }

        // Default page value
        int page = 1;

        if (parameters.ContainsKey("page") && int.TryParse(parameters["page"]?.ToString(), out int p))
            page = Math.Max(1, p);

        // Parse optional parameters
        Guid? artistId = null;
        if (parameters.ContainsKey("artistId") && parameters["artistId"] != null)
        {
            if (Guid.TryParse(parameters["artistId"]?.ToString(), out var parsedArtistId))
            {
                artistId = parsedArtistId;
            }
        }

        string? category = null;
        if (parameters.ContainsKey("category") && parameters["category"] != null)
        {
            category = parameters["category"]?.ToString()?.Trim();
        }

        decimal? minHammerPrice = null;
        if (parameters.ContainsKey("minHammerPrice") && parameters["minHammerPrice"] != null)
        {
            if (decimal.TryParse(parameters["minHammerPrice"]?.ToString(), out var parsedMinHammerPrice))
            {
                minHammerPrice = parsedMinHammerPrice;
            }
        }

        decimal? maxHammerPrice = null;
        if (parameters.ContainsKey("maxHammerPrice") && parameters["maxHammerPrice"] != null)
        {
            if (decimal.TryParse(parameters["maxHammerPrice"]?.ToString(), out var parsedMaxHammerPrice))
            {
                maxHammerPrice = parsedMaxHammerPrice;
            }
        }

        decimal? minHeight = null;
        if (parameters.ContainsKey("minHeight") && parameters["minHeight"] != null)
        {
            if (decimal.TryParse(parameters["minHeight"]?.ToString(), out var parsedMinHeight))
            {
                minHeight = parsedMinHeight;
            }
        }

        decimal? maxHeight = null;
        if (parameters.ContainsKey("maxHeight") && parameters["maxHeight"] != null)
        {
            if (decimal.TryParse(parameters["maxHeight"]?.ToString(), out var parsedMaxHeight))
            {
                maxHeight = parsedMaxHeight;
            }
        }

        decimal? minWidth = null;
        if (parameters.ContainsKey("minWidth") && parameters["minWidth"] != null)
        {
            if (decimal.TryParse(parameters["minWidth"]?.ToString(), out var parsedMinWidth))
            {
                minWidth = parsedMinWidth;
            }
        }

        decimal? maxWidth = null;
        if (parameters.ContainsKey("maxWidth") && parameters["maxWidth"] != null)
        {
            if (decimal.TryParse(parameters["maxWidth"]?.ToString(), out var parsedMaxWidth))
            {
                maxWidth = parsedMaxWidth;
            }
        }

        int? minYearCreated = null;
        if (parameters.ContainsKey("minYearCreated") && parameters["minYearCreated"] != null)
        {
            if (int.TryParse(parameters["minYearCreated"]?.ToString(), out var parsedMinYearCreated))
            {
                minYearCreated = parsedMinYearCreated;
            }
        }

        int? maxYearCreated = null;
        if (parameters.ContainsKey("maxYearCreated") && parameters["maxYearCreated"] != null)
        {
            if (int.TryParse(parameters["maxYearCreated"]?.ToString(), out var parsedMaxYearCreated))
            {
                maxYearCreated = parsedMaxYearCreated;
            }
        }

        // Check if at least one filter is provided
        if (!artistId.HasValue && string.IsNullOrWhiteSpace(category) &&
            !minHammerPrice.HasValue && !maxHammerPrice.HasValue &&
            !minHeight.HasValue && !maxHeight.HasValue &&
            !minWidth.HasValue && !maxWidth.HasValue &&
            !minYearCreated.HasValue && !maxYearCreated.HasValue)
        {
            throw new ArgumentException("At least one search parameter (artistId, category, or price/dimension filters) must be provided");
        }

        var artworkSales = await _dbContext.ArtworkSales
            .Include(a => a.Artist)
            .ToListAsync();

        // Apply filters
        var filteredSales = artworkSales.AsEnumerable();

        if (artistId.HasValue)
        {
            filteredSales = filteredSales.Where(a => a.ArtistId == artistId.Value);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            filteredSales = filteredSales.Where(a => 
                a.Category != null && a.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        if (minHammerPrice.HasValue)
        {
            filteredSales = filteredSales.Where(a => a.HammerPrice >= minHammerPrice.Value);
        }

        if (maxHammerPrice.HasValue)
        {
            filteredSales = filteredSales.Where(a => a.HammerPrice <= maxHammerPrice.Value);
        }

        if (minHeight.HasValue)
        {
            filteredSales = filteredSales.Where(a => a.Height >= minHeight.Value);
        }

        if (maxHeight.HasValue)
        {
            filteredSales = filteredSales.Where(a => a.Height <= maxHeight.Value);
        }

        if (minWidth.HasValue)
        {
            filteredSales = filteredSales.Where(a => a.Width >= minWidth.Value);
        }

        if (maxWidth.HasValue)
        {
            filteredSales = filteredSales.Where(a => a.Width <= maxWidth.Value);
        }

        if (minYearCreated.HasValue)
        {
            filteredSales = filteredSales.Where(a => a.YearCreated >= minYearCreated.Value);
        }

        if (maxYearCreated.HasValue)
        {
            filteredSales = filteredSales.Where(a => a.YearCreated <= maxYearCreated.Value);
        }

        // Order by name and create results
        var allResults = filteredSales
            .OrderBy(a => a.Name)
            .Select(a => new
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
            })
            .OrderByDescending(x => x.saleDate).ToList();

        var totalCount = allResults.Count;
        var totalPages = (int)Math.Ceiling(totalCount / (double)PageSize);

        var results = allResults
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        return new
        {
            success = true,
            filters = new
            {
                artistId,
                category,
                hammerPriceRange = minHammerPrice.HasValue || maxHammerPrice.HasValue 
                    ? new { min = minHammerPrice, max = maxHammerPrice } 
                    : null,
                heightRange = minHeight.HasValue || maxHeight.HasValue 
                    ? new { min = minHeight, max = maxHeight } 
                    : null,
                widthRange = minWidth.HasValue || maxWidth.HasValue 
                    ? new { min = minWidth, max = maxWidth } 
                    : null,
                yearCreatedRange = minYearCreated.HasValue || maxYearCreated.HasValue 
                    ? new { min = minYearCreated, max = maxYearCreated } 
                    : null
            },
            pagination = new
            {
                currentPage = page,
                pageSize = PageSize,
                totalItems = totalCount,
                totalPages = totalPages,
                hasNextPage = page < totalPages,
                hasPreviousPage = page > 1
            },
            count = results.Count,
            artworkSales = results
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
                    artistId = new
                    {
                        type = "string",
                        format = "uuid",
                        description = "Filter by artist ID"
                    },
                    category = new
                    {
                        type = "string",
                        description = "Filter by category"
                    },
                    minHammerPrice = new
                    {
                        type = "number",
                        description = "Minimum hammer price filter"
                    },
                    maxHammerPrice = new
                    {
                        type = "number",
                        description = "Maximum hammer price filter"
                    },
                    minHeight = new
                    {
                        type = "number",
                        description = "Minimum height filter"
                    },
                    maxHeight = new
                    {
                        type = "number",
                        description = "Maximum height filter"
                    },
                    minWidth = new
                    {
                        type = "number",
                        description = "Minimum width filter"
                    },
                    maxWidth = new
                    {
                        type = "number",
                        description = "Maximum width filter"
                    },
                    minYearCreated = new
                    {
                        type = "integer",
                        description = "Minimum year created filter"
                    },
                    maxYearCreated = new
                    {
                        type = "integer",
                        description = "Maximum year created filter"
                    },
                    page = new
                    {
                        type = "integer",
                        description = "Page number to retrieve (default: 1). Returns 20 results per page.",
                        minimum = 1
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
                    ["name"] = "artistId",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["format"] = "uuid"
                    },
                    ["description"] = "Filter by artist ID"
                },
                new Dictionary<string, object>
                {
                    ["name"] = "category",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "string"
                    },
                    ["description"] = "Filter by category"
                },
                new Dictionary<string, object>
                {
                    ["name"] = "minHammerPrice",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "number"
                    },
                    ["description"] = "Minimum hammer price filter"
                },
                new Dictionary<string, object>
                {
                    ["name"] = "maxHammerPrice",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "number"
                    },
                    ["description"] = "Maximum hammer price filter"
                },
                new Dictionary<string, object>
                {
                    ["name"] = "minHeight",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "number"
                    },
                    ["description"] = "Minimum height filter"
                },
                new Dictionary<string, object>
                {
                    ["name"] = "maxHeight",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "number"
                    },
                    ["description"] = "Maximum height filter"
                },
                new Dictionary<string, object>
                {
                    ["name"] = "minWidth",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "number"
                    },
                    ["description"] = "Minimum width filter"
                },
                new Dictionary<string, object>
                {
                    ["name"] = "maxWidth",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "number"
                    },
                    ["description"] = "Maximum width filter"
                },
                new Dictionary<string, object>
                {
                    ["name"] = "minYearCreated",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "integer"
                    },
                    ["description"] = "Minimum year created filter"
                },
                new Dictionary<string, object>
                {
                    ["name"] = "maxYearCreated",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "integer"
                    },
                    ["description"] = "Maximum year created filter"
                },
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
                    ["description"] = "Page number to retrieve. Returns 20 results per page."
                }
            },
            ["responses"] = new Dictionary<string, object>
            {
                ["200"] = new Dictionary<string, object>
                {
                    ["description"] = "Paginated search results for artwork sales (20 per page)",
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
                                    ["filters"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "object"
                                    },
                                    ["pagination"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "object",
                                        ["properties"] = new Dictionary<string, object>
                                        {
                                            ["currentPage"] = new Dictionary<string, object>
                                            {
                                                ["type"] = "integer"
                                            },
                                            ["pageSize"] = new Dictionary<string, object>
                                            {
                                                ["type"] = "integer",
                                                ["enum"] = new[] { 20 }
                                            },
                                            ["totalItems"] = new Dictionary<string, object>
                                            {
                                                ["type"] = "integer"
                                            },
                                            ["totalPages"] = new Dictionary<string, object>
                                            {
                                                ["type"] = "integer"
                                            },
                                            ["hasNextPage"] = new Dictionary<string, object>
                                            {
                                                ["type"] = "boolean"
                                            },
                                            ["hasPreviousPage"] = new Dictionary<string, object>
                                            {
                                                ["type"] = "boolean"
                                            }
                                        }
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