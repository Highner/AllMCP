using System;
using System.Collections;
using System.ClientModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;

namespace AllMCPSolution.Services;

public interface IChatGptService
{
    Task<ChatCompletion> GetChatCompletionAsync(
        IEnumerable<ChatMessage> messages,
        string? model = null,
        double? temperature = null,
        bool useWebSearch = false,
        CancellationToken ct = default);

    IAsyncEnumerable<string> StreamChatCompletionAsync(
        IEnumerable<ChatMessage> messages,
        string? model = null,
        double? temperature = null,
        bool useWebSearch = false,
        CancellationToken ct = default);

    Task<DrinkingWindowPrediction> GetDrinkingWindowAsync(
        string wineContext,
        CancellationToken ct = default);
}

public sealed class ChatGptService : IChatGptService
{
    private const string ResponsesBetaHeaderValue = "responses=v1";
    private static readonly Uri OpenAiBaseUri = new("https://api.openai.com/");
    private static readonly Uri ResponsesEndpoint = new("https://api.openai.com/v1/responses");
    private static readonly JsonSerializerOptions WorkflowSerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ChatClient? _chatClient;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ChatGptService> _logger;
    private readonly string _defaultModel;
    private readonly string _apiKey;
    private readonly string? _drinkingWindowWorkflowId;
    private readonly bool _isConfigured;
    private readonly string? _drinkingWindowWorkflowKey;

    public ChatGptService(
        IConfiguration configuration,
        ILogger<ChatGptService> logger)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _httpClient = new HttpClient
        {
            BaseAddress = OpenAiBaseUri
        };

        var options = configuration
            .GetSection(ChatGptOptions.ConfigurationSectionName)
            .Get<ChatGptOptions>();

        _defaultModel = string.IsNullOrWhiteSpace(options?.DefaultModel)
            ? ChatGptOptions.FallbackModel
            : options!.DefaultModel!;

        _drinkingWindowWorkflowId = options?.DrinkingWindowWorkflowId;
        _drinkingWindowWorkflowKey = options?.DrinkingWindowWorkflowKey;

        if (string.IsNullOrWhiteSpace(options?.ApiKey))
        {
            _isConfigured = false;
            _apiKey = string.Empty;
            _chatClient = null;
            _logger.LogWarning(
                "ChatGPT integration is disabled because the OpenAI API key is not configured.");
            return;
        }

        _isConfigured = true;
        _apiKey = options.ApiKey!;
        _chatClient = new ChatClient(_defaultModel, _apiKey);
        ConfigureHttpClient();
    }

    public async Task<ChatCompletion> GetChatCompletionAsync(
        IEnumerable<ChatMessage> messages,
        string? model = null,
        double? temperature = null,
        bool useWebSearch = false,
        CancellationToken ct = default)
    {
        EnsureConfigured();

        var materializedMessages = MaterializeMessages(messages);
        var completionOptions = CreateCompletionOptions(temperature, useWebSearch);
        var client = ResolveClient(model);

        try
        {
            var result = await client
                .CompleteChatAsync(materializedMessages, completionOptions, ct)
                .ConfigureAwait(false);

            // Log any tool calls returned in the response
            LogToolCallsFromCompletion(result);

            return result;
        }
        catch (ClientResultException ex)
        {
            _logger.LogError(
                ex,
                "ChatGPT API request failed with status {StatusCode}: {Message}",
                ex.Status,
                ex.Message);
            throw;
        }
    }

    public async IAsyncEnumerable<string> StreamChatCompletionAsync(
        IEnumerable<ChatMessage> messages,
        string? model = null,
        double? temperature = null,
        bool useWebSearch = false,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        EnsureConfigured();

        var materializedMessages = MaterializeMessages(messages);
        var completionOptions = CreateCompletionOptions(temperature, useWebSearch);
        var client = ResolveClient(model);

        AsyncCollectionResult<StreamingChatCompletionUpdate> response;
        try
        {
            response = client.CompleteChatStreamingAsync(materializedMessages, completionOptions, ct);
        }
        catch (ClientResultException ex)
        {
            _logger.LogError(
                ex,
                "ChatGPT streaming request failed with status {StatusCode}: {Message}",
                ex.Status,
                ex.Message);
            throw;
        }

        //try
        //{
            await foreach (var update in response.WithCancellation(ct).ConfigureAwait(false))
            {
                // Try to log any tool call updates present at the update level
                TryLogToolCallsFromUpdate(update);

                if (update?.ContentUpdate is not { Count: > 0 })
                {
                    continue;
                }

                foreach (var part in update.ContentUpdate)
                {
                    // Try to log tool call details from each content part if present
                    TryLogToolCallFromContentPart(part);

                    if (part.Kind == ChatMessageContentPartKind.Text && !string.IsNullOrEmpty(part.Text))
                    {
                        yield return part.Text!;
                    }
                }
            }
        //}
       // catch (ClientResultException ex)
       // {
       //     _logger.LogError(
        //        ex,
         //       "ChatGPT streaming enumeration failed with status {StatusCode}: {Message}",
         //       ex.Status,
         //       ex.Message);
         //   throw;
        //}
    }

    public async Task<DrinkingWindowPrediction> GetDrinkingWindowAsync(
        string wineContext,
        CancellationToken ct = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(wineContext))
        {
            throw new ArgumentException("A wine description is required.", nameof(wineContext));
        }

        if (string.IsNullOrWhiteSpace(_drinkingWindowWorkflowId))
        {
            throw new InvalidOperationException(
                "The drinking window workflow identifier is not configured.");
        }

        using var content = new StringContent(
            JsonSerializer.Serialize(CreateDrinkingWindowWorkflowPayload(wineContext), WorkflowSerializerOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient
            .PostAsync(ResponsesEndpoint, content, ct)
            .ConfigureAwait(false);

        var rawContent = await response
            .Content
            .ReadAsStringAsync(ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "OpenAI drinking window workflow request failed with status {StatusCode}: {Body}",
                (int)response.StatusCode,
                rawContent);

            throw new HttpRequestException(
                $"OpenAI drinking window workflow request failed with status code {(int)response.StatusCode}.");
        }

        try
        {
            using var document = JsonDocument.Parse(rawContent);
            if (TryFindDrinkingWindow(document.RootElement, out var window))
            {
                return window;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse the drinking window workflow response: {Body}", rawContent);
            throw;
        }

        _logger.LogError("The drinking window workflow response did not include the expected Start and End values. Body: {Body}", rawContent);
        throw new InvalidOperationException("The drinking window workflow response did not include Start and End values.");
    }

    private void LogToolCallsFromCompletion(ChatCompletion completion)
    {
        try
        {
            if (completion == null)
            {
                return;
            }

            // Prefer strongly-typed access if available on this SDK version
            try
            {
                if (completion.Content is { Count: > 0 })
                {
                    foreach (var part in completion.Content)
                    {
                        TryLogToolCallFromContentPart(part);
                    }
                }
            }
            catch
            {
                // Fallback to reflection if the above members differ in this SDK
                TryLogToolCallsFromObject(completion);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to log tool calls from ChatCompletion.");
        }
    }

    private void TryLogToolCallsFromUpdate(object? update)
    {
        try
        {
            if (update == null) return;
            TryLogToolCallsFromObject(update);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to inspect streaming update for tool calls.");
        }
    }

    private void TryLogToolCallFromContentPart(object? part)
    {
        if (part == null) return;

        // Strongly-typed path for known SDK shape
        try
        {
            var partType = part.GetType();
            var toolCallProp = partType.GetProperty("ToolCall");
            if (toolCallProp != null)
            {
                var toolCall = toolCallProp.GetValue(part);
                if (toolCall != null)
                {
                    LogToolCall(toolCall);
                    return;
                }
            }
        }
        catch
        {
            // ignore, fall back to generic approach
        }

        // Fallback: inspect object for any ToolCall/ToolCalls members
        TryLogToolCallsFromObject(part);
    }

    private void TryLogToolCallsFromObject(object obj)
    {
        if (obj == null) return;

        var type = obj.GetType();

        // Single ToolCall property
        var singleToolCall = type.GetProperty("ToolCall")?.GetValue(obj);
        if (singleToolCall != null)
        {
            LogToolCall(singleToolCall);
        }

        // Collection properties potentially containing tool calls
        foreach (var propName in new[] { "ToolCalls", "ToolCallUpdate", "ToolCallsUpdate", "ToolCallDelta", "ToolCallsDelta", "Content", "ContentUpdate", "Output", "Delta", "Choice", "Choices", "Item", "Items" })
        {
            var prop = type.GetProperty(propName);
            if (prop == null) continue;
            var value = prop.GetValue(obj);
            LogToolCallsFromUnknown(value);
        }
    }

    private void LogToolCallsFromUnknown(object? value)
    {
        if (value == null) return;

        if (value is IEnumerable enumerable && value is not string)
        {
            foreach (var item in enumerable)
            {
                if (item == null) continue;

                // Check if item itself is a tool call or contains one
                var itemType = item.GetType();
                if ((itemType.GetProperty("Type") != null && itemType.GetProperty("Input") != null)
                    || (itemType.GetProperty("Name") != null && (itemType.GetProperty("Arguments") != null || itemType.GetProperty("ArgumentsUpdate") != null))
                    || (itemType.GetProperty("FunctionName") != null))
                {
                    LogToolCall(item);
                }
                else
                {
                    // Recurse into nested structures
                    TryLogToolCallsFromObject(item);
                }
            }
        }
        else
        {
            TryLogToolCallsFromObject(value);
        }
    }

    private void LogToolCall(object toolCall)
    {
        try
        {
            var t = toolCall.GetType();

            // Common property candidates across SDK shapes
            var typeProp = t.GetProperty("Type");
            var nameProp = t.GetProperty("Name") ?? t.GetProperty("FunctionName");
            var idProp = t.GetProperty("Id") ?? t.GetProperty("CallId") ?? t.GetProperty("ToolCallId");
            var inputProp = t.GetProperty("Input");
            var argsProp = t.GetProperty("Arguments");
            var argsUpdateProp = t.GetProperty("ArgumentsUpdate");

            var typeValue = typeProp?.GetValue(toolCall)?.ToString() ?? "<unknown>";
            var nameValue = nameProp?.GetValue(toolCall)?.ToString();
            var idValue = idProp?.GetValue(toolCall)?.ToString();

            var inputValue = inputProp?.GetValue(toolCall);
            var argsValue = argsProp?.GetValue(toolCall);
            var argsUpdateValue = argsUpdateProp?.GetValue(toolCall);

            // Pick the most informative payload
            string payload = string.Empty;
            if (argsUpdateValue is not null)
            {
                payload = argsUpdateValue.ToString() ?? string.Empty; // partial chunk during streaming
            }
            else if (argsValue is not null)
            {
                payload = argsValue.ToString() ?? string.Empty;
            }
            else if (inputValue is not null)
            {
                payload = inputValue.ToString() ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(nameValue) || !string.IsNullOrWhiteSpace(idValue))
            {
                _logger.LogInformation("Tool call: {ToolType} Name={Name} Id={Id}\n{Payload}", typeValue, nameValue ?? string.Empty, idValue ?? string.Empty, payload);
            }
            else
            {
                _logger.LogInformation("Tool call: {ToolType}\n{Payload}", typeValue, payload);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to log a tool call instance.");
        }
    }

    private ChatCompletionOptions CreateCompletionOptions(double? temperature, bool useWebSearch)
    {
        var options = new ChatCompletionOptions
        {

        };

        // Suppress unused parameter warning for temperature in SDK versions without a Temperature option
        _ = temperature;

        // Try to add WebSearchTool if available in the referenced OpenAI SDK
        if (useWebSearch)
        {
            TryAddWebSearchTool(options);
        }

        return options;
    }

    private void TryAddWebSearchTool(ChatCompletionOptions options)
    {
        if (options is null)
        {
            return;
        }

        try
        {
            ChatTool searchTool = ChatTool.CreateFunctionTool("web_search_preview");
            options.Tools.Add(searchTool);
            
            /*var possibleTypeNames = new[]
            {
                "OpenAI.Chat.Tools.WebSearchTool, OpenAI",
                "OpenAI.Chat.WebSearchTool, OpenAI"
            };

            Type? toolType = null;
            foreach (var name in possibleTypeNames)
            {
                toolType = Type.GetType(name, throwOnError: false);
                if (toolType != null)
                {
                    break;
                }
            }

            if (toolType is null)
            {
                // Last resort: search loaded assemblies
                toolType = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Select(a => a.GetType("OpenAI.Chat.Tools.WebSearchTool") ?? a.GetType("OpenAI.Chat.WebSearchTool"))
                    .FirstOrDefault(t => t is not null);
            }

            if (toolType is null)
            {
                _logger.LogInformation("WebSearchTool type not found in current OpenAI SDK. Continuing without it.");
                return;
            }

            var instance = Activator.CreateInstance(toolType);

            if (instance is ChatTool tool)
            {
                options.Tools.Add(tool);
 
                options.ToolChoice = ChatToolChoice.CreateRequiredChoice();
            }
            else
            {
                _logger.LogInformation("WebSearchTool type does not inherit from ChatTool in this SDK version. Skipping.");
            }*/
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to attach WebSearchTool. Continuing without it.");
        }
    }

    private static ChatMessage[] MaterializeMessages(IEnumerable<ChatMessage> messages)
    {
        if (messages is null)
        {
            throw new ArgumentNullException(nameof(messages));
        }

        var materializedMessages = messages as ChatMessage[] ?? messages.ToArray();

        for (var index = 0; index < materializedMessages.Length; index++)
        {
            if (materializedMessages[index] is null)
            {
                throw new ArgumentException($"Message at index {index} cannot be null.", nameof(messages));
            }
        }

        if (materializedMessages.Length == 0)
        {
            throw new ArgumentException("At least one chat message is required.", nameof(messages));
        }

        return materializedMessages;
    }

    private ChatClient ResolveClient(string? model)
    {
        EnsureConfigured();

        if (!string.IsNullOrWhiteSpace(model) && !string.Equals(model, _defaultModel, StringComparison.Ordinal))
        {
            return new ChatClient(model!, _apiKey);
        }

        return _chatClient!;
    }

    private void EnsureConfigured()
    {
        if (_isConfigured)
        {
            return;
        }

        throw new ChatGptServiceNotConfiguredException();
    }

    private void ConfigureHttpClient()
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        if (!_httpClient.DefaultRequestHeaders.Contains("OpenAI-Beta"))
        {
            _httpClient.DefaultRequestHeaders.Add("OpenAI-Beta", ResponsesBetaHeaderValue);
        }

        if (!_httpClient.DefaultRequestHeaders.Accept.Any(h => string.Equals(h.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)))
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AllMCPSolution/1.0");
        }
    }

    private object CreateDrinkingWindowWorkflowPayload(string wineContext)
    {
        var workflow = new Dictionary<string, object?>
        {
            ["id"] = _drinkingWindowWorkflowId
        };

        if (!string.IsNullOrWhiteSpace(_drinkingWindowWorkflowKey))
        {
            workflow["key"] = _drinkingWindowWorkflowKey;
        }

        return new
        {
            model = _defaultModel,
            workflow,
            input = new[]
            {
                new
                {
                    role = "user",
                    content = new[]
                    {
                        new
                        {
                            type = "input_text",
                            text = wineContext
                        }
                    }
                }
            },
            text = new
            {
                format = "json_schema",
                json_schema = new
                {
                    name = "response_schema",
                    schema = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["Start"] = new
                            {
                                type = "integer",
                                description = "first year of the drinking window"
                            },
                            ["End"] = new
                            {
                                type = "integer",
                                description = "last year of the drinking window"
                            }
                        },
                        additionalProperties = false,
                        required = new[] { "Start", "End" },
                        title = "response_schema"
                    }
                }
            }
        };
    }

    private static bool TryFindDrinkingWindow(JsonElement element, out DrinkingWindowPrediction result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (TryExtractDrinkingWindow(element, out result))
            {
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (TryFindDrinkingWindow(property.Value, out result))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindDrinkingWindow(item, out result))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    using var nestedDocument = JsonDocument.Parse(text);
                    if (TryFindDrinkingWindow(nestedDocument.RootElement, out result))
                    {
                        return true;
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed JSON fragments and continue searching.
                }
            }
        }

        result = default!;
        return false;
    }

    private static bool TryExtractDrinkingWindow(JsonElement element, out DrinkingWindowPrediction result)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("Start", out var startProperty)
            && element.TryGetProperty("End", out var endProperty)
            && TryReadYear(startProperty, out var start)
            && TryReadYear(endProperty, out var end))
        {
            result = new DrinkingWindowPrediction(start, end);
            return true;
        }

        result = default!;
        return false;
    }

    private static bool TryReadYear(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            if (element.TryGetInt32(out value))
            {
                return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var raw = element.GetString();
            if (!string.IsNullOrWhiteSpace(raw)
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }
}

public sealed record ChatGptOptions
{
    public const string ConfigurationSectionName = "OpenAI";
    public const string FallbackModel = "gpt-4o-mini";

    public string? ApiKey { get; init; }
    public string? DefaultModel { get; init; }
    public string? SmallModel { get; init; }
    public string? WebSearchModel { get; init; }
    public string? SurfEyeAnalysisModel { get; init; }
    public string? TasteProfileModel { get; init; }
    public string? DrinkingWindowWorkflowId { get; init; }
    public string? DrinkingWindowWorkflowKey { get; init; }
}

public sealed class ChatGptServiceNotConfiguredException : InvalidOperationException
{
    public ChatGptServiceNotConfiguredException()
        : base("ChatGPT integration is not configured.")
    {
    }
}

public sealed record DrinkingWindowPrediction(int Start, int End);
