using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Repositories;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AllMCPSolution.Tools;

public abstract class BottleToolBase : IToolBase, IMcpTool
{
    private readonly IBottleRepository _bottleRepository;
    private readonly IWineRepository? _wineRepository;
    private readonly ICountryRepository? _countryRepository;
    private readonly IRegionRepository? _regionRepository;
    private readonly IAppellationRepository? _appellationRepository;

    protected BottleToolBase(
        IBottleRepository bottleRepository,
        IWineRepository? wineRepository = null,
        ICountryRepository? countryRepository = null,
        IRegionRepository? regionRepository = null,
        IAppellationRepository? appellationRepository = null)
    {
        _bottleRepository = bottleRepository;
        _wineRepository = wineRepository;
        _countryRepository = countryRepository;
        _regionRepository = regionRepository;
        _appellationRepository = appellationRepository;
    }

    protected IBottleRepository BottleRepository => _bottleRepository;
    protected IWineRepository WineRepository => _wineRepository ?? throw new InvalidOperationException("Wine repository is required for this tool but was not provided.");
    protected ICountryRepository CountryRepository => _countryRepository ?? throw new InvalidOperationException("Country repository is required for this tool but was not provided.");
    protected IRegionRepository RegionRepository => _regionRepository ?? throw new InvalidOperationException("Region repository is required for this tool but was not provided.");
    protected IAppellationRepository AppellationRepository => _appellationRepository ?? throw new InvalidOperationException("Appellation repository is required for this tool but was not provided.");

    public abstract string Name { get; }
    public abstract string Description { get; }
    public virtual string Title => Description;
    public virtual string? SafetyLevel => "non_critical";
    protected virtual string InvokingMessage => "Processing bottle operation…";
    protected virtual string InvokedMessage => "Bottle operation complete.";

    public async Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
        => await ExecuteInternalAsync(parameters, CancellationToken.None);

    public async ValueTask<CallToolResult> RunAsync(CallToolRequestParams request, CancellationToken ct)
    {
        var parameters = ConvertArgumentsToDictionary(request?.Arguments);
        var result = await ExecuteInternalAsync(parameters, ct);

        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Type = "text", Text = result.Message }
            ],
            StructuredContent = JsonSerializer.SerializeToNode(result) as JsonObject
        };
    }

    public Tool GetDefinition() => new()
    {
        Name = Name,
        Title = Title,
        Description = Description,
        InputSchema = JsonDocument.Parse(BuildInputSchema().ToJsonString()).RootElement,
        Meta = new JsonObject
        {
            ["openai/toolInvocation/invoking"] = InvokingMessage,
            ["openai/toolInvocation/invoked"] = InvokedMessage
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
                    description = "Bottle operation completed",
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

    protected abstract Task<BottleOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct);
    protected abstract JsonObject BuildInputSchema();

    protected Dictionary<string, object?>? ConvertArgumentsToDictionary(IReadOnlyDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null) return null;
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in arguments)
        {
            dict[kvp.Key] = kvp.Value;
        }

        return dict;
    }

    protected BottleOperationResult Success(string operation, string message, object? data = null)
        => BottleOperationResult.CreateSuccess(operation, message, data);

    protected BottleOperationResult Failure(
        string operation,
        string message,
        IReadOnlyList<string>? errors = null,
        object? suggestions = null,
        Exception? exception = null)
        => BottleOperationResult.CreateFailure(operation, message, errors, suggestions, exception);

    protected sealed record BottleOperationResult : IProcessingResult
    {
        public bool Success { get; init; }
        public string Operation { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public object? Data { get; init; }
        public IReadOnlyList<string>? Errors { get; init; }
        public object? Suggestions { get; init; }
        public string? ExceptionMessage { get; init; }
        public string? ExceptionStackTrace { get; init; }

        public static BottleOperationResult CreateSuccess(string operation, string message, object? data)
            => new() { Success = true, Operation = operation, Message = message, Data = data };

        public static BottleOperationResult CreateFailure(
            string operation,
            string message,
            IReadOnlyList<string>? errors = null,
            object? suggestions = null,
            Exception? exception = null)
            => new()
            {
                Success = false,
                Operation = operation,
                Message = message,
                Errors = errors,
                Suggestions = suggestions,
                ExceptionMessage = exception?.Message,
                ExceptionStackTrace = exception?.StackTrace
            };
    }
}
