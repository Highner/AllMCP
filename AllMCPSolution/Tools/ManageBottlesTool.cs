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
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AllMCPSolution.Tools;

[McpTool("manage_bottles", "Performs CRUD operations for bottles and surfaces available wine metadata options.")]
public sealed class ManageBottlesTool : IToolBase, IMcpTool
{
    private readonly IBottleRepository _bottleRepository;
    private readonly IWineVintageRepository _wineVintageRepository;
    private readonly ICountryRepository _countryRepository;
    private readonly IRegionRepository _regionRepository;

    private readonly Lazy<IReadOnlyList<OptionDescriptor>> _countryOptions;
    private readonly Lazy<IReadOnlyList<OptionDescriptor>> _regionOptions;
    private readonly Lazy<IReadOnlyList<WineVintageOptionDescriptor>> _wineVintageOptions;
    private readonly string[] _colorOptions = Enum.GetNames(typeof(WineColor));

    public ManageBottlesTool(
        IBottleRepository bottleRepository,
        IWineVintageRepository wineVintageRepository,
        ICountryRepository countryRepository,
        IRegionRepository regionRepository)
    {
        _bottleRepository = bottleRepository;
        _wineVintageRepository = wineVintageRepository;
        _countryRepository = countryRepository;
        _regionRepository = regionRepository;
        _countryOptions = new Lazy<IReadOnlyList<OptionDescriptor>>(LoadCountryOptions, LazyThreadSafetyMode.ExecutionAndPublication);
        _regionOptions = new Lazy<IReadOnlyList<OptionDescriptor>>(LoadRegionOptions, LazyThreadSafetyMode.ExecutionAndPublication);
        _wineVintageOptions = new Lazy<IReadOnlyList<WineVintageOptionDescriptor>>(LoadWineVintageOptions, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Name => "manage_bottles";
    public string Description => "Performs CRUD operations for bottles.";
    public string? SafetyLevel => "critical";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
        => await ExecuteInternalAsync(parameters, CancellationToken.None);

    public async ValueTask<CallToolResult> RunAsync(CallToolRequestParams request, CancellationToken ct)
    {
        var parameters = ConvertArgumentsToDictionary(request?.Arguments);
        var result = await ExecuteInternalAsync(parameters, ct);
        var message = result.Message;

        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Type = "text", Text = message }
            ],
            StructuredContent = JsonSerializer.SerializeToNode(result) as JsonObject
        };
    }

    public Tool GetDefinition() => new()
    {
        Name = Name,
        Title = "Manage Bottles",
        Description = Description,
        InputSchema = JsonDocument.Parse(BuildInputSchema().ToJsonString()).RootElement,
        Meta = new JsonObject
        {
            ["openai/toolInvocation/invoking"] = "Processing bottle operation…",
            ["openai/toolInvocation/invoked"] = "Bottle operation complete"
        }
    };

    public object GetToolDefinition() => new
    {
        name = Name,
        description = Description,
        safety = new { level = SafetyLevel },
        inputSchema = JsonSerializer.Deserialize<object>(BuildInputSchema().ToJsonString())
    }!;

    public object GetOpenApiSchema()
    {
        var schema = JsonSerializer.Deserialize<object>(BuildInputSchema().ToJsonString());
        return new
        {
            operationId = Name,
            summary = Description,
            description = Description,
            requestBody = new
            {
                required = true,
                content = new
                {
                    application__json = new
                    {
                        schema
                    }
                }
            },
            responses = new
            {
                _200 = new
                {
                    description = "Successful bottle operation",
                    content = new
                    {
                        application__json = new
                        {
                            schema = new { type = "object" }
                        }
                    }
                }
            }
        };
    }

    private async Task<OperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var operationRaw = ParameterHelpers.GetStringParameter(parameters, "operation", "operation");
        var operation = operationRaw?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(operation))
        {
            return OperationResult.Failure("Operation is required. Supported operations: list, get, create, update, delete.");
        }

        return operation switch
        {
            "list" => await ListAsync(ct),
            "get" => await GetAsync(parameters, ct),
            "create" => await CreateAsync(parameters, ct),
            "update" => await UpdateAsync(parameters, ct),
            "delete" => await DeleteAsync(parameters, ct),
            _ => OperationResult.Failure($"Unsupported operation '{operation}'. Valid options: list, get, create, update, delete.")
        };
    }

    private async Task<OperationResult> ListAsync(CancellationToken ct)
    {
        var bottles = await _bottleRepository.GetAllAsync(ct);
        var mapped = bottles.Select(MapBottle).ToList();
        return OperationResult.CreateSuccess("list", $"Retrieved {mapped.Count} bottles.", mapped);
    }

    private async Task<OperationResult> GetAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        if (id is null)
        {
            return OperationResult.Failure("'id' is required for the get operation.");
        }

        var bottle = await _bottleRepository.GetByIdAsync(id.Value, ct);
        if (bottle is null)
        {
            return OperationResult.Failure($"Bottle with id {id} was not found.");
        }

        return OperationResult.CreateSuccess("get", "Bottle retrieved successfully.", MapBottle(bottle));
    }

    private async Task<OperationResult> CreateAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var validationErrors = new List<string>();

        var wineVintageId = ParameterHelpers.GetGuidParameter(parameters, "wineVintageId", "wine_vintage_id");
        if (wineVintageId is null) validationErrors.Add("'wineVintageId' is required.");

        var price = ParameterHelpers.GetDecimalParameter(parameters, "price", "price");
        var score = ParameterHelpers.GetDecimalParameter(parameters, "score", "score");
        var tastingNote = ParameterHelpers.GetStringParameter(parameters, "tastingNote", "tasting_note")
                          ?? ParameterHelpers.GetStringParameter(parameters, "tastingNotes", "tasting_notes");

        if (validationErrors.Count > 0)
        {
            return OperationResult.Failure("Validation failed.", validationErrors);
        }

        var wineVintage = await _wineVintageRepository.GetByIdAsync(wineVintageId!.Value, ct);
        if (wineVintage is null)
        {
            return OperationResult.Failure($"Wine vintage with id {wineVintageId} was not found.");
        }

        var bottle = new Bottle
        {
            Id = Guid.NewGuid(),
            WineVintageId = wineVintage.Id,
            Price = price,
            Score = score,
            TastingNote = tastingNote ?? string.Empty
        };

        await _bottleRepository.AddAsync(bottle, ct);
        var created = await _bottleRepository.GetByIdAsync(bottle.Id, ct) ?? bottle;

        return OperationResult.CreateSuccess("create", "Bottle created successfully.", MapBottle(created));
    }

    private async Task<OperationResult> UpdateAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        if (id is null)
        {
            return OperationResult.Failure("'id' is required for update.");
        }

        var bottle = await _bottleRepository.GetByIdAsync(id.Value, ct);
        if (bottle is null)
        {
            return OperationResult.Failure($"Bottle with id {id} was not found.");
        }

        var wineVintageId = ParameterHelpers.GetGuidParameter(parameters, "wineVintageId", "wine_vintage_id");
        var price = ParameterHelpers.GetDecimalParameter(parameters, "price", "price");
        var score = ParameterHelpers.GetDecimalParameter(parameters, "score", "score");
        var tastingNote = ParameterHelpers.GetStringParameter(parameters, "tastingNote", "tasting_note")
                          ?? ParameterHelpers.GetStringParameter(parameters, "tastingNotes", "tasting_notes");

        if (wineVintageId is not null)
        {
            var wineVintage = await _wineVintageRepository.GetByIdAsync(wineVintageId.Value, ct);
            if (wineVintage is null)
            {
                return OperationResult.Failure($"Wine vintage with id {wineVintageId} was not found.");
            }

            bottle.WineVintageId = wineVintage.Id;
        }

        if (price is not null) bottle.Price = price;
        if (score is not null) bottle.Score = score;
        if (!string.IsNullOrWhiteSpace(tastingNote)) bottle.TastingNote = tastingNote!;

        await _bottleRepository.UpdateAsync(bottle, ct);
        var updated = await _bottleRepository.GetByIdAsync(bottle.Id, ct) ?? bottle;

        return OperationResult.CreateSuccess("update", "Bottle updated successfully.", MapBottle(updated));
    }

    private async Task<OperationResult> DeleteAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        if (id is null)
        {
            return OperationResult.Failure("'id' is required for delete.");
        }

        await _bottleRepository.DeleteAsync(id.Value, ct);
        return OperationResult.CreateSuccess("delete", $"Bottle {id} deleted if it existed.", null);
    }

    private JsonObject BuildInputSchema()
    {
        var countryOptions = _countryOptions.Value.Select(o => $"{o.Name} (Id: {o.Id})").ToArray();
        var regionOptions = _regionOptions.Value.Select(o => $"{o.Name} (Id: {o.Id})").ToArray();
        var wineVintageOptions = _wineVintageOptions.Value.Select(o => $"{o.DisplayName} (WineVintageId: {o.Id})").ToArray();

        var properties = new JsonObject
        {
            ["operation"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "CRUD operation to perform.",
                ["enum"] = new JsonArray("list", "get", "create", "update", "delete")
            },
            ["id"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "uuid",
                ["description"] = "Bottle identifier (required for get, update, delete)."
            },
            ["wineVintageId"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "uuid",
                ["description"] = "Wine vintage id to associate with the bottle. Available options: " + string.Join(", ", wineVintageOptions),
                ["options"] = new JsonArray(wineVintageOptions.Select(o => (JsonNode?)o).ToArray())
            },
            ["wine_vintage_id"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "uuid",
                ["description"] = "Wine vintage id (snake_case). Available options: " + string.Join(", ", wineVintageOptions),
                ["options"] = new JsonArray(wineVintageOptions.Select(o => (JsonNode?)o).ToArray())
            },
            ["price"] = new JsonObject
            {
                ["type"] = "number",
                ["description"] = "Price paid for the bottle (optional)."
            },
            ["score"] = new JsonObject
            {
                ["type"] = "number",
                ["description"] = "Score or rating for the bottle (optional)."
            },
            ["tastingNote"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Tasting note or comments (optional)."
            },
            ["tasting_note"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Tasting note or comments (snake_case alias)."
            },
            ["countryOptions"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "Available country options (read-only helper).",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["default"] = new JsonArray(countryOptions.Select(o => (JsonNode?)o).ToArray())
            },
            ["regionOptions"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "Available region options (read-only helper).",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["default"] = new JsonArray(regionOptions.Select(o => (JsonNode?)o).ToArray())
            },
            ["wineColorOptions"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "Available wine colors (read-only helper).",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["default"] = new JsonArray(_colorOptions.Select(o => (JsonNode?)o).ToArray())
            },
            ["wineVintageOptions"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "Available wine vintage options (read-only helper).",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["default"] = new JsonArray(wineVintageOptions.Select(o => (JsonNode?)o).ToArray())
            }
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray("operation")
        };
    }

    private Dictionary<string, object?>? ConvertArgumentsToDictionary(IReadOnlyDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null) return null;
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in arguments)
        {
            dict[kv.Key] = kv.Value;
        }

        return dict;
    }

    private IReadOnlyList<OptionDescriptor> LoadCountryOptions()
        => _countryRepository.GetAllAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .Select(c => new OptionDescriptor(c.Id, c.Name))
            .ToList();

    private IReadOnlyList<OptionDescriptor> LoadRegionOptions()
        => _regionRepository.GetAllAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .Select(r => new OptionDescriptor(r.Id, r.Name))
            .ToList();

    private IReadOnlyList<WineVintageOptionDescriptor> LoadWineVintageOptions()
        => _wineVintageRepository.GetAllAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .Select(wv => new WineVintageOptionDescriptor(
                wv.Id,
                $"{wv.Wine?.Name ?? "Unknown Wine"} {wv.Vintage} - {wv.Wine?.Color.ToString() ?? "Unknown Color"} ({wv.Wine?.Country?.Name ?? "Unknown Country"} / {wv.Wine?.Region?.Name ?? "Unknown Region"})"))
            .ToList();

    private static object MapBottle(Bottle bottle)
        => new
        {
            id = bottle.Id,
            price = bottle.Price,
            score = bottle.Score,
            tastingNote = bottle.TastingNote,
            wineVintage = bottle.WineVintage is null
                ? null
                : new
                {
                    id = bottle.WineVintage.Id,
                    vintage = bottle.WineVintage.Vintage,
                    wine = bottle.WineVintage.Wine is null
                        ? null
                        : new
                        {
                            id = bottle.WineVintage.Wine.Id,
                            name = bottle.WineVintage.Wine.Name,
                            grapeVariety = bottle.WineVintage.Wine.GrapeVariety,
                            color = bottle.WineVintage.Wine.Color.ToString(),
                            country = bottle.WineVintage.Wine.Country is null ? null : new { id = bottle.WineVintage.Wine.Country.Id, name = bottle.WineVintage.Wine.Country.Name },
                            region = bottle.WineVintage.Wine.Region is null ? null : new { id = bottle.WineVintage.Wine.Region.Id, name = bottle.WineVintage.Wine.Region.Name }
                        }
                },
            wine = bottle.WineVintage?.Wine is null
                ? null
                : new
                {
                    id = bottle.WineVintage.Wine.Id,
                    name = bottle.WineVintage.Wine.Name,
                    grapeVariety = bottle.WineVintage.Wine.GrapeVariety,
                    color = bottle.WineVintage.Wine.Color.ToString(),
                    country = bottle.WineVintage.Wine.Country is null ? null : new { id = bottle.WineVintage.Wine.Country.Id, name = bottle.WineVintage.Wine.Country.Name },
                    region = bottle.WineVintage.Wine.Region is null ? null : new { id = bottle.WineVintage.Wine.Region.Id, name = bottle.WineVintage.Wine.Region.Name }
                }
        };

    private sealed record OptionDescriptor(Guid Id, string Name);

    private sealed record WineVintageOptionDescriptor(Guid Id, string DisplayName);

    private sealed record OperationResult
    {
        public bool Success { get; init; }
        public string Operation { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public object? Data { get; init; }
        public IReadOnlyList<string>? Errors { get; init; }

        public static OperationResult CreateSuccess(string operation, string message, object? data)
            => new() { Success = true, Operation = operation, Message = message, Data = data };

        public static OperationResult Failure(string message, IReadOnlyList<string>? errors = null)
            => new() { Success = false, Operation = "error", Message = message, Errors = errors };
    }
}
