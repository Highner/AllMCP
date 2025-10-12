
using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Artists;

[McpTool("get_artist_by_id", "Retrieves a specific artist by their ID")]
public class GetArtistByIdTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public GetArtistByIdTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "get_artist_by_id";
    public string Description => "Retrieves a specific artist by their ID";

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

        var artist = await _dbContext.Artists.FirstOrDefaultAsync(a => a.Id == id);

        if (artist == null)
        {
            return new
            {
                success = false,
                message = "Artist not found"
            };
        }

        return new
        {
            success = true,
            artist = new
            {
                id = artist.Id,
                name = artist.Name
            }
        };
    }

    public object GetToolDefinition()
    {
        return new
        {
            name = Name,
            description = Description,
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    id = new
                    {
                        type = "string",
                        description = "The GUID of the artist"
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
                    ["description"] = "Artist details",
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
                                    ["artist"] = new Dictionary<string, object>
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
