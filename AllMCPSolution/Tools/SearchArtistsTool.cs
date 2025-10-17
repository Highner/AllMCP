using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AllMCPSolution.Artists;

[McpTool("search_artists", "Searches for artists using fuzzy matching on their names")]
public class SearchArtistsTool : IToolBase, IMcpTool
{
    private readonly AllMCPSolution.Repositories.IArtistRepository _artists;

    public SearchArtistsTool(AllMCPSolution.Repositories.IArtistRepository artists)
    {
        _artists = artists;
    }

    public string Name => "search_artists";
    public string Description => "Searches for artists using fuzzy matching on their names";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        if (parameters == null || !parameters.ContainsKey("query"))
        {
            return new { success = false, error = "Parameter 'query' is required" };
        }

        var query = parameters["query"]?.ToString();
        if (string.IsNullOrWhiteSpace(query))
        {
            return new { success = false, error = "Search query cannot be empty" };
        }

        // Get threshold for fuzzy matching (default to 3 for moderate tolerance)
        var threshold = 3;
        if (parameters.ContainsKey("threshold") && int.TryParse(parameters["threshold"]?.ToString(), out var parsedThreshold))
        {
            threshold = parsedThreshold;
        }

        var allArtists = await _artists.GetAllAsync(CancellationToken.None);

        // Perform fuzzy search on both first name, last name, and full name
        var searchResults = allArtists
            .Select(artist => {
                var first = artist.FirstName ?? string.Empty;
                var last = artist.LastName ?? string.Empty;
                var full = string.Concat(first, " ", last).Trim();
                var q = query.ToLowerInvariant();
                return new
                {
                    artist,
                    fullName = full,
                    firstNameDistance = CalculateLevenshteinDistance(q, first.ToLowerInvariant()),
                    lastNameDistance = CalculateLevenshteinDistance(q, last.ToLowerInvariant()),
                    fullNameDistance = CalculateLevenshteinDistance(q, full.ToLowerInvariant()),
                    firstNameContains = first.Contains(query, StringComparison.OrdinalIgnoreCase),
                    lastNameContains = last.Contains(query, StringComparison.OrdinalIgnoreCase),
                    fullNameContains = full.Contains(query, StringComparison.OrdinalIgnoreCase)
                }; 
            })
            .Select(x => new
            {
                x.artist,
                x.fullName,
                bestDistance = Math.Min(x.firstNameDistance, Math.Min(x.lastNameDistance, x.fullNameDistance)),
                containsMatch = x.firstNameContains || x.lastNameContains || x.fullNameContains
            })
            .Where(x => x.containsMatch || x.bestDistance <= threshold)
            .OrderBy(x => x.containsMatch ? 0 : 1) // Prioritize contains matches
            .ThenBy(x => x.bestDistance) // Then sort by similarity
            .Select(x => new
            {
                id = x.artist.Id,
                firstName = x.artist.FirstName,
                lastName = x.artist.LastName,
                fullName = x.fullName,
                relevanceScore = CalculateRelevanceScore(x.bestDistance, query.Length, x.containsMatch)
            })
            .ToList();

        return new
        {
            success = true,
            query,
            count = searchResults.Count,
            results = searchResults
        };
    }

    private static int CalculateLevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
        {
            return string.IsNullOrEmpty(target) ? 0 : target.Length;
        }

        if (string.IsNullOrEmpty(target))
        {
            return source.Length;
        }

        var sourceLength = source.Length;
        var targetLength = target.Length;
        var distance = new int[sourceLength + 1, targetLength + 1];

        // Initialize first column and row
        for (var i = 0; i <= sourceLength; i++)
        {
            distance[i, 0] = i;
        }

        for (var j = 0; j <= targetLength; j++)
        {
            distance[0, j] = j;
        }

        // Calculate distances
        for (var i = 1; i <= sourceLength; i++)
        {
            for (var j = 1; j <= targetLength; j++)
            {
                var cost = target[j - 1] == source[i - 1] ? 0 : 1;

                distance[i, j] = Math.Min(
                    Math.Min(
                        distance[i - 1, j] + 1,      // Deletion
                        distance[i, j - 1] + 1),     // Insertion
                    distance[i - 1, j - 1] + cost);  // Substitution
            }
        }

        return distance[sourceLength, targetLength];
    }

    private static double CalculateRelevanceScore(int distance, int queryLength, bool containsMatch)
    {
        if (containsMatch)
        {
            return 1.0; // Perfect match or contains the query
        }

        // Calculate similarity as a percentage (lower distance = higher score)
        var maxLength = Math.Max(distance, queryLength);
        if (maxLength == 0) return 1.0;

        return Math.Max(0, 1.0 - (double)distance / maxLength);
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
                    query = new
                    {
                        type = "string",
                        description = "The search query to match against artist names"
                    },
                    threshold = new
                    {
                        type = "integer",
                        description = "Maximum edit distance for fuzzy matching (default: 3). Lower values are more strict.",
                        @default = 3
                    }
                },
                required = new[] { "query" }
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
                    ["name"] = "query",
                    ["in"] = "query",
                    ["required"] = true,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "string"
                    },
                    ["description"] = "The search query to match against artist names"
                },
                new Dictionary<string, object>
                {
                    ["name"] = "threshold",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "integer",
                        ["default"] = 3
                    },
                    ["description"] = "Maximum edit distance for fuzzy matching"
                }
            },
            ["responses"] = new Dictionary<string, object>
            {
                ["200"] = new Dictionary<string, object>
                {
                    ["description"] = "Search results with fuzzy matching",
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
                                    ["query"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "string"
                                    },
                                    ["count"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "integer"
                                    },
                                    ["results"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "array",
                                        ["items"] = new Dictionary<string, object>
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
                                                },
                                                ["fullName"] = new Dictionary<string, object>
                                                {
                                                    ["type"] = "string"
                                                },
                                                ["relevanceScore"] = new Dictionary<string, object>
                                                {
                                                    ["type"] = "number",
                                                    ["description"] = "Relevance score from 0 to 1 (1 being perfect match)"
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    // IMcpTool implementation (delegates to ExecuteAsync)
    public Tool GetDefinition() => new Tool
    {
        Name = Name,
        Title = "Search artists",
        Description = Description,
        InputSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "The search query to match against artist names" },
            "threshold": { "type": "integer", "description": "Maximum edit distance (default 3)", "default": 3 }
          },
          "required": ["query"]
        }
        """).RootElement
    };

    public async ValueTask<CallToolResult> RunAsync(CallToolRequestParams request, CancellationToken ct)
    {
        try
        {
            Dictionary<string, object?>? dict = null;
            if (request?.Arguments is not null)
            {
                dict = new Dictionary<string, object?>();
                foreach (var kvp in request.Arguments)
                {
                    dict[kvp.Key] = kvp.Value.ValueKind switch
                    {
                        System.Text.Json.JsonValueKind.String => kvp.Value.GetString(),
                        System.Text.Json.JsonValueKind.Number => kvp.Value.TryGetInt32(out var i) ? i :
                            kvp.Value.TryGetInt64(out var l) ? l :
                            kvp.Value.TryGetDouble(out var d) ? d : null,
                        System.Text.Json.JsonValueKind.True => true,
                        System.Text.Json.JsonValueKind.False => false,
                        _ => null
                    };
                }
            }

            var result = await ExecuteAsync(dict);
            var node = JsonSerializer.SerializeToNode(result) as JsonObject;
            // Build a simple text message similar to HelloWorld for clients that read Content
            var msg = "";
            if (node != null)
            {
                if (node.TryGetPropertyValue("error", out var err) && err is JsonValue jvErr && jvErr.TryGetValue<string>(out var errStr))
                {
                    msg = $"search_artists: {errStr}";
                }
                else
                {
                    var q = node.TryGetPropertyValue("query", out var qn) && qn is JsonValue jvQ && jvQ.TryGetValue<string>(out var qs) ? qs : null;
                    var c = node.TryGetPropertyValue("count", out var cn) && cn is JsonValue jvC && jvC.TryGetValue<int>(out var ci) ? ci : (int?)null;
                    msg = q is not null && c.HasValue ? $"Found {c} artists for '{q}'." : "Search completed.";
                }
            }

            return new CallToolResult
            {
                Content = string.IsNullOrEmpty(msg) ? null : [ new TextContentBlock { Type = "text", Text = msg } ],
                StructuredContent = node
            };
        }
        catch (McpException ex)
        {
            return new CallToolResult
            {
                Content = ex.Message is not null ? [ new TextContentBlock { Type = "text", Text = ex.Message + ex.StackTrace } ] : null,
                //StructuredContent = node
            };
        }
        catch (ArgumentException ex)
        {
            return new CallToolResult
            {
                Content = ex.Message is not null ? [ new TextContentBlock { Type = "text", Text = ex.Message + ex.StackTrace } ] : null,
                //StructuredContent = node
            };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                Content = ex.Message is not null ? [ new TextContentBlock { Type = "text", Text = ex.Message + ex.StackTrace } ] : null,
                //StructuredContent = node
            };
            
        }
    }
}