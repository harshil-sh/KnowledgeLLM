using System.Net;
using FluentAssertions;
using KnowledgeLLM.Api.HealthChecks;
using KnowledgeLLM.Core.Configuration;
using KnowledgeLLM.Core.Tests.Helpers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace KnowledgeLLM.Core.Tests.HealthChecks;

public sealed class OpenAiConnectivityCheckTests
{
    // ---- Builder helpers -------------------------------------------------------

    private static IOptions<KnowledgeLLMOptions> ValidOptions() =>
        Options.Create(new KnowledgeLLMOptions
        {
            OpenAI = new OpenAIOptions { ApiKey = "sk-test" }
        });

    private static OpenAiConnectivityCheck BuildSut(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var factory = Substitute.For<IHttpClientFactory>();
        // CreateClient() extension method resolves to CreateClient(string.Empty)
        factory.CreateClient(Arg.Any<string>()).Returns(httpClient);
        return new OpenAiConnectivityCheck(factory, ValidOptions());
    }

    private static HealthCheckContext BuildContext() =>
        new()
        {
            Registration = new HealthCheckRegistration(
                name: "openai",
                instance: Substitute.For<IHealthCheck>(),
                failureStatus: HealthStatus.Unhealthy,
                tags: null)
        };

    // ---- HTTP 200 → Healthy ----------------------------------------------------

    [Fact]
    public async Task CheckHealthAsync_Returns200_ReturnsHealthy()
    {
        var handler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var sut = BuildSut(handler);

        var result = await sut.CheckHealthAsync(BuildContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("reachable");
    }

    // ---- HTTP 401 → Degraded ---------------------------------------------------

    [Fact]
    public async Task CheckHealthAsync_Returns401_ReturnsDegraded()
    {
        var handler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var sut = BuildSut(handler);

        var result = await sut.CheckHealthAsync(BuildContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("401");
    }

    // ---- HttpRequestException → Unhealthy --------------------------------------

    [Fact]
    public async Task CheckHealthAsync_ThrowsHttpRequestException_ReturnsUnhealthy()
    {
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException("Connection refused"));
        var sut = BuildSut(handler);

        var result = await sut.CheckHealthAsync(BuildContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("unreachable");
    }

    // ---- TaskCanceledException (timeout) → Unhealthy ---------------------------

    [Fact]
    public async Task CheckHealthAsync_ThrowsTaskCanceledException_ReturnsUnhealthy()
    {
        var handler = new ThrowingHttpMessageHandler(new TaskCanceledException("Request timed out"));
        var sut = BuildSut(handler);

        var result = await sut.CheckHealthAsync(BuildContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("timed out");
    }
}
