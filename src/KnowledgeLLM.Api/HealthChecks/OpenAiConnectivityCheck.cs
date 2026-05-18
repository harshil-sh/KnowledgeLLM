using KnowledgeLLM.Core.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace KnowledgeLLM.Api.HealthChecks;

/// <summary>
/// Health check that verifies connectivity to the OpenAI API by making a lightweight
/// <c>GET /v1/models</c> request with the configured API key.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description>Returns <see cref="HealthCheckResult.Healthy"/> on HTTP 200.</description></item>
///   <item><description>Returns <see cref="HealthCheckResult.Degraded"/> on HTTP 401 — the key is present but rejected by OpenAI.</description></item>
///   <item><description>Returns <see cref="HealthCheckResult.Unhealthy"/> when no response is received or any exception occurs.</description></item>
/// </list>
/// The request times out after 5 seconds. Exceptions are never propagated — all failure paths
/// return an <see cref="HealthCheckResult"/> with a descriptive message.
/// </remarks>
public sealed class OpenAiConnectivityCheck : IHealthCheck
{
    private static readonly Uri ModelsEndpoint = new("https://api.openai.com/v1/models");
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<KnowledgeLLMOptions> _options;

    /// <summary>
    /// Initialises a new instance of <see cref="OpenAiConnectivityCheck"/>.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create a short-lived <see cref="HttpClient"/>.</param>
    /// <param name="options">Bound KnowledgeLLM configuration, providing the OpenAI API key.</param>
    public OpenAiConnectivityCheck(
        IHttpClientFactory httpClientFactory,
        IOptions<KnowledgeLLMOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options           = options;
    }

    /// <summary>
    /// Executes the connectivity check against <c>https://api.openai.com/v1/models</c>.
    /// </summary>
    /// <param name="context">Health check context (unused).</param>
    /// <param name="cancellationToken">Token that signals the probe has timed out or been cancelled.</param>
    /// <returns>
    /// A <see cref="HealthCheckResult"/> indicating whether OpenAI is reachable and the
    /// configured API key is accepted.
    /// </returns>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var apiKey = _options.Value.OpenAI.ApiKey;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeout);

            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, ModelsEndpoint);
            if (!string.IsNullOrWhiteSpace(apiKey))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.OK          => HealthCheckResult.Healthy("OpenAI API is reachable."),
                System.Net.HttpStatusCode.Unauthorized => HealthCheckResult.Degraded(
                    "OpenAI API returned 401 — API key is present but was rejected."),
                _ => HealthCheckResult.Unhealthy(
                    $"OpenAI API returned unexpected status {(int)response.StatusCode} {response.ReasonPhrase}."),
            };
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("OpenAI API check timed out or was cancelled.");
        }
        catch (HttpRequestException ex)
        {
            return HealthCheckResult.Unhealthy($"OpenAI API is unreachable: {ex.Message}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"OpenAI API check failed unexpectedly: {ex.Message}");
        }
    }
}
