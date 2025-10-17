using System;
using System.Collections.Generic;
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

[McpTool("manage_wines", "Performs CRUD operations for wines, including listing, creating, updating, and deleting records.")]
public sealed class ManageWinesTool : IToolBase, IMcpTool
{
    private readonly IWineRepository _wineRepository;
    private readonly ICountryRepository _countryRepository;
    private readonly IRegionRepository _regionRepository;

    private readonly Lazy<IReadOnlyList<OptionDescriptor>> _countryOptions;
    private readonly Lazy<IReadOnlyList<OptionDescriptor>> _regionOptions;

    private readonly string[] _colorOptions = Enum.GetNames(typeof(WineColor));

    public ManageWinesTool(
        IWineRepository wineRepository,
        ICountryRepository countryRepository,
        IRegionRepository regionRepository)
    {
        _wineRepository = wineRepository;
        _countryRepository = countryRepository;
        _regionRepository = regionRepository;
        _countryOptions = new Lazy<IReadOnlyList<OptionDescriptor>>(LoadCountryOptions, LazyThreadSafetyMode.ExecutionAndPublication);
        _regionOptions = new Lazy<IReadOnlyList<OptionDescriptor>>(LoadRegionOptions, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Name => "manage_wines";
    public string Description => "Performs CRUD operations for wines.";
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
        Title = "Manage Wines",
        Description = Description,
        InputSchema = JsonDocument.Parse(BuildInputSchema().ToJsonString()).RootElement,
        Meta = new JsonObject
        {
            ["openai/toolInvocation/invoking"] = "Processing wine operation…",
            ["openai/toolInvocation/invoked"] = "Wine operation complete"
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
                    description = "Successful wine operation",
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

        switch (operation)
        {
            case "list":
                return await ListAsync(ct);
            case "get":
                return await GetAsync(parameters, ct);
            case "create":
                return await CreateAsync(parameters, ct);
            case "update":
                return await UpdateAsync(parameters, ct);
            case "delete":
                return await DeleteAsync(parameters, ct);
            default:
                return OperationResult.Failure($"Unsupported operation '{operation}'. Valid options: list, get, create, update, delete.");
        }
    }

    private async Task<OperationResult> ListAsync(CancellationToken ct)
    {
        var wines = await _wineRepository.GetAllAsync(ct);
        var mapped = wines.Select(MapWine).ToList();
        return OperationResult.CreateSuccess("list", $"Retrieved {mapped.Count} wines.", mapped);
    }

    private async Task<OperationResult> GetAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        if (id is null)
        {
            return OperationResult.Failure("'id' is required for the get operation.");
        }

        var wine = await _wineRepository.GetByIdAsync(id.Value, ct);
        if (wine is null)
        {
            return OperationResult.Failure($"Wine with id {id} was not found.");
        }

        return OperationResult.CreateSuccess("get", "Wine retrieved successfully.", MapWine(wine));
    }

    private async Task<OperationResult> CreateAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var validationErrors = new List<string>();

        var name = ParameterHelpers.GetStringParameter(parameters, "name", "name");
        if (string.IsNullOrWhiteSpace(name)) validationErrors.Add("'name' is required.");

        var grapeVariety = ParameterHelpers.GetStringParameter(parameters, "grapeVariety", "grape_variety");
        if (string.IsNullOrWhiteSpace(grapeVariety)) validationErrors.Add("'grapeVariety' is required.");

        var vintage = ParameterHelpers.GetIntParameter(parameters, "vintage", "vintage");
        if (vintage is null) validationErrors.Add("'vintage' is required and must be a valid year.");

        var colorRaw = ParameterHelpers.GetStringParameter(parameters, "color", "color");
        if (!TryParseWineColor(colorRaw, out var color)) validationErrors.Add("'color' is required. Valid options: " + string.Join(", ", _colorOptions) + ".");

        var countryId = ParameterHelpers.GetGuidParameter(parameters, "countryId", "country_id");
        if (countryId is null) validationErrors.Add("'countryId' is required.");

        var regionId = ParameterHelpers.GetGuidParameter(parameters, "regionId", "region_id");
        if (regionId is null) validationErrors.Add("'regionId' is required.");

        if (validationErrors.Count > 0)
        {
            return OperationResult.Failure("Validation failed.", validationErrors);
        }

        var wine = new Wine
        {
            Id = Guid.NewGuid(),
            Name = name!,
            GrapeVariety = grapeVariety!,
            Vintage = vintage!.Value,
            Color = color,
            CountryId = countryId!.Value,
            RegionId = regionId!.Value
        };

        await _wineRepository.AddAsync(wine, ct);
        var created = await _wineRepository.GetByIdAsync(wine.Id, ct) ?? wine;

        return OperationResult.CreateSuccess("create", "Wine created successfully.", MapWine(created));
    }

    private async Task<OperationResult> UpdateAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        if (id is null)
        {
            return OperationResult.Failure("'id' is required for update.");
        }

        var wine = await _wineRepository.GetByIdAsync(id.Value, ct);
        if (wine is null)
        {
            return OperationResult.Failure($"Wine with id {id} was not found.");
        }

        var name = ParameterHelpers.GetStringParameter(parameters, "name", "name");
        var grapeVariety = ParameterHelpers.GetStringParameter(parameters, "grapeVariety", "grape_variety");
        var vintage = ParameterHelpers.GetIntParameter(parameters, "vintage", "vintage");
        var colorRaw = ParameterHelpers.GetStringParameter(parameters, "color", "color");
        var countryId = ParameterHelpers.GetGuidParameter(parameters, "countryId", "country_id");
        var regionId = ParameterHelpers.GetGuidParameter(parameters, "regionId", "region_id");

        if (!string.IsNullOrWhiteSpace(name)) wine.Name = name!;
        if (!string.IsNullOrWhiteSpace(grapeVariety)) wine.GrapeVariety = grapeVariety!;
        if (vintage is not null) wine.Vintage = vintage.Value;
        if (TryParseWineColor(colorRaw, out var parsedColor)) wine.Color = parsedColor;
        if (countryId is not null) wine.CountryId = countryId.Value;
        if (regionId is not null) wine.RegionId = regionId.Value;

        await _wineRepository.UpdateAsync(wine, ct);
        var updated = await _wineRepository.GetByIdAsync(wine.Id, ct) ?? wine;

        return OperationResult.CreateSuccess("update", "Wine updated successfully.", MapWine(updated));
    }

    private async Task<OperationResult> DeleteAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        if (id is null)
        {
            return OperationResult.Failure("'id' is required for delete.");
        }

        await _wineRepository.DeleteAsync(id.Value, ct);
        return OperationResult.CreateSuccess("delete", $"Wine {id} deleted if it existed.", null);
    }

    private JsonObject BuildInputSchema()
    {
        var countryOptions = _countryOptions.Value.Select(o => $"{o.Name} (Id: {o.Id})").ToArray();
        var regionOptions = _regionOptions.Value.Select(o => $"{o.Name} (Id: {o.Id})").ToArray();

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
                ["description"] = "Wine identifier (required for get, update, delete)."
            },
            ["name"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Wine name (required for create)."
            },
            ["grapeVariety"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Primary grape variety (required for create)."
            },
            ["grape_variety"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Primary grape variety (snake_case alias)."
            },
            ["vintage"] = new JsonObject
            {
                ["type"] = "integer",
                ["description"] = "Vintage year (required for create)."
            },
            ["color"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Wine color (required for create).",
                ["enum"] = new JsonArray(_colorOptions.Select(c => (JsonNode?)c).ToArray()),
                ["options"] = new JsonArray(_colorOptions.Select(c => (JsonNode?)c).ToArray())
            },
            ["countryId"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "uuid",
                ["description"] = "Country id linked to the wine. Available options: " + string.Join(", ", countryOptions),
                ["options"] = new JsonArray(countryOptions.Select(o => (JsonNode?)o).ToArray())
            },
            ["country_id"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "uuid",
                ["description"] = "Country id linked to the wine (snake_case). Available options: " + string.Join(", ", countryOptions),
                ["options"] = new JsonArray(countryOptions.Select(o => (JsonNode?)o).ToArray())
            },
            ["regionId"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "uuid",
                ["description"] = "Region id linked to the wine. Available options: " + string.Join(", ", regionOptions),
                ["options"] = new JsonArray(regionOptions.Select(o => (JsonNode?)o).ToArray())
            },
            ["region_id"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "uuid",
                ["description"] = "Region id linked to the wine (snake_case). Available options: " + string.Join(", ", regionOptions),
                ["options"] = new JsonArray(regionOptions.Select(o => (JsonNode?)o).ToArray())
            }
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray("operation")
        };
    }

    private Dictionary<string, object?>? ConvertArgumentsToDictionary(Dictionary<string, JsonElement>? arguments)
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

    private static bool TryParseWineColor(string? value, out WineColor color)
    {
        if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse<WineColor>(value, true, out var parsed))
        {
            color = parsed;
            return true;
        }

        color = default;
        return false;
    }

    private static object MapWine(Wine wine)
        => new
        {
            id = wine.Id,
            name = wine.Name,
            grapeVariety = wine.GrapeVariety,
            vintage = wine.Vintage,
            color = wine.Color.ToString(),
            country = wine.Country is null ? null : new { id = wine.Country.Id, name = wine.Country.Name },
            region = wine.Region is null ? null : new { id = wine.Region.Id, name = wine.Region.Name }
        };

    private sealed record OptionDescriptor(Guid Id, string Name);

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
