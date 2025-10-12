using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Artworks;

[McpTool("get_all_artworks", "Retrieves all artworks from the database")]
public class GetAllArtworksTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public GetAllArtworksTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "get_all_artworks";
    public string Description => "Retrieves all artworks from the database";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        var artworks = await _dbContext.Artworks
            .Include(a => a.Artist)
            .ToListAsync();

        return new
        {
            success = true,
            count = artworks.Count,
            artworks = artworks.Select(a => new
            {
                id = a.Id,
                name = a.Name,
                height = a.Height,
                width = a.Width,
                yearCreated = a.YearCreated,
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
                    ["description"] = "List of all artworks",
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
