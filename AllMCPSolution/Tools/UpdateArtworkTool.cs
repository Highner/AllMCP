using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Artworks;

[McpTool("update_artwork", "Updates an existing artwork in the database")]
public class UpdateArtworkTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public UpdateArtworkTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "update_artwork";
    public string Description => "Updates an existing artwork in the database";
    public string? SafetyLevel => "non_critical";

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

        var artwork = await _dbContext.Artworks.FirstOrDefaultAsync(a => a.Id == id);

        if (artwork == null)
        {
            return new
            {
                success = false,
                message = "Artwork not found"
            };
        }

        // Update name if provided
        if (parameters.ContainsKey("name"))
        {
            var name = parameters["name"]?.ToString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                artwork.Name = name;
            }
        }

        // Update height if provided
        if (parameters.ContainsKey("height"))
        {
            artwork.Height = Convert.ToInt32(parameters["height"]);
        }

        // Update width if provided
        if (parameters.ContainsKey("width"))
        {
            artwork.Width = Convert.ToInt32(parameters["width"]);
        }

        // Update yearCreated if provided
        if (parameters.ContainsKey("yearCreated"))
        {
            artwork.YearCreated = Convert.ToInt32(parameters["yearCreated"]);
        }

        // Update artistId if provided
        if (parameters.ContainsKey("artistId"))
        {
            var artistIdString = parameters["artistId"]?.ToString();
            if (Guid.TryParse(artistIdString, out var artistId))
            {
                artwork.ArtistId = artistId;
            }
        }

        await _dbContext.SaveChangesAsync();

        return new
        {
            success = true,
            message = "Artwork updated successfully",
            artwork = new
            {
                id = artwork.Id,
                name = artwork.Name,
                height = artwork.Height,
                width = artwork.Width,
                yearCreated = artwork.YearCreated,
                artistId = artwork.ArtistId
            }
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
                    id = new
                    {
                        type = "string",
                        description = "The GUID of the artwork to update"
                    },
                    name = new
                    {
                        type = "string",
                        description = "The new name of the artwork (optional)"
                    },
                    height = new
                    {
                        type = "integer",
                        description = "The new height of the artwork in centimeters (optional)"
                    },
                    width = new
                    {
                        type = "integer",
                        description = "The new width of the artwork in centimeters (optional)"
                    },
                    yearCreated = new
                    {
                        type = "integer",
                        description = "The new year the artwork was created (optional)"
                    },
                    artistId = new
                    {
                        type = "string",
                        description = "The new GUID of the artist (optional)"
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
                                ["name"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string"
                                },
                                ["height"] = new Dictionary<string, object>
                                {
                                    ["type"] = "integer"
                                },
                                ["width"] = new Dictionary<string, object>
                                {
                                    ["type"] = "integer"
                                },
                                ["yearCreated"] = new Dictionary<string, object>
                                {
                                    ["type"] = "integer"
                                },
                                ["artistId"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string",
                                    ["format"] = "uuid"
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
                    ["description"] = "Artwork updated successfully",
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
                                    ["artwork"] = new Dictionary<string, object>
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
