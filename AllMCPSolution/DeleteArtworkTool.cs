using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Artworks;

[McpTool("delete_artwork", "Deletes an artwork from the database")]
public class DeleteArtworkTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public DeleteArtworkTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "delete_artwork";
    public string Description => "Deletes an artwork from the database";
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

        try
        {
            _dbContext.Artworks.Remove(artwork);
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception e)
        {
            return e.Message + e.StackTrace;
        }

        return new
        {
            success = true,
            message = "Artwork deleted successfully",
            deletedArtworkId = id
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
                        description = "The GUID of the artwork to delete"
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
                    ["description"] = "Artwork deleted successfully",
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
                                    ["deletedArtworkId"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "string",
                                        ["format"] = "uuid"
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
