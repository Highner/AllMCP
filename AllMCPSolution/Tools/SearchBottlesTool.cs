using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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

        var trimmedQuery = query.Trim();
        if (trimmedQuery.Length == 0)
        {
            return new { success = false, error = "Search query cannot be empty" };
        }

        var tokens = TokenizeQuery(trimmedQuery);
        if (tokens.Count == 0)
        {
            return new { success = false, error = "Search query did not contain any searchable terms" };
        }

        var countryFilters = NormalizeFilterValues(ParameterHelpers.GetStringArrayParameter(parameters, "countries", "countries"), out _);
        var regionFilters = NormalizeFilterValues(ParameterHelpers.GetStringArrayParameter(parameters, "regions", "regions"), out var regionOriginalValues);
        var appellationFilters = NormalizeFilterValues(ParameterHelpers.GetStringArrayParameter(parameters, "appellations", "appellations"), out var appellationOriginalValues);
        var wineNameFilters = NormalizeFilterValues(ParameterHelpers.GetStringArrayParameter(parameters, "wineNames", "wine_names"), out _);
        var grapeVarietyFilters = NormalizeFilterValues(ParameterHelpers.GetStringArrayParameter(parameters, "grapeVarieties", "grape_varieties"), out _);
        var vintageFilters = NormalizeFilterValues(ParameterHelpers.GetStringArrayParameter(parameters, "vintages", "vintages"), out _);

        var normalizedQuery = string.Join(" ", tokens.Select(t => t.Normalized));
        var bottles = await _bottles.GetAllAsync(CancellationToken.None);
        var (knownRegionNames, knownRegionExamples) = BuildKnownNameCatalog(bottles.Select(b => b.WineVintage?.Wine?.Appellation?.Region?.Name));
        var (knownAppellationNames, knownAppellationExamples) = BuildKnownNameCatalog(bottles.Select(b => b.WineVintage?.Wine?.Appellation?.Name));

        ValidateRegionAppellationFilters(
            regionFilters,
            appellationFilters,
            knownRegionNames,
            knownAppellationNames,
            regionOriginalValues,
            appellationOriginalValues,
            knownRegionExamples,
            knownAppellationExamples);

        var structuredFilters = new StructuredFilters(countryFilters, regionFilters, appellationFilters, wineNameFilters, grapeVarietyFilters, vintageFilters);
        var filteredBottles = structuredFilters.HasFilters
            ? bottles.Where(bottle => MatchesStructuredFilters(bottle, structuredFilters)).ToList()
            : bottles;

        var candidates = filteredBottles
            .Select(bottle => EvaluateBottle(bottle, tokens, threshold))
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
                tastingNotePreview = candidate.TastingNotePreview,
                tokenMatches = candidate.TokenMatches
                    .Where(tm => tm.HasMatches)
                    .Select(tm => new
                    {
                        token = tm.Token.Original,
                        normalizedToken = tm.Token.Normalized,
                        fields = tm.FieldMatches
                            .Select(f => new { field = f.Field, matchType = f.MatchType })
                            .ToList(),
                        tastingNotes = tm.TastingNoteSnippets
                            .Select(sn => new
                            {
                                noteId = sn.NoteId,
                                matchType = sn.MatchType,
                                snippet = sn.Snippet
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .ToList();

        return new
        {
            success = true,
            query = trimmedQuery,
            normalizedQuery,
            tokens = tokens
                .Select(t => new
                {
                    original = t.Original,
                    normalized = t.Normalized
                })
                .ToList(),
            totalMatches = candidates.Count,
            count = limited.Count,
            limit,
            results = limited
        };
    }

    private static BottleCandidate EvaluateBottle(Bottle bottle, IReadOnlyList<QueryToken> tokens, int threshold)
    {
        var matchedFieldSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedFieldsList = new List<string>();
        var tokenMatches = tokens
            .Select(token => new TokenMatch(token))
            .ToDictionary(match => match.Token, match => match);
        var containsMatch = false;
        var hasFuzzyMatch = false;
        var bestDistance = int.MaxValue;
        var queryLength = Math.Max(1, tokens.Sum(t => t.Normalized.Length));
        var bestFieldLength = queryLength;

        void UpdateBest(int distance, int candidateLength)
        {
            if (distance < bestDistance || (distance == bestDistance && candidateLength < bestFieldLength))
            {
                bestDistance = distance;
                bestFieldLength = Math.Max(1, candidateLength);
            }
        }

        void RegisterMatch(QueryToken token, string fieldName, string matchType, TastingNote? note, bool attachSnippet)
        {
            if (matchType == MatchTypes.Substring)
            {
                containsMatch = true;
            }
            else
            {
                hasFuzzyMatch = true;
            }

            if (matchedFieldSet.Add(fieldName))
            {
                matchedFieldsList.Add(fieldName);
            }

            var tokenMatch = tokenMatches[token];
            if (!tokenMatch.FieldMatches.Any(f => f.Field.Equals(fieldName, StringComparison.OrdinalIgnoreCase) && f.MatchType == matchType))
            {
                tokenMatch.FieldMatches.Add(new FieldMatch
                {
                    Field = fieldName,
                    MatchType = matchType
                });
            }

            if (attachSnippet && note is not null)
            {
                var snippet = CreateTastingNoteSnippet(note.Note, token.Original, out _);
                if (!string.IsNullOrWhiteSpace(snippet)
                    && tokenMatch.TastingNoteSnippets.All(sn => sn.NoteId != note.Id || !string.Equals(sn.Snippet, snippet, StringComparison.Ordinal)))
                {
                    tokenMatch.TastingNoteSnippets.Add(new TastingNoteSnippet(note.Id, snippet, matchType));
                }
            }
        }

        void EvaluateField(string fieldName, string? value)
        {
            EvaluateFieldInternal(fieldName, value, null, false);
        }

        void EvaluateFieldInternal(string fieldName, string? value, TastingNote? note, bool attachSnippet)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var normalizedValue = NormalizeForComparison(value);
            if (normalizedValue.Length == 0)
            {
                return;
            }

            foreach (var token in tokens)
            {
                var candidateLength = normalizedValue.Length;
                if (normalizedValue.Contains(token.Normalized, StringComparison.Ordinal))
                {
                    RegisterMatch(token, fieldName, MatchTypes.Substring, note, attachSnippet);
                    UpdateBest(0, candidateLength);
                    continue;
                }

                var distance = CalculateLevenshteinDistance(token.Normalized, normalizedValue);
                UpdateBest(distance, candidateLength);
                if (distance <= threshold)
                {
                    RegisterMatch(token, fieldName, MatchTypes.Fuzzy, note, attachSnippet);
                }
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
                EvaluateFieldInternal("tastingNote", note.Note, note, attachSnippet: true);

                if (note.Score.HasValue)
                {
                    var formattedScore = note.Score.Value.ToString("0.##", CultureInfo.InvariantCulture);
                    EvaluateFieldInternal("tastingNoteScore", formattedScore, note, attachSnippet: false);
                    EvaluateFieldInternal("tastingNoteScoreKeywords", "score pts points rating", note, attachSnippet: false);
                }
            }
        }

        if (bestDistance == int.MaxValue)
        {
            bestDistance = queryLength;
            bestFieldLength = queryLength;
        }

        var include = containsMatch || hasFuzzyMatch || bestDistance <= threshold;
        var relevance = CalculateRelevanceScore(bestDistance, queryLength, bestFieldLength, containsMatch);
        var orderedMatches = tokens.Select(token => tokenMatches[token]).ToList();

        return new BottleCandidate
        {
            Bottle = bottle,
            Include = include,
            ContainsMatch = containsMatch,
            MatchedFields = matchedFieldsList,
            BestDistance = bestDistance,
            RelevanceScore = relevance,
            TastingNotePreview = BuildTastingNotePreview(orderedMatches),
            TokenMatches = orderedMatches
        };
    }

    private static bool MatchesStructuredFilters(Bottle bottle, StructuredFilters filters)
    {
        if (!filters.HasFilters)
        {
            return true;
        }

        var wineVintage = bottle.WineVintage;
        var wine = wineVintage?.Wine;

        if (filters.Countries.Count > 0)
        {
            var candidate = NormalizeFieldValue(wine?.Appellation?.Region?.Country?.Name);
            if (candidate is null || !filters.Countries.Contains(candidate))
            {
                return false;
            }
        }

        if (filters.Regions.Count > 0)
        {
            var candidate = NormalizeFieldValue(wine?.Appellation?.Region?.Name);
            if (candidate is null || !filters.Regions.Contains(candidate))
            {
                return false;
            }
        }

        if (filters.Appellations.Count > 0)
        {
            var candidate = NormalizeFieldValue(wine?.Appellation?.Name);
            if (candidate is null || !filters.Appellations.Contains(candidate))
            {
                return false;
            }
        }

        if (filters.WineNames.Count > 0)
        {
            var candidate = NormalizeFieldValue(wine?.Name);
            if (candidate is null || !filters.WineNames.Contains(candidate))
            {
                return false;
            }
        }

        if (filters.GrapeVarieties.Count > 0)
        {
            var candidate = NormalizeFieldValue(wine?.GrapeVariety);
            if (candidate is null || !filters.GrapeVarieties.Contains(candidate))
            {
                return false;
            }
        }

        if (filters.Vintages.Count > 0)
        {
            var candidate = wineVintage?.Vintage;
            var normalized = candidate.HasValue
                ? NormalizeFieldValue(candidate.Value.ToString(CultureInfo.InvariantCulture))
                : null;
            if (normalized is null || !filters.Vintages.Contains(normalized))
            {
                return false;
            }
        }

        return true;
    }

    private static HashSet<string> NormalizeFilterValues(IReadOnlyList<string>? values, out Dictionary<string, string> normalizedToOriginal)
    {
        var normalized = new HashSet<string>(StringComparer.Ordinal);
        normalizedToOriginal = new Dictionary<string, string>(StringComparer.Ordinal);
        if (values is null)
        {
            return normalized;
        }

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            var normalizedValue = NormalizeForComparison(trimmed);
            if (normalizedValue.Length > 0)
            {
                normalized.Add(normalizedValue);
                if (!normalizedToOriginal.ContainsKey(normalizedValue))
                {
                    normalizedToOriginal[normalizedValue] = trimmed;
                }
            }
        }

        return normalized;
    }

    private static string? NormalizeFieldValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = NormalizeForComparison(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private static (HashSet<string> Normalized, List<string> DisplayNames) BuildKnownNameCatalog(IEnumerable<string?> names)
    {
        var normalized = new HashSet<string>(StringComparer.Ordinal);
        var displaySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var displayNames = new List<string>();

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var trimmed = name.Trim();
            if (displaySet.Add(trimmed))
            {
                displayNames.Add(trimmed);
            }

            var normalizedName = NormalizeForComparison(trimmed);
            if (normalizedName.Length > 0)
            {
                normalized.Add(normalizedName);
            }
        }

        displayNames.Sort(StringComparer.OrdinalIgnoreCase);
        return (normalized, displayNames);
    }

    private static void ValidateRegionAppellationFilters(
        HashSet<string> regionFilters,
        HashSet<string> appellationFilters,
        HashSet<string> knownRegionNames,
        HashSet<string> knownAppellationNames,
        IReadOnlyDictionary<string, string> regionOriginalValues,
        IReadOnlyDictionary<string, string> appellationOriginalValues,
        IReadOnlyList<string> knownRegionExamples,
        IReadOnlyList<string> knownAppellationExamples)
    {
        if (regionFilters.Count == 0 && appellationFilters.Count == 0)
        {
            return;
        }

        var incorrectRegions = new List<string>();
        if (knownAppellationNames.Count > 0)
        {
            foreach (var filter in regionFilters)
            {
                var looksLikeAppellation = knownAppellationNames.Contains(filter);
                var alsoKnownRegion = knownRegionNames.Count > 0 && knownRegionNames.Contains(filter);
                if (looksLikeAppellation && !alsoKnownRegion)
                {
                    incorrectRegions.Add(regionOriginalValues.TryGetValue(filter, out var original) ? original : filter);
                }
            }
        }

        var incorrectAppellations = new List<string>();
        if (knownRegionNames.Count > 0)
        {
            foreach (var filter in appellationFilters)
            {
                var looksLikeRegion = knownRegionNames.Contains(filter);
                var alsoKnownAppellation = knownAppellationNames.Count > 0 && knownAppellationNames.Contains(filter);
                if (looksLikeRegion && !alsoKnownAppellation)
                {
                    incorrectAppellations.Add(appellationOriginalValues.TryGetValue(filter, out var original) ? original : filter);
                }
            }
        }

        if (incorrectRegions.Count == 0 && incorrectAppellations.Count == 0)
        {
            return;
        }

        var message = new StringBuilder();
        message.Append("Some location filters look like they belong to the wrong hierarchy level. ");

        if (incorrectRegions.Count > 0)
        {
            message.Append("Move these values from 'regions' to 'appellations': ");
            message.Append(string.Join(", ", incorrectRegions));
            message.Append(". ");
        }

        if (incorrectAppellations.Count > 0)
        {
            message.Append("Move these values from 'appellations' to 'regions': ");
            message.Append(string.Join(", ", incorrectAppellations));
            message.Append(". ");
        }

        if (knownRegionExamples.Count > 0)
        {
            var regionSample = string.Join(", ", knownRegionExamples.Take(5));
            if (knownRegionExamples.Count > 5)
            {
                regionSample += ", …";
            }

            message.Append("Regions cover broad areas such as ");
            message.Append(regionSample);
            message.Append(". ");
        }
        else
        {
            message.Append("Regions cover broad areas such as Bordeaux, Burgundy, or Napa Valley. ");
        }

        if (knownAppellationExamples.Count > 0)
        {
            var appellationSample = string.Join(", ", knownAppellationExamples.Take(5));
            if (knownAppellationExamples.Count > 5)
            {
                appellationSample += ", …";
            }

            message.Append("Appellations are specific subzones such as ");
            message.Append(appellationSample);
            message.Append(". ");
        }
        else
        {
            message.Append("Appellations are specific subzones such as Pomerol, Côte de Beaune, or Pauillac. ");
        }

        message.Append("Update the request to follow that hierarchy and try again.");

        throw new McpException(message.ToString(), McpErrorCode.InvalidArguments);
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

    private static string? BuildTastingNotePreview(IEnumerable<TokenMatch> tokenMatches)
    {
        foreach (var match in tokenMatches)
        {
            foreach (var snippet in match.TastingNoteSnippets)
            {
                if (!string.IsNullOrWhiteSpace(snippet.Snippet))
                {
                    return snippet.Snippet;
                }
            }
        }

        return null;
    }

    private static string? CreateTastingNoteSnippet(string tastingNote, string token, out bool containsToken)
    {
        var trimmed = tastingNote.Trim();
        if (trimmed.Length == 0)
        {
            containsToken = false;
            return null;
        }

        var compareInfo = CultureInfo.InvariantCulture.CompareInfo;
        var index = compareInfo.IndexOf(trimmed, token, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
        if (index < 0)
        {
            containsToken = false;
            return trimmed.Length <= 160 ? trimmed : trimmed.Substring(0, 160) + "…";
        }

        containsToken = true;
        var start = Math.Max(0, index - 40);
        var end = Math.Min(trimmed.Length, index + token.Length + 40);
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

    private static string NormalizeForComparison(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC).Trim();
    }

    private static List<QueryToken> TokenizeQuery(string query)
    {
        var tokens = new List<QueryToken>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in TokenSeparatorRegex.Split(query))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var normalized = NormalizeForComparison(trimmed);
            if (normalized.Length == 0 || !seen.Add(normalized))
            {
                continue;
            }

            tokens.Add(new QueryToken(trimmed, normalized));
        }

        return tokens;
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

    private static readonly Regex TokenSeparatorRegex = new("[,;\\s]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
                    },
                    countries = new
                    {
                        type = "array",
                        description = "Optional list of countries. Matches if the bottle country equals any value (case-insensitive).",
                        items = new { type = "string" }
                    },
                    regions = new
                    {
                        type = "array",
                        description = "Optional list of regions. Regions are broad winegrowing areas (e.g., Bordeaux, Burgundy, Napa Valley, Rioja). Use this for higher-level geography rather than specific appellations.",
                        items = new { type = "string" }
                    },
                    appellations = new
                    {
                        type = "array",
                        description = "Optional list of appellations. Appellations are sub-regions within a region (e.g., Pomerol, Côte de Beaune, Pauillac, Barolo). Use this for commune/cru-level filters.",
                        items = new { type = "string" }
                    },
                    wineNames = new
                    {
                        type = "array",
                        description = "Optional list of wine names. Matches if the bottle wine name equals any value (case-insensitive).",
                        items = new { type = "string" }
                    },
                    grapeVarieties = new
                    {
                        type = "array",
                        description = "Optional list of grape varieties. Matches if the bottle grape variety equals any value (case-insensitive).",
                        items = new { type = "string" }
                    },
                    vintages = new
                    {
                        type = "array",
                        description = "Optional list of vintages. Matches if the bottle vintage equals any value (case-insensitive).",
                        items = new { type = "string" }
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
                },
                new Dictionary<string, object>
                {
                    ["name"] = "countries",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "array",
                        ["items"] = new Dictionary<string, object>
                        {
                            ["type"] = "string"
                        }
                    },
                    ["style"] = "form",
                    ["explode"] = true,
                    ["description"] = "Optional list of countries. Matches if the bottle country equals any value."
                },
                new Dictionary<string, object>
                {
                    ["name"] = "regions",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "array",
                        ["items"] = new Dictionary<string, object>
                        {
                            ["type"] = "string"
                        }
                    },
                    ["style"] = "form",
                    ["explode"] = true,
                    ["description"] = "Optional list of regions. Regions are broad winegrowing areas (e.g., Bordeaux, Burgundy, Napa Valley, Rioja) and should be used for higher-level geography."
                },
                new Dictionary<string, object>
                {
                    ["name"] = "appellations",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "array",
                        ["items"] = new Dictionary<string, object>
                        {
                            ["type"] = "string"
                        }
                    },
                    ["style"] = "form",
                    ["explode"] = true,
                    ["description"] = "Optional list of appellations. Appellations are specific sub-regions within a region (e.g., Pomerol, Côte de Beaune, Pauillac, Barolo) and should contain the commune/cru-level filters."
                },
                new Dictionary<string, object>
                {
                    ["name"] = "wineNames",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "array",
                        ["items"] = new Dictionary<string, object>
                        {
                            ["type"] = "string"
                        }
                    },
                    ["style"] = "form",
                    ["explode"] = true,
                    ["description"] = "Optional list of wine names. Matches if the bottle wine name equals any value."
                },
                new Dictionary<string, object>
                {
                    ["name"] = "grapeVarieties",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "array",
                        ["items"] = new Dictionary<string, object>
                        {
                            ["type"] = "string"
                        }
                    },
                    ["style"] = "form",
                    ["explode"] = true,
                    ["description"] = "Optional list of grape varieties. Matches if the bottle grape variety equals any value."
                },
                new Dictionary<string, object>
                {
                    ["name"] = "vintages",
                    ["in"] = "query",
                    ["required"] = false,
                    ["schema"] = new Dictionary<string, object>
                    {
                        ["type"] = "array",
                        ["items"] = new Dictionary<string, object>
                        {
                            ["type"] = "string"
                        }
                    },
                    ["style"] = "form",
                    ["explode"] = true,
                    ["description"] = "Optional list of vintages. Matches if the bottle vintage equals any value."
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
                                    ["normalizedQuery"] = new Dictionary<string, object> { ["type"] = "string" },
                                    ["tokens"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "array",
                                        ["items"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "object",
                                            ["properties"] = new Dictionary<string, object>
                                            {
                                                ["original"] = new Dictionary<string, object> { ["type"] = "string" },
                                                ["normalized"] = new Dictionary<string, object> { ["type"] = "string" }
                                            }
                                        }
                                    },
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
                                                ["tastingNotePreview"] = new Dictionary<string, object> { ["type"] = "string", ["nullable"] = true },
                                                ["tokenMatches"] = new Dictionary<string, object>
                                                {
                                                    ["type"] = "array",
                                                    ["items"] = new Dictionary<string, object>
                                                    {
                                                        ["type"] = "object",
                                                        ["properties"] = new Dictionary<string, object>
                                                        {
                                                            ["token"] = new Dictionary<string, object> { ["type"] = "string" },
                                                            ["normalizedToken"] = new Dictionary<string, object> { ["type"] = "string" },
                                                            ["fields"] = new Dictionary<string, object>
                                                            {
                                                                ["type"] = "array",
                                                                ["items"] = new Dictionary<string, object>
                                                                {
                                                                    ["type"] = "object",
                                                                    ["properties"] = new Dictionary<string, object>
                                                                    {
                                                                        ["field"] = new Dictionary<string, object> { ["type"] = "string" },
                                                                        ["matchType"] = new Dictionary<string, object> { ["type"] = "string" }
                                                                    }
                                                                }
                                                            },
                                                            ["tastingNotes"] = new Dictionary<string, object>
                                                            {
                                                                ["type"] = "array",
                                                                ["items"] = new Dictionary<string, object>
                                                                {
                                                                    ["type"] = "object",
                                                                    ["properties"] = new Dictionary<string, object>
                                                                    {
                                                                        ["noteId"] = new Dictionary<string, object> { ["type"] = "string", ["format"] = "uuid" },
                                                                        ["matchType"] = new Dictionary<string, object> { ["type"] = "string" },
                                                                        ["snippet"] = new Dictionary<string, object> { ["type"] = "string" }
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
              },
              "countries": {
                "type": "array",
                "description": "Optional list of countries. Matches if the bottle country equals any value (case-insensitive).",
                "items": {
                  "type": "string"
                }
              },
              "regions": {
                "type": "array",
                "description": "Optional list of regions. Regions are broad winegrowing areas (e.g., Bordeaux, Burgundy, Napa Valley, Rioja) and should be used for higher-level geography rather than appellations.",
                "items": {
                  "type": "string"
                }
              },
              "appellations": {
                "type": "array",
                "description": "Optional list of appellations. Appellations are sub-regions within a region (e.g., Pomerol, Côte de Beaune, Pauillac, Barolo) and should contain the commune/cru-level values instead of regions.",
                "items": {
                  "type": "string"
                }
              },
              "wineNames": {
                "type": "array",
                "description": "Optional list of wine names. Matches if the bottle wine name equals any value (case-insensitive).",
                "items": {
                  "type": "string"
                }
              },
              "grapeVarieties": {
                "type": "array",
                "description": "Optional list of grape varieties. Matches if the bottle grape variety equals any value (case-insensitive).",
                "items": {
                  "type": "string"
                }
              },
              "vintages": {
                "type": "array",
                "description": "Optional list of vintages. Matches if the bottle vintage equals any value (case-insensitive).",
                "items": {
                  "type": "string"
                }
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

    private sealed class StructuredFilters
    {
        public StructuredFilters(
            HashSet<string> countries,
            HashSet<string> regions,
            HashSet<string> appellations,
            HashSet<string> wineNames,
            HashSet<string> grapeVarieties,
            HashSet<string> vintages)
        {
            Countries = countries;
            Regions = regions;
            Appellations = appellations;
            WineNames = wineNames;
            GrapeVarieties = grapeVarieties;
            Vintages = vintages;
            HasFilters = countries.Count > 0
                || regions.Count > 0
                || appellations.Count > 0
                || wineNames.Count > 0
                || grapeVarieties.Count > 0
                || vintages.Count > 0;
        }

        public bool HasFilters { get; }
        public HashSet<string> Countries { get; }
        public HashSet<string> Regions { get; }
        public HashSet<string> Appellations { get; }
        public HashSet<string> WineNames { get; }
        public HashSet<string> GrapeVarieties { get; }
        public HashSet<string> Vintages { get; }
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
        public required List<TokenMatch> TokenMatches { get; init; }
    }

    private sealed class QueryToken
    {
        public QueryToken(string original, string normalized)
        {
            Original = original;
            Normalized = normalized;
        }

        public string Original { get; }
        public string Normalized { get; }
    }

    private sealed class TokenMatch
    {
        public TokenMatch(QueryToken token)
        {
            Token = token;
        }

        public QueryToken Token { get; }
        public List<FieldMatch> FieldMatches { get; } = new();
        public List<TastingNoteSnippet> TastingNoteSnippets { get; } = new();
        public bool HasMatches => FieldMatches.Count > 0 || TastingNoteSnippets.Count > 0;
    }

    private sealed class FieldMatch
    {
        public required string Field { get; init; }
        public required string MatchType { get; init; }
    }

    private sealed class TastingNoteSnippet
    {
        public TastingNoteSnippet(Guid noteId, string snippet, string matchType)
        {
            NoteId = noteId;
            Snippet = snippet;
            MatchType = matchType;
        }

        public Guid NoteId { get; }
        public string Snippet { get; }
        public string MatchType { get; }
    }

    private static class MatchTypes
    {
        public const string Substring = "substring";
        public const string Fuzzy = "fuzzy";
    }
}
