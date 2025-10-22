using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AllMCPSolution.Services;

public interface IChatGptService
{
    Task<ChatGptResponse> GetChatCompletionAsync(
        IEnumerable<ChatGptMessage> messages,
        string? model = null,
        double? temperature = null,
        CancellationToken ct = default);
}

public sealed class ChatGptService : IChatGptService
{
    private static readonly Uri BaseUri = new("https://api.openai.com/v1/");

    private readonly HttpClient _httpClient;
    private readonly ILogger<ChatGptService> _logger;
    private readonly string _defaultModel;
    private readonly JsonSerializerOptions _serializerOptions;

    public ChatGptService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ChatGptService> logger)
    {
        _httpClient = httpClientFactory.CreateClient(nameof(ChatGptService));
        _httpClient.BaseAddress = BaseUri;

        var options = configuration.GetSection(ChatGptOptions.ConfigurationSectionName).Get<ChatGptOptions>();
        if (options is null || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("OpenAI:ApiKey must be configured in appsettings.");
        }

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _defaultModel = string.IsNullOrWhiteSpace(options.DefaultModel)
            ? ChatGptOptions.FallbackModel
            : options.DefaultModel!;

        _logger = logger;

        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        _serializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public async Task<ChatGptResponse> GetChatCompletionAsync(
        IEnumerable<ChatGptMessage> messages,
        string? model = null,
        double? temperature = null,
        CancellationToken ct = default)
    {
        if (messages is null)
        {
            throw new ArgumentNullException(nameof(messages));
        }

        var materializedMessages = messages
            .Select((message, index) =>
            {
                if (message is null)
                {
                    throw new ArgumentException($"Message at index {index} cannot be null.", nameof(messages));
                }

                return message with
                {
                    Role = string.IsNullOrWhiteSpace(message.Role) ? "user" : message.Role.Trim(),
                    Content = string.IsNullOrWhiteSpace(message.Content) ? string.Empty : message.Content
                };
            })
            .ToArray();

        if (materializedMessages.Length == 0)
        {
            throw new ArgumentException("At least one chat message is required.", nameof(messages));
        }

        var request = new ChatGptRequest
        {
            Model = model ?? _defaultModel,
            Messages = materializedMessages,
            Temperature = temperature
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(request, _serializerOptions), Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogError(
                "ChatGPT API request failed with status {StatusCode}: {Body}",
                (int)response.StatusCode,
                body);
            throw new HttpRequestException($"ChatGPT API returned status code {(int)response.StatusCode}.");
        }

        var payload = await response.Content.ReadFromJsonAsync<ChatGptResponse>(_serializerOptions, ct).ConfigureAwait(false);
        if (payload is null)
        {
            throw new InvalidOperationException("ChatGPT API response payload was empty.");
        }

        return payload;
    }
}

public sealed record ChatGptOptions
{
    public const string ConfigurationSectionName = "OpenAI";
    public const string FallbackModel = "gpt-4o-mini";

    public string? ApiKey { get; init; }
    public string? DefaultModel { get; init; }
}

public sealed record ChatGptRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = ChatGptOptions.FallbackModel;

    [JsonPropertyName("messages")]
    public IReadOnlyList<ChatGptMessage> Messages { get; init; } = Array.Empty<ChatGptMessage>();

    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }
}

public sealed record ChatGptMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "user";

    [JsonPropertyName("content")]
    public string? Content { get; init; } = string.Empty;
}

public sealed record ChatGptResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("choices")]
    public IReadOnlyList<ChatGptChoice> Choices { get; init; } = Array.Empty<ChatGptChoice>();

    [JsonPropertyName("usage")]
    public ChatGptUsage? Usage { get; init; }
}

public sealed record ChatGptChoice
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("message")]
    public ChatGptMessage Message { get; init; } = new();

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

public sealed record ChatGptUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; init; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; init; }
}
