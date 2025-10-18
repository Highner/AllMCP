using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Attributes;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;
using AllMCPSolution.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AllMCPSolution.Bottles;

[McpTool("search_bottles", "Searches bottles by wine, appellation, region, country, vintage, or tasting notes using fuzzy matching")]
public sealed class SearchBottlesTool : IToolBase, IMcpTool
{
    private readonly IBottleRepository _bottles;

    public SearchBottlesTool(IBottleRepository bottles)
    {
        _bottles = bottles;
    }

    public string Name => "search_bottles";
    public string Description => "Searches bottles by wine, appellation, region, country, vintage, or tasting notes using fuzzy matching.";
    public string? SafetyLevel => "non_critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        var query = ParameterHelpers.GetStringParameter(parameters, "query", "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return new { success = false, error = "Parameter 'query' is required" };
        }

        var threshold = ParameterHelpers.GetIntParameter(parameters, "threshold", "threshold") ?? 3;
        if (threshold < 0)
        {
            threshold = 0;
        }

        var limit = ParameterHelpers.GetIntParameter(parameters, "limit", "limit") ?? 25;
        if (limit <= 0)
        {
            limit = 25;
        }

        limit = Math.Min(limit, 100);

        var normalizedQuery = query.Trim();
        if (normalizedQuery.Length == 0)
        {
            return new { success = false, error = "Search query cannot be empty" };
        }

        var bottles = await _bottles.GetAllAsync(CancellationToken.None);
        var queryLower = normalizedQuery.ToLowerInvariant();

        var candidates = bottles
            .Select(bottle => EvaluateBottle(bottle, queryLower, normalizedQuery, threshold))
            .Where(candidate => candidate.Include)
            .OrderBy(candidate => candidate.ContainsMatch ? 0 : 1)
            .ThenBy(candidate => candidate.BestDistance)
            .ThenBy(candidate => candidate.Bottle.WineVintage?.Wine?.Name ?? string.Empty)
            .ThenBy(candidate => candidate.Bottle.WineVintage?.Vintage ?? int.MaxValue)
            .ToList();

        var limited = candidates
            .Take(limit)
            .Select(candidate => new
            {
                bottle = BottleResponseMapper.MapBottle(candidate.Bottle),
                matchedFields = candidate.MatchedFields,
                containsMatch = candidate.ContainsMatch,
                relevanceScore = candidate.RelevanceScore,
                bestDistance = candidate.BestDistance,
                tastingNotePreview = candidate.TastingNotePreview
            })
            .ToList();

        return new
        {
            success = true,
            query = normalizedQuery,
            totalMatches = candidates.Count,
            count = limited.Count,
            limit,
            results = limited
        };
    }

    private static BottleCandidate EvaluateBottle(Bottle bottle, string queryLower, string originalQuery, int threshold)
    {
        var matchedFields = new List<string>();
        var containsMatch = false;
        var bestDistance = int.MaxValue;
        var bestFieldLength = originalQuery.Length;

        void EvaluateField(string fieldName, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var normalizedValue = value.Trim();
            if (normalizedValue.Length == 0)
            {
                return;
            }

            if (normalizedValue.Contains(originalQuery, StringComparison.OrdinalIgnoreCase))
            {
                containsMatch = true;
                if (!matchedFields.Contains(fieldName))
                {
                    matchedFields.Add(fieldName);
                }
            }

            var candidateLower = normalizedValue.ToLowerInvariant();
            var distance = CalculateLevenshteinDistance(queryLower, candidateLower);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestFieldLength = normalizedValue.Length;
            }
        }

        var wineVintage = bottle.WineVintage;
        var wine = wineVintage?.Wine;
        EvaluateField("wine", wine?.Name);
        EvaluateField("grapeVariety", wine?.GrapeVariety);
        EvaluateField("appellation", wine?.Appellation?.Name);
        EvaluateField("region", wine?.Appellation?.Region?.Name);
        EvaluateField("country", wine?.Appellation?.Region?.Country?.Name);
        EvaluateField("vintage", wineVintage?.Vintage.ToString());
        if (bottle.TastingNotes is not null)
        {
            foreach (var note in bottle.TastingNotes)
            {
                EvaluateField("tastingNote", note.TastingNote);
            }
        }

        if (bestDistance == int.MaxValue)
        {
            bestDistance = originalQuery.Length;
        }

        var include = containsMatch || bestDistance <= threshold;
        var relevance = CalculateRelevanceScore(bestDistance, originalQuery.Length, bestFieldLength, containsMatch);

        return new BottleCandidate
        {
            Bottle = bottle,
            Include = include,
            ContainsMatch = containsMatch,
            MatchedFields = matchedFields,
            BestDistance = bestDistance,
            RelevanceScore = relevance,
            TastingNotePreview = BuildTastingNotePreview(bottle.TastingNotes, originalQuery)
        };
    }

    private static double CalculateRelevanceScore(int distance, int queryLength, int fieldLength, bool containsMatch)
    {
        if (containsMatch)
        {
            return 1.0;
        }

        if (distance <= 0)
        {
            return 1.0;
        }

        var maxLength = Math.Max(Math.Max(queryLength, fieldLength), 1);
        var score = 1.0 - (double)distance / maxLength;
        return Math.Max(0, Math.Min(1.0, score));
    }

    private static string? BuildTastingNotePreview(IEnumerable<TastingNote>? tastingNotes, string query)
    {
        if (tastingNotes is null)
        {
            return null;
        }

        string? fallback = null;

        foreach (var note in tastingNotes)
        {
            if (string.IsNullOrWhiteSpace(note.TastingNote))
            {
                continue;
            }

            var snippet = CreateTastingNoteSnippet(note.TastingNote, query, out var containsQuery);
            if (containsQuery && !string.IsNullOrWhiteSpace(snippet))
            {
                return snippet;
            }

            fallback ??= snippet;
        }

        return fallback;
    }

    private static string? CreateTastingNoteSnippet(string tastingNote, string query, out bool containsQuery)
    {
        var trimmed = tastingNote.Trim();
        if (trimmed.Length == 0)
        {
            containsQuery = false;
            return null;
        }

        var index = trimmed.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            containsQuery = false;
            return trimmed.Length <= 160 ? trimmed : trimmed.Substring(0, 160) + "…";
        }

        containsQuery = true;
        var start = Math.Max(0, index - 40);
        var end = Math.Min(trimmed.Length, index + query.Length + 40);
        var snippet = trimmed.Substring(start, end - start).Trim();
        if (start > 0)
        {
            snippet = "…" + snippet;
        }

        if (end < trimmed.Length)
        {
            snippet += "…";
        }

        return snippet;
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

        for (var i = 0; i <= sourceLength; i++)
        {
            distance[i, 0] = i;
        }

        for (var j = 0; j <= targetLength; j++)
        {
            distance[0, j] = j;
        }

        for (var i = 1; i <= sourceLength; i++)
        {
            for (var j = 1; j <= targetLength; j++)
            {
                var cost = target[j - 1] == source[i - 1] ? 0 : 1;

                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost);
            }
        }

        return distance[sourceLength, targetLength];
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
                        description = "Search text to match against wine names, regions, countries, vintages, or tasting notes"
                    },
                    threshold = new
                    {
                        type = "integer",
                        description = "Maximum edit distance for fuzzy matching (default: 3)",
                        minimum = 0,
                        @default = 3
                    },
                    limit = new
                    {
                        type = "integer",
                        description = "Maximum number of results to return (default: 25, max: 100)",
                        minimum = 1,
                        maximum = 100,
                        @default = 25
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
                    ["description"] = "Search text to match against wine names, regions, countries, vintages, or tasting notes"
                },
                new Dictionary<string, object>
                {
                    ["name"] = "threshold",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "integer",
                        ["default"] = 3,
                        ["minimum"] = 0
                    },
                    ["description"] = "Maximum edit distance for fuzzy matching"
                },
                new Dictionary<string, object>
                {
                    ["name"] = "limit",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "integer",
                        ["default"] = 25,
                        ["minimum"] = 1,
                        ["maximum"] = 100
                    },
                    ["description"] = "Maximum number of results to return"
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
                                    ["success"] = new Dictionary<string, object> { ["type"] = "boolean" },
                                    ["query"] = new Dictionary<string, object> { ["type"] = "string" },
                                    ["totalMatches"] = new Dictionary<string, object> { ["type"] = "integer" },
                                    ["count"] = new Dictionary<string, object> { ["type"] = "integer" },
                                    ["limit"] = new Dictionary<string, object> { ["type"] = "integer" },
                                    ["results"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "array",
                                        ["items"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "object",
                                            ["properties"] = new Dictionary<string, object>
                                            {
                                                ["bottle"] = new Dictionary<string, object> { ["type"] = "object" },
                                                ["matchedFields"] = new Dictionary<string, object>
                                                {
                                                    ["type"] = "array",
                                                    ["items"] = new Dictionary<string, object> { ["type"] = "string" }
                                                },
                                                ["containsMatch"] = new Dictionary<string, object> { ["type"] = "boolean" },
                                                ["relevanceScore"] = new Dictionary<string, object> { ["type"] = "number" },
                                                ["bestDistance"] = new Dictionary<string, object> { ["type"] = "integer" },
                                                ["tastingNotePreview"] = new Dictionary<string, object> { ["type"] = "string", ["nullable"] = true }
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

    public Tool GetDefinition() => new Tool
    {
        Name = Name,
        Title = "Search bottles",
        Description = Description,
        InputSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "Search text to match against wine names, regions, countries, vintages, or tasting notes"
            },
            "threshold": {
              "type": "integer",
              "description": "Maximum edit distance for fuzzy matching (default: 3)",
              "minimum": 0,
              "default": 3
            },
            "limit": {
              "type": "integer",
              "description": "Maximum number of results to return (default: 25, max: 100)",
              "minimum": 1,
              "maximum": 100,
              "default": 25
            }
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
                        System.Text.Json.JsonValueKind.Number => kvp.Value.TryGetInt32(out var i)
                            ? i
                            : kvp.Value.TryGetInt64(out var l)
                                ? l
                                : kvp.Value.TryGetDouble(out var d)
                                    ? d
                                    : null,
                        System.Text.Json.JsonValueKind.True => true,
                        System.Text.Json.JsonValueKind.False => false,
                        _ => null
                    };
                }
            }

            var result = await ExecuteAsync(dict);
            var node = JsonSerializer.SerializeToNode(result) as JsonObject;
            var message = "";
            if (node != null)
            {
                if (node.TryGetPropertyValue("error", out var err)
                    && err is JsonValue errValue
                    && errValue.TryGetValue<string>(out var errText))
                {
                    message = $"search_bottles: {errText}";
                }
                else
                {
                    var q = node.TryGetPropertyValue("query", out var qn)
                        && qn is JsonValue qv
                        && qv.TryGetValue<string>(out var qs)
                        ? qs
                        : null;
                    var total = node.TryGetPropertyValue("totalMatches", out var tn)
                        && tn is JsonValue tv
                        && tv.TryGetValue<int>(out var ti)
                        ? ti
                        : (int?)null;

                    message = q is not null && total.HasValue
                        ? $"Found {total} bottles for '{q}'."
                        : "Bottle search completed.";
                }
            }

            return new CallToolResult
            {
                Content = string.IsNullOrEmpty(message)
                    ? null
                    : [new TextContentBlock { Type = "text", Text = message }],
                StructuredContent = node
            };
        }
        catch (McpException ex)
        {
            return new CallToolResult
            {
                Content = ex.Message is not null
                    ? [new TextContentBlock { Type = "text", Text = ex.Message + ex.StackTrace }]
                    : null
            };
        }
        catch (ArgumentException ex)
        {
            return new CallToolResult
            {
                Content = ex.Message is not null
                    ? [new TextContentBlock { Type = "text", Text = ex.Message + ex.StackTrace }]
                    : null
            };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                Content = ex.Message is not null
                    ? [new TextContentBlock { Type = "text", Text = ex.Message + ex.StackTrace }]
                    : null
            };
        }
    }

    private sealed class BottleCandidate
    {
        public required Bottle Bottle { get; init; }
        public required bool Include { get; init; }
        public required bool ContainsMatch { get; init; }
        public required List<string> MatchedFields { get; init; }
        public required int BestDistance { get; init; }
        public required double RelevanceScore { get; init; }
        public string? TastingNotePreview { get; init; }
    }
}
