using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Artists;

[McpTool("get_all_artists", "Retrieves all artists from the database")]
public class GetAllArtistsTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public GetAllArtistsTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "get_all_artists";
    public string Description => "Retrieves all artists from the database";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        var artists = await _dbContext.Artists.ToListAsync();

        return new
        {
            success = true,
            count = artists.Count,
            artists = artists.Select(a => new
            {
                id = a.Id,
                firstName = a.FirstName,
                lastName = a.LastName
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
                    ["description"] = "List of all artists",
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
                                    ["artists"] = new Dictionary<string, object>
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