using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using OpenAI.Chat;

namespace AllMCPSolution.Services;

public interface IChatGptService
{
    Task<ChatCompletion> GetChatCompletionAsync(
        IEnumerable<ChatMessage> messages,
        string? model = null,
        double? temperature = null,
        CancellationToken ct = default);
}

public sealed class ChatGptService : IChatGptService
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<ChatGptService> _logger;
    private readonly string _defaultModel;

    public ChatGptService(
        IConfiguration configuration,
        ILogger<ChatGptService> logger)
    {
        var options = configuration.GetSection(ChatGptOptions.ConfigurationSectionName).Get<ChatGptOptions>();
        if (options is null || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("OpenAI:ApiKey must be configured in appsettings.");
        }

        _defaultModel = string.IsNullOrWhiteSpace(options.DefaultModel)
            ? ChatGptOptions.FallbackModel
            : options.DefaultModel!;

        _logger = logger;

        _chatClient = new ChatClient(_defaultModel, options.ApiKey);
    }

    public async Task<ChatCompletion> GetChatCompletionAsync(
        IEnumerable<ChatMessage> messages,
        string? model = null,
        double? temperature = null,
        CancellationToken ct = default)
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

        var completionOptions = new ChatCompletionOptions();

        if (!string.IsNullOrWhiteSpace(model) && !string.Equals(model, _chatClient.Model, StringComparison.Ordinal))
        {
            completionOptions.Model = model;
        }

        if (temperature.HasValue)
        {
            completionOptions.Temperature = (float?)temperature.Value;
        }

        try
        {
            return await _chatClient
                .CompleteChatAsync(materializedMessages, completionOptions, ct)
                .ConfigureAwait(false);
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
}

public sealed record ChatGptOptions
{
    public const string ConfigurationSectionName = "OpenAI";
    public const string FallbackModel = "gpt-4o-mini";

    public string? ApiKey { get; init; }
    public string? DefaultModel { get; init; }
}
