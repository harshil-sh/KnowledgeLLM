using System.Net;
using System.Text;

namespace KnowledgeLLM.Core.Tests.Helpers;

/// <summary>Fake handler that returns a pre-built <see cref="HttpResponseMessage"/> on every call.</summary>
internal sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(response);
    }
}

/// <summary>Fake handler that creates a fresh <see cref="HttpResponseMessage"/> with a JSON body on every call.
/// Safe for concurrent use because each invocation returns a new content stream.</summary>
internal sealed class JsonFactoryHttpMessageHandler(string json) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }
}

/// <summary>Fake handler that always throws the supplied exception.</summary>
internal sealed class ThrowingHttpMessageHandler(Exception exception) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => throw exception;
}

/// <summary>
/// Fake handler that cancels the supplied <see cref="CancellationTokenSource"/> and then throws
/// <see cref="TaskCanceledException"/>, simulating an externally-cancelled in-flight request.
/// </summary>
internal sealed class CancelOnSendHandler(CancellationTokenSource cts) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        cts.Cancel();
        throw new TaskCanceledException("Cancelled by test.", null, cts.Token);
    }
}

/// <summary>Shared HTTP factory helpers.</summary>
internal static class HttpClientFactoryHelper
{
    /// <summary>Returns a JSON 200 OK <see cref="HttpResponseMessage"/>.</summary>
    internal static HttpResponseMessage JsonOk(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}
