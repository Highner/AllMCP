using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Artworks;

[McpTool("search_artworks", "Searches for artworks by name using fuzzy matching")]
public class SearchArtworksTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public SearchArtworksTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "search_artworks";
    public string Description => "Searches for artworks by name using fuzzy matching";
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

        var artworks = await _dbContext.Artworks
            .Include(a => a.Artist)
            .ToListAsync();

        // Perform fuzzy matching
        var results = artworks
            .Select(a => new
            {
                artwork = a,
                distance = CalculateLevenshteinDistance(query, a.Name.ToLower()),
                containsMatch = a.Name.ToLower().Contains(query)
            })
            .Where(x => x.distance <= query.Length || x.containsMatch)
            .OrderBy(x => CalculateRelevanceScore(x.distance, query.Length, x.containsMatch))
            .Take(10)
            .Select(x => new
            {
                id = x.artwork.Id,
                name = x.artwork.Name,
                height = x.artwork.Height,
                width = x.artwork.Width,
                yearCreated = x.artwork.YearCreated,
                artistId = x.artwork.ArtistId,
                artist = x.artwork.Artist != null ? new
                {
                    id = x.artwork.Artist.Id,
                    firstName = x.artwork.Artist.FirstName,
                    lastName = x.artwork.Artist.LastName
                } : null,
                relevanceScore = CalculateRelevanceScore(x.distance, query.Length, x.containsMatch)
            })
            .ToList();

        return new
        {
            success = true,
            query = query,
            count = results.Count,
            artworks = results
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
                        description = "The search query to match against artwork names"
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
                    }
                }
            },
            ["responses"] = new Dictionary<string, object>
            {
                ["200"] = new Dictionary<string, object>
                {
                    ["description"] = "Search results for artworks",
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
                                    ["count"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "integer"
                                    },
                                    ["artworks"] = new Dictionary<string, object>
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
