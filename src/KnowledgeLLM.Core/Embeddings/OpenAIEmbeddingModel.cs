using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using KnowledgeLLM.Core.Configuration;
using Microsoft.Extensions.Options;
using WeaveLLM.Core.Models;

namespace KnowledgeLLM.Core.Embeddings;

/// <summary>OpenAI-backed embedding model that calls the /v1/embeddings endpoint.</summary>
public sealed class OpenAIEmbeddingModel : IEmbeddingModel
{
    private const int MaxOpenAIRetryAttempts = 3;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KnowledgeLLMOptions _options;

    /// <summary>Initialises the model with an HTTP client factory and configuration.</summary>
    /// <param name="httpClientFactory">Factory that provides the named "openai-embeddings" <see cref="HttpClient"/>.</param>
    /// <param name="options">KnowledgeLLM configuration bound from the "KnowledgeLLM" config section.</param>
    public OpenAIEmbeddingModel(IHttpClientFactory httpClientFactory, IOptions<KnowledgeLLMOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    /// <summary>Dimensionality of the embedding vectors produced by this model.</summary>
    public int Dimensions => _options.OpenAI.EmbeddingDimensions;

    /// <summary>
    /// Embeds a single text string into a float vector using the OpenAI embeddings API.
    /// Never throws — all failures are returned as <see cref="ChainResult{T}"/> errors.
    /// </summary>
    /// <param name="text">Input text to embed. Must not be null or whitespace.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Success: a <c>float[]</c> of length <see cref="Dimensions"/>.
    /// Failure codes: INVALID_INPUT, INVALID_CONFIGURATION, AUTHENTICATION_FAILED,
    /// RATE_LIMIT_EXCEEDED, PROVIDER_ERROR, CANCELLED, NETWORK_TIMEOUT.
    /// </returns>
    public async Task<ChainResult<float[]>> EmbedAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ChainResult<float[]>.Failure(WeaveLLMError.InvalidInput("text must not be null or whitespace."));

        var keyError = ValidateApiKey<float[]>();
        if (keyError is not null) return keyError;

        var client = _httpClientFactory.CreateClient("openai-embeddings");
        var requestBody = new SingleEmbeddingRequest(_options.OpenAI.EmbeddingModel, text);

        return await SendWithRetryAsync(
            () => client.PostAsJsonAsync("https://api.openai.com/v1/embeddings", requestBody, ct),
            async response =>
            {
                var body = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: ct);
                return body!.Data[0].Embedding;
            },
            ct);
    }

    /// <summary>
    /// Embeds a batch of text strings into float vectors using the OpenAI embeddings API.
    /// Results are ordered by the index returned in the API response.
    /// Never throws — all failures are returned as <see cref="ChainResult{T}"/> errors.
    /// </summary>
    /// <param name="texts">Input texts. Must not be null or empty.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Success: an <see cref="IReadOnlyList{T}"/> of <c>float[]</c> vectors in original input order.
    /// Failure codes: INVALID_INPUT, INVALID_CONFIGURATION, AUTHENTICATION_FAILED,
    /// RATE_LIMIT_EXCEEDED, PROVIDER_ERROR, CANCELLED, NETWORK_TIMEOUT.
    /// </returns>
    public async Task<ChainResult<IReadOnlyList<float[]>>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (texts is null || texts.Count == 0)
            return ChainResult<IReadOnlyList<float[]>>.Failure(
                WeaveLLMError.InvalidInput("texts must not be null or empty."));

        var keyError = ValidateApiKey<IReadOnlyList<float[]>>();
        if (keyError is not null) return keyError;

        var client = _httpClientFactory.CreateClient("openai-embeddings");
        var requestBody = new BatchEmbeddingRequest(_options.OpenAI.EmbeddingModel, texts);

        return await SendWithRetryAsync<IReadOnlyList<float[]>>(
            () => client.PostAsJsonAsync("https://api.openai.com/v1/embeddings", requestBody, ct),
            async response =>
            {
                var body = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: ct);
                return body!.Data
                    .OrderBy(d => d.Index)
                    .Select(d => d.Embedding)
                    .ToArray();
            },
            ct);
    }

    private static async Task<ChainResult<T>> SendWithRetryAsync<T>(
        Func<Task<HttpResponseMessage>> sendAsync,
        Func<HttpResponseMessage, Task<T>> readSuccessAsync,
        CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxOpenAIRetryAttempts; attempt++)
        {
            try
            {
                using var response = await sendAsync();
                var httpError = MapHttpError<T>(response);
                if (httpError is null)
                    return ChainResult<T>.Success(await readSuccessAsync(response));

                if (!IsRetriableStatus(response.StatusCode) || attempt == MaxOpenAIRetryAttempts)
                    return httpError;
            }
            catch (TaskCanceledException ex)
            {
                if (ct.IsCancellationRequested || attempt == MaxOpenAIRetryAttempts)
                    return BuildCancelledOrTimeoutError<T>(ex, ct);
            }
            catch (HttpRequestException ex)
            {
                if (attempt == MaxOpenAIRetryAttempts)
                    return ChainResult<T>.Failure(WeaveLLMError.ProviderError(
                        "OpenAI",
                        "Request to OpenAI failed after retries.",
                        ex));
            }

            var delayResult = await DelayBeforeRetryAsync<T>(attempt, ct);
            if (delayResult is not null) return delayResult;
        }

        return ChainResult<T>.Failure(WeaveLLMError.ProviderError("OpenAI", "OpenAI request failed after retries."));
    }

    private static async Task<ChainResult<T>?> DelayBeforeRetryAsync<T>(int attempt, CancellationToken ct)
    {
        try
        {
            await Task.Delay(GetRetryDelay(attempt), ct);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            return BuildCancelledOrTimeoutError<T>(ex, ct);
        }
    }

    private static bool IsRetriableStatus(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static TimeSpan GetRetryDelay(int attempt) =>
        TimeSpan.FromMilliseconds(InitialRetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));

    private ChainResult<T>? ValidateApiKey<T>()
    {
        if (!string.IsNullOrWhiteSpace(_options.OpenAI.ApiKey))
            return null;

        return ChainResult<T>.Failure(WeaveLLMError.InvalidConfiguration(
            "OpenAI API key is not configured. Set KnowledgeLLM:OpenAI:ApiKey in " +
            "appsettings.json or via KNOWLEDGELLM__OPENAI__APIKEY environment variable."));
    }

    private static ChainResult<T>? MapHttpError<T>(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return null;

        var statusCode = (int)response.StatusCode;

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return ChainResult<T>.Failure(WeaveLLMError.AuthenticationFailed(
                "OpenAI rejected the API key. Verify it is valid at platform.openai.com/api-keys."));

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return ChainResult<T>.Failure(WeaveLLMError.RateLimitExceeded(
                "OpenAI returned 429 after retry attempts. Reduce request frequency."));

        if (statusCode >= 500)
            return ChainResult<T>.Failure(WeaveLLMError.ProviderError(
                "OpenAI",
                $"OpenAI returned {statusCode}. This is likely transient and was retried.",
                new HttpRequestException($"HTTP {statusCode}")));

        return ChainResult<T>.Failure(WeaveLLMError.ProviderError(
            "OpenAI",
            $"OpenAI returned unexpected status {statusCode}."));
    }

    private static ChainResult<T> BuildCancelledOrTimeoutError<T>(TaskCanceledException ex, CancellationToken ct) =>
        ct.IsCancellationRequested
            ? ChainResult<T>.Failure(WeaveLLMError.Cancelled("Request cancelled.", ex))
            : ChainResult<T>.Failure(WeaveLLMError.NetworkTimeout(
                "Request to OpenAI timed out. Check network connectivity or increase HttpClient timeout.", ex));
}

// ---- Private DTOs ----------------------------------------------------------

file sealed record SingleEmbeddingRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] string Input);

file sealed record BatchEmbeddingRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

file sealed record EmbeddingResponse(
    [property: JsonPropertyName("data")] EmbeddingItem[] Data);

file sealed record EmbeddingItem(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("embedding")] float[] Embedding);
