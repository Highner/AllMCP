using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Artworks;

[McpTool("search_artwork_sales", "Searches for artwork sales by name using fuzzy matching with optional filters")]
public class SearchArtworkSalesTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public SearchArtworkSalesTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "search_artwork_sales";
    public string Description => "Searches for artwork sales by name using fuzzy matching with optional filters";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        if (parameters == null || !parameters.ContainsKey("query"))
        {
            throw new ArgumentException("Parameter 'query' is required");
        }

        var query = parameters["query"]?.ToString()?.ToLower();
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query cannot be empty");
        }

        // Parse optional parameters
        Guid? artistId = null;
        if (parameters.ContainsKey("artistId") && parameters["artistId"] != null)
        {
            if (Guid.TryParse(parameters["artistId"].ToString(), out var parsedArtistId))
            {
                artistId = parsedArtistId;
            }
        }

        string? category = null;
        if (parameters.ContainsKey("category") && parameters["category"] != null)
        {
            category = parameters["category"].ToString();
        }

        decimal? minHammerPrice = null;
        if (parameters.ContainsKey("minHammerPrice") && parameters["minHammerPrice"] != null)
        {
            if (decimal.TryParse(parameters["minHammerPrice"].ToString(), out var parsedMin))
            {
                minHammerPrice = parsedMin;
            }
        }

        decimal? maxHammerPrice = null;
        if (parameters.ContainsKey("maxHammerPrice") && parameters["maxHammerPrice"] != null)
        {
            if (decimal.TryParse(parameters["maxHammerPrice"].ToString(), out var parsedMax))
            {
                maxHammerPrice = parsedMax;
            }
        }

        decimal? minHeight = null;
        if (parameters.ContainsKey("minHeight") && parameters["minHeight"] != null)
        {
            if (decimal.TryParse(parameters["minHeight"].ToString(), out var parsedMin))
            {
                minHeight = parsedMin;
            }
        }

        decimal? maxHeight = null;
        if (parameters.ContainsKey("maxHeight") && parameters["maxHeight"] != null)
        {
            if (decimal.TryParse(parameters["maxHeight"].ToString(), out var parsedMax))
            {
                maxHeight = parsedMax;
            }
        }

        decimal? minWidth = null;
        if (parameters.ContainsKey("minWidth") && parameters["minWidth"] != null)
        {
            if (decimal.TryParse(parameters["minWidth"].ToString(), out var parsedMin))
            {
                minWidth = parsedMin;
            }
        }

        decimal? maxWidth = null;
        if (parameters.ContainsKey("maxWidth") && parameters["maxWidth"] != null)
        {
            if (decimal.TryParse(parameters["maxWidth"].ToString(), out var parsedMax))
            {
                maxWidth = parsedMax;
            }
        }

        int? minYearCreated = null;
        if (parameters.ContainsKey("minYearCreated") && parameters["minYearCreated"] != null)
        {
            if (int.TryParse(parameters["minYearCreated"].ToString(), out var parsedMin))
            {
                minYearCreated = parsedMin;
            }
        }

        int? maxYearCreated = null;
        if (parameters.ContainsKey("maxYearCreated") && parameters["maxYearCreated"] != null)
        {
            if (int.TryParse(parameters["maxYearCreated"].ToString(), out var parsedMax))
            {
                maxYearCreated = parsedMax;
            }
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

        // Perform fuzzy matching
        var results = filteredSales
            .Select(a => new
            {
                artworkSale = a,
                distance = CalculateLevenshteinDistance(query, a.Name.ToLower()),
                containsMatch = a.Name.ToLower().Contains(query)
            })
            .Where(x => x.distance <= query.Length || x.containsMatch)
            .OrderBy(x => CalculateRelevanceScore(x.distance, query.Length, x.containsMatch))
            .Take(10)
            .Select(x => new
            {
                id = x.artworkSale.Id,
                name = x.artworkSale.Name,
                height = x.artworkSale.Height,
                width = x.artworkSale.Width,
                yearCreated = x.artworkSale.YearCreated,
                saleDate = x.artworkSale.SaleDate,
                technique = x.artworkSale.Technique,
                category = x.artworkSale.Category,
                currency = x.artworkSale.Currency,
                lowEstimate = x.artworkSale.LowEstimate,
                highEstimate = x.artworkSale.HighEstimate,
                hammerPrice = x.artworkSale.HammerPrice,
                sold = x.artworkSale.Sold,
                artistId = x.artworkSale.ArtistId,
                artist = x.artworkSale.Artist != null ? new
                {
                    id = x.artworkSale.Artist.Id,
                    firstName = x.artworkSale.Artist.FirstName,
                    lastName = x.artworkSale.Artist.LastName
                } : null,
                relevanceScore = CalculateRelevanceScore(x.distance, query.Length, x.containsMatch)
            })
            .ToList();

        return new
        {
            success = true,
            query = query,
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
            count = results.Count,
            artworkSales = results
        };
    }

    private int CalculateLevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
        {
            return string.IsNullOrEmpty(target) ? 0 : target.Length;
        }

        if (string.IsNullOrEmpty(target))
        {
            return source.Length;
        }

        var distance = new int[source.Length + 1, target.Length + 1];

        for (var i = 0; i <= source.Length; i++)
        {
            distance[i, 0] = i;
        }

        for (var j = 0; j <= target.Length; j++)
        {
            distance[0, j] = j;
        }

        for (var i = 1; i <= source.Length; i++)
        {
            for (var j = 1; j <= target.Length; j++)
            {
                var cost = target[j - 1] == source[i - 1] ? 0 : 1;
                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost);
            }
        }

        return distance[source.Length, target.Length];
    }

    private double CalculateRelevanceScore(int distance, int queryLength, bool containsMatch)
    {
        var score = (double)distance / queryLength;
        return containsMatch ? score - 0.5 : score; // Boost exact substring matches
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
                    query = new
                    {
                        type = "string",
                        description = "The search query to match against artwork sale names"
                    },
                    artistId = new
                    {
                        type = "string",
                        description = "Optional: Filter by artist ID (GUID format)"
                    },
                    category = new
                    {
                        type = "string",
                        description = "Optional: Filter by category"
                    },
                    minHammerPrice = new
                    {
                        type = "number",
                        description = "Optional: Minimum hammer price"
                    },
                    maxHammerPrice = new
                    {
                        type = "number",
                        description = "Optional: Maximum hammer price"
                    },
                    minHeight = new
                    {
                        type = "number",
                        description = "Optional: Minimum height"
                    },
                    maxHeight = new
                    {
                        type = "number",
                        description = "Optional: Maximum height"
                    },
                    minWidth = new
                    {
                        type = "number",
                        description = "Optional: Minimum width"
                    },
                    maxWidth = new
                    {
                        type = "number",
                        description = "Optional: Maximum width"
                    },
                    minYearCreated = new
                    {
                        type = "integer",
                        description = "Optional: Minimum year created"
                    },
                    maxYearCreated = new
                    {
                        type = "integer",
                        description = "Optional: Maximum year created"
                    }
                },
                required = new[] { "query" }
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
                    ["name"] = "query",
                    ["in"] = "query",
                    ["required"] = true,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "string"
                    },
                    ["description"] = "The search query to match against artwork sale names"
                },
                new Dictionary<string, object>
                {
                    ["name"] = "artistId",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "string"
                    },
                    ["description"] = "Optional: Filter by artist ID (GUID format)"
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
                    ["description"] = "Optional: Filter by category"
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
                    ["description"] = "Optional: Minimum hammer price"
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
                    ["description"] = "Optional: Maximum hammer price"
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
                    ["description"] = "Optional: Minimum height"
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
                    ["description"] = "Optional: Maximum height"
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
                    ["description"] = "Optional: Minimum width"
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
                    ["description"] = "Optional: Maximum width"
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
                    ["description"] = "Optional: Minimum year created"
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
                    ["description"] = "Optional: Maximum year created"
                }
            },
            ["responses"] = new Dictionary<string, object>
            {
                ["200"] = new Dictionary<string, object>
                {
                    ["description"] = "Search results for artwork sales",
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
                                    ["query"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "string"
                                    },
                                    ["filters"] = new Dictionary<string, object>
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