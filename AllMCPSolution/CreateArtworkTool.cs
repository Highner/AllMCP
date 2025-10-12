using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;

namespace AllMCPSolution.Artworks;

[McpTool("create_artwork", "Creates a new artwork in the database")]
public class CreateArtworkTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public CreateArtworkTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "create_artwork";
    public string Description => "Creates a new artwork in the database";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        if (parameters == null || !parameters.ContainsKey("name") || !parameters.ContainsKey("artistId"))
        {
            throw new ArgumentException("Parameters 'name' and 'artistId' are required");
        }

        var name = parameters["name"]?.ToString();
        var artistIdString = parameters["artistId"]?.ToString();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be empty");
        }

        if (!Guid.TryParse(artistIdString, out var artistId))
        {
            throw new ArgumentException("Invalid artistId format. Must be a valid GUID");
        }

        // Optional parameters with defaults
        var height = parameters.ContainsKey("height") ? Convert.ToInt32(parameters["height"]) : 0;
        var width = parameters.ContainsKey("width") ? Convert.ToInt32(parameters["width"]) : 0;
        var yearCreated = parameters.ContainsKey("yearCreated") ? Convert.ToInt32(parameters["yearCreated"]) : 0;

        var artwork = new Artwork
        {
            Id = Guid.NewGuid(),
            Name = name,
            Height = height,
            Width = width,
            YearCreated = yearCreated,
            ArtistId = artistId
        };

        _dbContext.Artworks.Add(artwork);
        await _dbContext.SaveChangesAsync();

        return new
        {
            success = true,
            message = "Artwork created successfully",
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
                    name = new
                    {
                        type = "string",
                        description = "The name of the artwork"
                    },
                    artistId = new
                    {
                        type = "string",
                        description = "The GUID of the artist who created the artwork"
                    },
                    height = new
                    {
                        type = "integer",
                        description = "The height of the artwork in centimeters (optional)"
                    },
                    width = new
                    {
                        type = "integer",
                        description = "The width of the artwork in centimeters (optional)"
                    },
                    yearCreated = new
                    {
                        type = "integer",
                        description = "The year the artwork was created (optional)"
                    }
                },
                required = new[] { "name", "artistId" }
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
                                },
                                ["artistId"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string",
                                    ["format"] = "uuid"
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
                                }
                            },
                            ["required"] = new[] { "name", "artistId" }
                        }
                    }
                }
            },
            ["responses"] = new Dictionary<string, object>
            {
                ["200"] = new Dictionary<string, object>
                {
                    ["description"] = "Artwork created successfully",
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
