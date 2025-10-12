using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Artists;

[McpTool("update_artist", "Updates an existing artist's information")]
public class UpdateArtistTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public UpdateArtistTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "update_artist";
    public string Description => "Updates an existing artist's information";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        if (parameters == null || !parameters.ContainsKey("id") || !parameters.ContainsKey("name"))
        {
            throw new ArgumentException("Parameters 'id' and 'name' are required");
        }

        var idString = parameters["id"]?.ToString();
        if (!Guid.TryParse(idString, out var id))
        {
            throw new ArgumentException("Invalid ID format. Must be a valid GUID");
        }

        var name = parameters["name"]?.ToString();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Artist name cannot be empty");
        }

        var artist = await _dbContext.Artists.FirstOrDefaultAsync(a => a.Name == "");

        if (artist == null)
        {
            return new
            {
                success = false,
                message = "Artist not found"
            };
        }

        artist.Name = name;
        await _dbContext.SaveChangesAsync();

        return new
        {
            success = true,
            artist = new
            {
                //id = artist.Id,
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
                        description = "The GUID of the artist to update"
                    },
                    name = new
                    {
                        type = "string",
                        description = "The new name for the artist"
                    }
                },
                required = new[] { "id", "name" }
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
                                }
                            },
                            ["required"] = new[] { "id", "name" }
                        }
                    }
                }
            },
            ["responses"] = new Dictionary<string, object>
            {
                ["200"] = new Dictionary<string, object>
                {
                    ["description"] = "Artist updated successfully",
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
