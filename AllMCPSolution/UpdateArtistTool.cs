using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Artists;

[McpTool("update_artist", "Updates an existing artist in the database")]
public class UpdateArtistTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public UpdateArtistTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "update_artist";
    public string Description => "Updates an existing artist in the database";

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

        // Update firstName if provided
        if (parameters.ContainsKey("firstName"))
        {
            var firstName = parameters["firstName"]?.ToString();
            if (!string.IsNullOrWhiteSpace(firstName))
            {
                artist.FirstName = firstName;
            }
        }

        // Update lastName if provided
        if (parameters.ContainsKey("lastName"))
        {
            var lastName = parameters["lastName"]?.ToString();
            if (!string.IsNullOrWhiteSpace(lastName))
            {
                artist.LastName = lastName;
            }
        }

        await _dbContext.SaveChangesAsync();

        return new
        {
            success = true,
            message = "Artist updated successfully",
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
                    id = new
                    {
                        type = "string",
                        description = "The GUID of the artist to update"
                    },
                    firstName = new
                    {
                        type = "string",
                        description = "The new first name of the artist (optional)"
                    },
                    lastName = new
                    {
                        type = "string",
                        description = "The new last name of the artist (optional)"
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
                                ["firstName"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string"
                                },
                                ["lastName"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string"
                                }
                            },
                            ["required"] = new[] { "id" }
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