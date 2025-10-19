using System.Collections;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Models;

namespace AllMCPSolution.Tools;

[McpTool("manage_wine_vintage_evolution_scores", "Adds, reads, removes, or replaces wine vintage evolution scores for a wine.")]
public sealed class ManageWineVintageEvolutionScoresTool : CrudToolBase
{
    private readonly IWineRepository _wineRepository;
    private readonly IWineVintageRepository _wineVintageRepository;
    private readonly IWineVintageEvolutionScoreRepository _scoreRepository;

    public ManageWineVintageEvolutionScoresTool(
        IWineRepository wineRepository,
        IWineVintageRepository wineVintageRepository,
        IWineVintageEvolutionScoreRepository scoreRepository)
    {
        _wineRepository = wineRepository;
        _wineVintageRepository = wineVintageRepository;
        _scoreRepository = scoreRepository;
    }

    public override string Name => "manage_wine_vintage_evolution_scores";
    public override string Description => "Adds, reads, removes, or replaces wine vintage evolution scores for a wine.";
    public override string Title => "Manage Wine Vintage Evolution Scores";
    protected override string InvokingMessage => "Managing wine vintage evolution scores…";
    protected override string InvokedMessage => "Wine vintage evolution score operation completed.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var operation = ParameterHelpers.GetStringParameter(parameters, "operation", "operation")?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(operation))
        {
            return Failure("validate", "'operation' is required.", new[] { "'operation' is required." });
        }

        var wineId = ParameterHelpers.GetGuidParameter(parameters, "wineId", "wine_id");
        var wineName = ParameterHelpers.GetStringParameter(parameters, "wineName", "wine_name")?.Trim();
        var appellation = ParameterHelpers.GetStringParameter(parameters, "appellation", "appellation")?.Trim();

        var wineResolution = await ResolveWineAsync(wineId, wineName, appellation, ct);
        if (!wineResolution.Success)
        {
            return wineResolution.Result!;
        }

        var wine = wineResolution.Wine!;

        return operation switch
        {
            "read" => await ReadScoresAsync(wine, parameters, ct),
            "add" => await AddScoresAsync(wine, parameters, ct),
            "remove" => await RemoveScoresAsync(wine, parameters, ct),
            "replace" => await ReplaceScoresAsync(wine, parameters, ct),
            _ => Failure("validate", $"Unsupported operation '{operation}'.", new[] { "Supported operations: read, add, remove, replace." })
        };
    }

    private async Task<CrudOperationResult> ReadScoresAsync(Wine wine, Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var results = await _scoreRepository.GetByWineIdAsync(wine.Id, ct);

        var filtered = results.ToList();
        var filterVintage = ParameterHelpers.GetIntParameter(parameters, "vintage", "vintage");
        var filterYear = ParameterHelpers.GetIntParameter(parameters, "year", "year");

        if (filterVintage.HasValue)
        {
            filtered = filtered
                .Where(s => s.WineVintage?.Vintage == filterVintage.Value)
                .ToList();
        }

        if (filterYear.HasValue)
        {
            filtered = filtered
                .Where(s => s.Year == filterYear.Value)
                .ToList();
        }

        return Success("read", $"Retrieved {filtered.Count} evolution score(s) for wine '{wine.Name}'.", BuildResponse(wine, filtered));
    }

    private async Task<CrudOperationResult> AddScoresAsync(Wine wine, Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var (provided, payloads, errors) = ParseScorePayloads(parameters);
        if (!provided)
        {
            return Failure("validate", "'scores' is required for add operations.", new[] { "'scores' is required for add operations." });
        }

        if (errors.Count > 0)
        {
            return Failure("validate", "One or more score entries could not be parsed.", errors);
        }

        if (payloads.Count == 0)
        {
            return Failure("validate", "At least one score entry must be provided for add operations.", new[] { "Provide one or more score objects under 'scores'." });
        }

        var preparation = await PrepareScoreEntitiesAsync(wine, payloads, createMissingWineVintage: true, ct);
        if (preparation.Errors.Count > 0)
        {
            return Failure("validate", "One or more score entries are invalid.", preparation.Errors);
        }

        await _scoreRepository.UpsertRangeAsync(preparation.Scores, ct);
        var refreshed = await _scoreRepository.GetByWineIdAsync(wine.Id, ct);

        return Success("add", $"Recorded {preparation.Scores.Count} evolution score(s) for wine '{wine.Name}'.", BuildResponse(wine, refreshed));
    }

    private async Task<CrudOperationResult> RemoveScoresAsync(Wine wine, Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var (provided, payloads, errors) = ParseScorePayloads(parameters);
        if (!provided)
        {
            return Failure("validate", "'scores' is required for remove operations.", new[] { "'scores' is required for remove operations." });
        }

        if (errors.Count > 0)
        {
            return Failure("validate", "One or more score entries could not be parsed.", errors);
        }

        if (payloads.Count == 0)
        {
            return Failure("validate", "At least one score entry must be provided for remove operations.", new[] { "Provide one or more score objects under 'scores'." });
        }

        var removal = await PrepareRemovalKeysAsync(wine, payloads, ct);
        if (removal.Errors.Count > 0)
        {
            return Failure("validate", "One or more score entries are invalid.", removal.Errors);
        }

        await _scoreRepository.RemoveByPairsAsync(removal.Keys, ct);
        var refreshed = await _scoreRepository.GetByWineIdAsync(wine.Id, ct);

        return Success("remove", $"Removed {removal.Keys.Count} evolution score(s) from wine '{wine.Name}'.", BuildResponse(wine, refreshed));
    }

    private async Task<CrudOperationResult> ReplaceScoresAsync(Wine wine, Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var (provided, payloads, errors) = ParseScorePayloads(parameters);
        if (!provided)
        {
            return Failure("validate", "'scores' is required for replace operations.", new[] { "'scores' is required for replace operations." });
        }

        if (errors.Count > 0)
        {
            return Failure("validate", "One or more score entries could not be parsed.", errors);
        }

        var preparation = await PrepareScoreEntitiesAsync(wine, payloads, createMissingWineVintage: true, ct);
        if (preparation.Errors.Count > 0)
        {
            return Failure("validate", "One or more score entries are invalid.", preparation.Errors);
        }

        await _scoreRepository.ReplaceForWineAsync(wine.Id, preparation.Scores, ct);
        var refreshed = await _scoreRepository.GetByWineIdAsync(wine.Id, ct);

        var message = payloads.Count == 0
            ? $"Cleared all evolution scores for wine '{wine.Name}'."
            : $"Replaced evolution scores for wine '{wine.Name}' with {preparation.Scores.Count} entries.";

        return Success("replace", message, BuildResponse(wine, refreshed));
    }

    private async Task<(bool Success, Wine? Wine, CrudOperationResult? Result)> ResolveWineAsync(Guid? wineId, string? wineName, string? appellation, CancellationToken ct)
    {
        Wine? wine = null;
        if (wineId.HasValue)
        {
            wine = await _wineRepository.GetByIdAsync(wineId.Value, ct);
            if (wine is null)
            {
                return (false, null, Failure("find_wine", $"Wine with id '{wineId}' was not found.", new[] { $"Wine with id '{wineId}' was not found." }));
            }
        }
        else if (!string.IsNullOrWhiteSpace(wineName))
        {
            wine = await _wineRepository.FindByNameAsync(wineName, appellation, ct);
            if (wine is null)
            {
                var suggestions = await _wineRepository.FindClosestMatchesAsync(wineName, 5, ct);
                return (false, null, Failure(
                    "find_wine",
                    $"Wine '{wineName}' was not found.",
                    new[] { $"Wine '{wineName}' was not found." },
                    suggestions: suggestions.Select(BottleResponseMapper.MapWineSummary).ToList()));
            }
        }
        else
        {
            return (false, null, Failure("validate", "Either 'wineId' or 'wineName' must be provided.", new[] { "Provide 'wineId' or 'wineName'." }));
        }

        return (true, wine, null);
    }

    private async Task<(List<WineVintageEvolutionScore> Scores, List<string> Errors)> PrepareScoreEntitiesAsync(Wine wine, IReadOnlyCollection<ScorePayload> payloads, bool createMissingWineVintage, CancellationToken ct)
    {
        var errors = new List<string>();
        var scores = new List<WineVintageEvolutionScore>();
        var seen = new HashSet<(Guid WineVintageId, int Year)>();

        foreach (var payload in payloads)
        {
            if (payload.Year is null)
            {
                errors.Add("Each score must include a 'year'.");
                continue;
            }

            if (payload.Score is null)
            {
                errors.Add("Each score must include a 'score'.");
                continue;
            }

            var vintageResolution = await ResolveWineVintageAsync(wine, payload, createMissingWineVintage, ct);
            if (vintageResolution.Error is not null)
            {
                errors.Add(vintageResolution.Error);
                continue;
            }

            var wineVintage = vintageResolution.WineVintage!;
            var key = (wineVintage.Id, payload.Year.Value);
            if (!seen.Add(key))
            {
                errors.Add($"Duplicate entry detected for vintage {wineVintage.Vintage} and year {payload.Year.Value}.");
                continue;
            }

            scores.Add(new WineVintageEvolutionScore
            {
                Id = payload.Id ?? Guid.Empty,
                WineVintageId = wineVintage.Id,
                WineVintage = wineVintage,
                Year = payload.Year.Value,
                Score = payload.Score.Value
            });
        }

        return (scores, errors);
    }

    private async Task<(List<(Guid WineVintageId, int Year)> Keys, List<string> Errors)> PrepareRemovalKeysAsync(Wine wine, IReadOnlyCollection<ScorePayload> payloads, CancellationToken ct)
    {
        var errors = new List<string>();
        var keys = new List<(Guid WineVintageId, int Year)>();
        var seen = new HashSet<(Guid WineVintageId, int Year)>();

        foreach (var payload in payloads)
        {
            if (payload.Year is null)
            {
                errors.Add("Each score must include a 'year'.");
                continue;
            }

            var vintageResolution = await ResolveWineVintageAsync(wine, payload, createIfMissing: false, ct);
            if (vintageResolution.Error is not null)
            {
                errors.Add(vintageResolution.Error);
                continue;
            }

            var key = (vintageResolution.WineVintage!.Id, payload.Year.Value);
            if (seen.Add(key))
            {
                keys.Add(key);
            }
        }

        return (keys, errors);
    }

    private async Task<(WineVintage? WineVintage, string? Error)> ResolveWineVintageAsync(Wine wine, ScorePayload payload, bool createIfMissing, CancellationToken ct)
    {
        if (payload.WineVintageId.HasValue)
        {
            var byId = await _wineVintageRepository.GetByIdAsync(payload.WineVintageId.Value, ct);
            if (byId is null)
            {
                return (null, $"Wine vintage with id '{payload.WineVintageId}' was not found.");
            }

            if (byId.WineId != wine.Id)
            {
                return (null, $"Wine vintage '{payload.WineVintageId}' does not belong to wine '{wine.Name}'.");
            }

            return (byId, null);
        }

        if (payload.Vintage.HasValue)
        {
            var vintage = payload.Vintage.Value;
            WineVintage? wineVintage = createIfMissing
                ? await _wineVintageRepository.GetOrCreateAsync(wine.Id, vintage, ct)
                : await _wineVintageRepository.FindByWineAndVintageAsync(wine.Id, vintage, ct);

            if (wineVintage is null)
            {
                return (null, $"Wine '{wine.Name}' does not have a vintage {vintage} recorded.");
            }

            return (wineVintage, null);
        }

        return (null, "Each score must include either 'wineVintageId' or 'vintage'.");
    }

    private static object BuildResponse(Wine wine, IReadOnlyCollection<WineVintageEvolutionScore> scores)
    {
        return new
        {
            wine = BottleResponseMapper.MapWineSummary(wine),
            scores = scores
                .OrderBy(s => s.WineVintage?.Vintage ?? int.MaxValue)
                .ThenBy(s => s.Year)
                .Select(MapScore)
                .ToList()
        };
    }

    private static object MapScore(WineVintageEvolutionScore score)
    {
        return new
        {
            id = score.Id,
            wineVintageId = score.WineVintageId,
            vintage = score.WineVintage?.Vintage,
            year = score.Year,
            score = score.Score
        };
    }

    private static (bool Provided, List<ScorePayload> Payloads, List<string> Errors) ParseScorePayloads(Dictionary<string, object>? parameters)
    {
        var errors = new List<string>();
        var payloads = new List<ScorePayload>();

        if (!TryGetParameter(parameters, "scores", "scores", out var raw))
        {
            return (false, payloads, errors);
        }

        try
        {
            payloads.AddRange(ConvertToPayloads(raw));
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to parse 'scores': {ex.Message}");
        }

        return (true, payloads, errors);
    }

    private static IReadOnlyList<ScorePayload> ConvertToPayloads(object? raw)
    {
        if (raw is null)
        {
            return Array.Empty<ScorePayload>();
        }

        if (raw is JsonElement element)
        {
            return ConvertFromJsonElement(element);
        }

        if (raw is string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<ScorePayload>();
            }

            using var document = JsonDocument.Parse(text);
            return ConvertFromJsonElement(document.RootElement);
        }

        if (raw is IDictionary dictionary)
        {
            var dict = new Dictionary<string, object?>();
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is string key)
                {
                    dict[key] = entry.Value;
                }
            }

            var element = JsonSerializer.SerializeToElement(dict);
            return ConvertFromJsonElement(element);
        }

        if (raw is IEnumerable enumerable)
        {
            var list = new List<ScorePayload>();
            foreach (var item in enumerable)
            {
                list.AddRange(ConvertToPayloads(item));
            }

            return list;
        }

        var single = JsonSerializer.SerializeToElement(raw);
        return ConvertFromJsonElement(single);
    }

    private static IReadOnlyList<ScorePayload> ConvertFromJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Array => element.EnumerateArray().SelectMany(ConvertFromJsonElement).ToList(),
            JsonValueKind.Object =>
                ConvertObject(element) is { } payload
                    ? new[] { payload }
                    : Array.Empty<ScorePayload>(),
            JsonValueKind.Null or JsonValueKind.Undefined => Array.Empty<ScorePayload>(),
            JsonValueKind.String =>
                string.IsNullOrWhiteSpace(element.GetString())
                    ? Array.Empty<ScorePayload>()
                    : ParseNestedString(element.GetString()!),
            _ => Array.Empty<ScorePayload>()
        };
    }

    private static IReadOnlyList<ScorePayload> ParseNestedString(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return ConvertFromJsonElement(document.RootElement);
        }
        catch
        {
            return Array.Empty<ScorePayload>();
        }
    }

    private static ScorePayload? ConvertObject(JsonElement element)
    {
        var id = TryGetGuid(element, "id");
        var wineVintageId = TryGetGuid(element, "wineVintageId") ?? TryGetGuid(element, "wine_vintage_id");
        var vintage = TryGetInt(element, "vintage");
        var year = TryGetInt(element, "year");
        var score = TryGetDecimal(element, "score");

        return new ScorePayload
        {
            Id = id,
            WineVintageId = wineVintageId,
            Vintage = vintage,
            Year = year,
            Score = score
        };
    }

    private static Guid? TryGetGuid(JsonElement element, string propertyName)
    {
        if (TryGetProperty(element, propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var guid))
            {
                return guid;
            }
        }

        return null;
    }

    private static int? TryGetInt(JsonElement element, string propertyName)
    {
        if (TryGetProperty(element, propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
            {
                return number;
            }
        }

        return null;
    }

    private static decimal? TryGetDecimal(JsonElement element, string propertyName)
    {
        if (TryGetProperty(element, propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out number))
            {
                return number;
            }
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetParameter(Dictionary<string, object>? parameters, string camelCase, string snakeCase, out object? value)
    {
        value = null;
        if (parameters is null)
        {
            return false;
        }

        if (parameters.TryGetValue(camelCase, out value))
        {
            return true;
        }

        if (parameters.TryGetValue(snakeCase, out value))
        {
            return true;
        }

        return false;
    }

    private sealed record ScorePayload
    {
        public Guid? Id { get; init; }
        public Guid? WineVintageId { get; init; }
        public int? Vintage { get; init; }
        public int? Year { get; init; }
        public decimal? Score { get; init; }
    }

    protected override JsonObject BuildInputSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["operation"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Operation to perform. Supported values: read, add, remove, replace.",
                    ["enum"] = new JsonArray("read", "add", "remove", "replace")
                },
                ["wineId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "uuid",
                    ["description"] = "Identifier of the wine. Provide either wineId or wineName."
                },
                ["wineName"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Name of the wine. Provide either wineId or wineName."
                },
                ["appellation"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Optional appellation name used to disambiguate wines when wineName is provided."
                },
                ["vintage"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "Optional vintage filter used with read operations."
                },
                ["year"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "Optional year filter used with read operations."
                },
                ["scores"] = new JsonObject
                {
                    ["type"] = "array",
                    ["description"] = "Array of score objects. Each object must include 'year' and either 'wineVintageId' or 'vintage'. 'score' is required for add/replace operations.",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["id"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["format"] = "uuid",
                                ["description"] = "Existing evolution score identifier (optional)."
                            },
                            ["wineVintageId"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["format"] = "uuid",
                                ["description"] = "Identifier of an existing wine vintage."
                            },
                            ["vintage"] = new JsonObject
                            {
                                ["type"] = "integer",
                                ["description"] = "Vintage year. Used when wineVintageId is not provided."
                            },
                            ["year"] = new JsonObject
                            {
                                ["type"] = "integer",
                                ["description"] = "Year the score applies to."
                            },
                            ["score"] = new JsonObject
                            {
                                ["type"] = "number",
                                ["description"] = "Score for the wine vintage in the specified year."
                            }
                        },
                        ["required"] = new JsonArray("year")
                    }
                }
            },
            ["required"] = new JsonArray("operation")
        };
    }
}
