// TODO Phase 3: replace with WeaveLLM.Providers IChatModel
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using KnowledgeLLM.Core.Configuration;
using Microsoft.Extensions.Options;
using WeaveLLM.Core.Models;

namespace KnowledgeLLM.Core.Pipeline;

/// <summary>Thin internal HTTP client that calls the OpenAI chat completions endpoint.</summary>
public class OpenAIChatClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KnowledgeLLMOptions _options;

    /// <summary>Initialises the client with an HTTP client factory and configuration.</summary>
    /// <param name="httpClientFactory">Factory that provides the named "openai-chat" <see cref="HttpClient"/>.</param>
    /// <param name="options">KnowledgeLLM configuration bound from the "KnowledgeLLM" config section.</param>
    public OpenAIChatClient(IHttpClientFactory httpClientFactory, IOptions<KnowledgeLLMOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    /// <summary>Protected constructor for test subclasses and proxy generation.</summary>
    protected OpenAIChatClient()
    {
        _httpClientFactory = null!;
        _options = null!;
    }

    /// <summary>
    /// Sends a prompt to the OpenAI chat completions endpoint and returns the assistant reply.
    /// Never throws — all failures are returned as <see cref="ChainResult{T}"/> errors.
    /// </summary>
    /// <param name="prompt">The user prompt to send. Must not be null or whitespace.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Success: the assistant's reply as a <see cref="string"/>.
    /// Failure codes: INVALID_INPUT, INVALID_CONFIGURATION, AUTHENTICATION_FAILED,
    /// RATE_LIMIT_EXCEEDED, PROVIDER_ERROR, CANCELLED, NETWORK_TIMEOUT.
    /// </returns>
    public virtual async Task<ChainResult<string>> CompleteAsync(string prompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return ChainResult<string>.Failure(WeaveLLMError.InvalidInput("prompt must not be null or whitespace."));

        var keyError = ValidateApiKey();
        if (keyError is not null) return keyError;

        var client = _httpClientFactory.CreateClient("openai-chat");
        var requestBody = new ChatRequest(
            _options.OpenAI.ChatModel,
            new[] { new ChatMessage("user", prompt) },
            1024);

        try
        {
            var response = await client.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", requestBody, ct);
            var httpError = MapHttpError(response);
            if (httpError is not null) return httpError;

            var body = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct);
            return ChainResult<string>.Success(body!.Choices[0].Message.Content);
        }
        catch (TaskCanceledException ex)
        {
            return BuildCancelledOrTimeoutError(ex, ct);
        }
    }

    private ChainResult<string>? ValidateApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.OpenAI.ApiKey))
            return null;

        return ChainResult<string>.Failure(new WeaveLLMError(
            "OpenAI API key is not configured. Set KnowledgeLLM:OpenAI:ApiKey in " +
            "appsettings.json or via KNOWLEDGELLM__OPENAI__APIKEY environment variable.",
            "INVALID_CONFIGURATION"));
    }

    private static ChainResult<string>? MapHttpError(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return null;

        var statusCode = (int)response.StatusCode;

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return ChainResult<string>.Failure(new WeaveLLMError(
                "OpenAI rejected the API key. Verify it is valid at platform.openai.com/api-keys.",
                "AUTHENTICATION_FAILED"));

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return ChainResult<string>.Failure(new WeaveLLMError(
                "OpenAI returned 429. Reduce request frequency or add retry logic.",
                "RATE_LIMIT_EXCEEDED"));

        if (statusCode >= 500)
            return ChainResult<string>.Failure(new WeaveLLMError(
                $"OpenAI returned {statusCode}. This is likely transient — retry the request.",
                "PROVIDER_ERROR",
                new HttpRequestException($"HTTP {statusCode}")));

        return ChainResult<string>.Failure(new WeaveLLMError(
            $"OpenAI returned unexpected status {statusCode}.",
            "PROVIDER_ERROR"));
    }

    private static ChainResult<string> BuildCancelledOrTimeoutError(TaskCanceledException ex, CancellationToken ct) =>
        ct.IsCancellationRequested
            ? ChainResult<string>.Failure(new WeaveLLMError("Request cancelled.", "CANCELLED", ex))
            : ChainResult<string>.Failure(new WeaveLLMError(
                "Request to OpenAI timed out. Check network connectivity or increase HttpClient timeout.",
                "NETWORK_TIMEOUT", ex));
}

// ---- Private DTOs ----------------------------------------------------------

file sealed record ChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] ChatMessage[] Messages,
    [property: JsonPropertyName("max_tokens")] int MaxTokens);

file sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

file sealed record ChatResponse(
    [property: JsonPropertyName("choices")] ChatChoice[] Choices);

file sealed record ChatChoice(
    [property: JsonPropertyName("message")] ChatMessage Message);
