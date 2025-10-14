
using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AllMCPSolution.Artworks;

[McpTool("batch_delete_artwork_sales", "Deletes multiple artwork sales in a single batch operation")]
public class BatchDeleteArtworkSalesTool : IToolBase
{
    private readonly ApplicationDbContext _dbContext;

    public BatchDeleteArtworkSalesTool(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "batch_delete_artwork_sales";
    public string Description => "Deletes multiple artwork sales in a single batch operation";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        if (parameters == null || !parameters.ContainsKey("ids"))
        {
            throw new ArgumentException("Parameter 'ids' is required");
        }

        var idsParam = parameters["ids"];
        var idsJson = JsonSerializer.Serialize(idsParam);
        var idsData = JsonSerializer.Deserialize<List<string>>(idsJson);

        if (idsData == null || idsData.Count == 0)
        {
            throw new ArgumentException("At least one ID must be provided");
        }

        var deletedIds = new List<Guid>();
        var errors = new List<object>();

        foreach (var idString in idsData)
        {
            try
            {
                if (!Guid.TryParse(idString, out var id))
                {
                    errors.Add(new
                    {
                        id = idString,
                        error = "Invalid ID format. Must be a valid GUID"
                    });
                    continue;
                }

                var artworkSale = await _dbContext.ArtworkSales.FirstOrDefaultAsync(a => a.Id == id);

                if (artworkSale == null)
                {
                    errors.Add(new
                    {
                        id = idString,
                        error = "Artwork sale not found"
                    });
                    continue;
                }

                _dbContext.ArtworkSales.Remove(artworkSale);
                deletedIds.Add(id);
            }
            catch (Exception ex)
            {
                errors.Add(new
                {
                    id = idString,
                    error = ex.Message
                });
            }
        }

        if (deletedIds.Count > 0)
        {
            await _dbContext.SaveChangesAsync();
        }

        return new
        {
            success = true,
            message = $"Batch delete completed. Deleted: {deletedIds.Count}, Failed: {errors.Count}",
            deletedIds = deletedIds,
            errors = errors
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
                    ids = new
                    {
                        type = "array",
                        description = "Array of artwork sale GUIDs to delete",
                        items = new
                        {
                            type = "string",
                            description = "GUID of the artwork sale to delete"
                        }
                    }
                },
                required = new[] { "ids" }
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
                                ["ids"] = new Dictionary<string, object>
                                {
                                    ["type"] = "array",
                                    ["items"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "string",
                                        ["format"] = "uuid"
                                    }
                                }
                            },
                            ["required"] = new[] { "ids" }
                        }
                    }
                }
            },
            ["responses"] = new Dictionary<string, object>
            {
                ["200"] = new Dictionary<string, object>
                {
                    ["description"] = "Batch delete completed",
                    ["content"] = new Dictionary<string, object>
                    {
                        ["application/json"] = new Dictionary<string, object>
                        {
                            ["schema"] = new Dictionary<string, object>
                            {
                                ["type"] = "object",
                                ["properties"] = new Dictionary<string, object>
                                {
                                    ["success"] = new Dictionary<string, object> { ["type"] = "boolean" },
                                    ["message"] = new Dictionary<string, object> { ["type"] = "string" },
                                    ["deletedIds"] = new Dictionary<string, object> { ["type"] = "array" },
                                    ["errors"] = new Dictionary<string, object> { ["type"] = "array" }
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}
