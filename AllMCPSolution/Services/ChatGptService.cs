using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenAI.Chat;

namespace AllMCPSolution.Services;

public interface IChatGptService
{
    Task<ChatCompletion> GetChatCompletionAsync(
        IEnumerable<ChatMessage> messages,
        string? model = null,
        double? temperature = null,
        CancellationToken ct = default);

    IAsyncEnumerable<string> StreamChatCompletionAsync(
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
    private readonly string _apiKey;

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

        _apiKey = options.ApiKey!;
        _chatClient = new ChatClient(_defaultModel, _apiKey);
    }

    public async Task<ChatCompletion> GetChatCompletionAsync(
        IEnumerable<ChatMessage> messages,
        string? model = null,
        double? temperature = null,
        CancellationToken ct = default)
    {
        var materializedMessages = MaterializeMessages(messages);
        var completionOptions = CreateCompletionOptions(temperature);
        var client = ResolveClient(model);

        try
        {
            return await client
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

    public async IAsyncEnumerable<string> StreamChatCompletionAsync(
        IEnumerable<ChatMessage> messages,
        string? model = null,
        double? temperature = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var materializedMessages = MaterializeMessages(messages);
        var completionOptions = CreateCompletionOptions(temperature);
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
                if (update?.ContentUpdate is not { Count: > 0 })
                {
                    continue;
                }

                foreach (var part in update.ContentUpdate)
                {
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

    private static ChatCompletionOptions CreateCompletionOptions(double? temperature)
    {
        var options = new ChatCompletionOptions();

        return options;
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
        if (!string.IsNullOrWhiteSpace(model) && !string.Equals(model, _defaultModel, StringComparison.Ordinal))
        {
            return new ChatClient(model!, _apiKey);
        }

        return _chatClient;
    }
}

public sealed record ChatGptOptions
{
    public const string ConfigurationSectionName = "OpenAI";
    public const string FallbackModel = "gpt-4o-mini";

    public string? ApiKey { get; init; }
    public string? DefaultModel { get; init; }
}
