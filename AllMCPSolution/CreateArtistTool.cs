
using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;

namespace AllMCPSolution.Artists;

[McpTool("create_artist", "Creates a new artist with the specified name")]
public class CreateArtistTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public CreateArtistTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "create_artist";
    public string Description => "Creates a new artist with the specified name";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        if (parameters == null || !parameters.ContainsKey("name"))
        {
            throw new ArgumentException("Parameter 'name' is required");
        }

        var name = parameters["name"]?.ToString();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Artist name cannot be empty");
        }

        var artist = new Artist
        {
            Id = Guid.NewGuid(),
            Name = name
        };

        try
        {
            _dbContext.Artists.Add(artist);
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception e)
        {
            return e.Message + e.StackTrace;
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
                    name = new
                    {
                        type = "string",
                        description = "The name of the artist"
                    }
                },
                required = new[] { "name" }
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
                                }
                            },
                            ["required"] = new[] { "name" }
                        }
                    }
                }
            },
            ["responses"] = new Dictionary<string, object>
            {
                ["200"] = new Dictionary<string, object>
                {
                    ["description"] = "Artist created successfully",
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
