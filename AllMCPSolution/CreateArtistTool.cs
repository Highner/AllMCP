using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;

namespace AllMCPSolution.Artists;

[McpTool("create_artist", "Creates a new artist in the database")]
public class CreateArtistTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public CreateArtistTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "create_artist";
    public string Description => "Creates a new artist in the database";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        if (parameters == null || !parameters.ContainsKey("firstName") || !parameters.ContainsKey("lastName"))
        {
            throw new ArgumentException("Parameters 'firstName' and 'lastName' are required");
        }

        var firstName = parameters["firstName"]?.ToString();
        var lastName = parameters["lastName"]?.ToString();

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            throw new ArgumentException("First name and last name cannot be empty");
        }

        var artist = new Artist
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName
        };

        _dbContext.Artists.Add(artist);
        await _dbContext.SaveChangesAsync();

        return new
        {
            success = true,
            message = "Artist created successfully",
            artist = new
            {
                id = artist.Id,
                firstName = artist.FirstName,
                lastName = artist.LastName
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
                    firstName = new
                    {
                        type = "string",
                        description = "The first name of the artist"
                    },
                    lastName = new
                    {
                        type = "string",
                        description = "The last name of the artist"
                    }
                },
                required = new[] { "firstName", "lastName" }
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
                                ["firstName"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string"
                                },
                                ["lastName"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string"
                                }
                            },
                            ["required"] = new[] { "firstName", "lastName" }
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
                                    ["message"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "string"
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