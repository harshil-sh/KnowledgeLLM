using System.Text.Json;
using KnowledgeLLM.Core.Configuration;
using Microsoft.Extensions.Options;

namespace KnowledgeLLM.Api.Middleware;

/// <summary>
/// Middleware that enforces API key authentication via the <c>X-Api-Key</c> HTTP header.
/// When <see cref="ApiOptions.ApiKey"/> is empty, authentication is bypassed entirely,
/// allowing zero-config local development without breaking existing behaviour.
/// </summary>
/// <remarks>
/// Exempt paths (GET only): <c>/health</c>, <c>/health/ready</c>, <c>/health/live</c>,
/// and any path under <c>/swagger</c>.
/// </remarks>
public sealed class ApiKeyMiddleware
{
    private const string ApiKeyHeader = "X-Api-Key";

    private static readonly PathString HealthPath      = new("/health");
    private static readonly PathString HealthReadyPath = new("/health/ready");
    private static readonly PathString HealthLivePath  = new("/health/live");
    private static readonly PathString SwaggerPath     = new("/swagger");

    // JsonSerializerDefaults.Web applies camelCase naming — produces {"code":…,"message":…}.
    private static readonly JsonSerializerOptions s_jsonOpts = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly IOptions<KnowledgeLLMOptions> _options;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    /// <summary>Initializes a new instance of <see cref="ApiKeyMiddleware"/>.</summary>
    public ApiKeyMiddleware(
        RequestDelegate next,
        IOptions<KnowledgeLLMOptions> options,
        ILogger<ApiKeyMiddleware> logger)
    {
        _next    = next;
        _options = options;
        _logger  = logger;
    }

    /// <summary>Processes an HTTP request, enforcing the API key policy.</summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;

        if (IsExempt(request.Method, request.Path))
        {
            await _next(context);
            return;
        }

        var configuredKey = _options.Value.Api.ApiKey;

        // Zero-config bypass: no key configured means auth is disabled.
        if (string.IsNullOrEmpty(configuredKey))
        {
            await _next(context);
            return;
        }

        if (!request.Headers.TryGetValue(ApiKeyHeader, out var provided))
        {
            _logger.LogWarning(
                "Request to {Path} rejected: {Header} header is missing.",
                request.Path, ApiKeyHeader);
            await WriteErrorAsync(context.Response, 401, "UNAUTHORIZED", "X-Api-Key header is required.");
            return;
        }

        if (!string.Equals(provided, configuredKey, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Request to {Path} rejected: invalid API key {MaskedKey}.",
                request.Path, Mask(provided.ToString()));
            await WriteErrorAsync(context.Response, 403, "FORBIDDEN", "Invalid API key.");
            return;
        }

        await _next(context);
    }

    private static bool IsExempt(string method, PathString path)
    {
        if (!HttpMethods.IsGet(method))
            return false;

        return path == HealthPath
            || path == HealthReadyPath
            || path == HealthLivePath
            || path.StartsWithSegments(SwaggerPath);
    }

    private static string Mask(string key) =>
        key.Length <= 4 ? "****" : string.Concat(key.AsSpan(0, 4), "***");

    private static Task WriteErrorAsync(
        HttpResponse response, int statusCode, string code, string message)
    {
        response.StatusCode  = statusCode;
        response.ContentType = "application/json";
        return response.WriteAsync(
            JsonSerializer.Serialize(new ErrorBody(code, message), s_jsonOpts));
    }

    /// <summary>Shape of the JSON error body written on 401/403 responses.</summary>
    private sealed record ErrorBody(string Code, string Message);
}
