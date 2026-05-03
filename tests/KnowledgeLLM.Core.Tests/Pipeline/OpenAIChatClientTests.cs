using System.Net;
using FluentAssertions;
using KnowledgeLLM.Core.Configuration;
using KnowledgeLLM.Core.Pipeline;
using KnowledgeLLM.Core.Tests.Helpers;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace KnowledgeLLM.Core.Tests.Pipeline;

public sealed class OpenAIChatClientTests
{
    private const string ChatResponseJson =
        """{"choices":[{"message":{"role":"assistant","content":"Hello from OpenAI!"}}]}""";

    // ---- Builder helpers -------------------------------------------------------

    private static IOptions<KnowledgeLLMOptions> ValidOptions() =>
        Options.Create(new KnowledgeLLMOptions
        {
            OpenAI = new OpenAIOptions
            {
                ApiKey = "sk-test",
                ChatModel = "gpt-4o-mini"
            }
        });

    private static IOptions<KnowledgeLLMOptions> OptionsWithKey(string? apiKey) =>
        Options.Create(new KnowledgeLLMOptions
        {
            OpenAI = new OpenAIOptions { ApiKey = apiKey! }
        });

    private static OpenAIChatClient BuildSut(
        IOptions<KnowledgeLLMOptions> options,
        HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(httpClient);
        return new OpenAIChatClient(factory, options);
    }

    // ---- CompleteAsync: invalid input -----------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CompleteAsync_NullOrWhitespacePrompt_ReturnsInvalidInput(string? prompt)
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await sut.CompleteAsync(prompt!, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_INPUT");
    }

    // ---- CompleteAsync: missing / invalid API key -----------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CompleteAsync_MissingApiKey_ReturnsInvalidConfiguration(string? apiKey)
    {
        var sut = BuildSut(OptionsWithKey(apiKey), new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await sut.CompleteAsync("Tell me about AI.", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_CONFIGURATION");
    }

    // ---- CompleteAsync: HTTP success ------------------------------------------

    [Fact]
    public async Task CompleteAsync_200Ok_ReturnsAssistantContent()
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(HttpClientFactoryHelper.JsonOk(ChatResponseJson)));

        var result = await sut.CompleteAsync("What is AI?", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Hello from OpenAI!");
    }

    // ---- CompleteAsync: HTTP error codes --------------------------------------

    [Fact]
    public async Task CompleteAsync_401Unauthorized_ReturnsAuthenticationFailed()
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var result = await sut.CompleteAsync("What is AI?", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AUTHENTICATION_FAILED");
    }

    [Fact]
    public async Task CompleteAsync_429TooManyRequests_ReturnsRateLimitExceeded()
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));

        var result = await sut.CompleteAsync("What is AI?", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("RATE_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task CompleteAsync_500InternalServerError_ReturnsProviderError()
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await sut.CompleteAsync("What is AI?", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("PROVIDER_ERROR");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task CompleteAsync_Other5xxStatusCode_ReturnsProviderError(HttpStatusCode statusCode)
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(statusCode)));

        var result = await sut.CompleteAsync("What is AI?", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("PROVIDER_ERROR");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]       // 403
    [InlineData(HttpStatusCode.UnprocessableEntity)] // 422
    public async Task CompleteAsync_Unexpected4xxStatusCode_ReturnsProviderError(HttpStatusCode statusCode)
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(statusCode)));

        var result = await sut.CompleteAsync("What is AI?", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("PROVIDER_ERROR");
    }

    // ---- CompleteAsync: TaskCanceledException ---------------------------------

    [Fact]
    public async Task CompleteAsync_TimeoutWithExternalTokenNotFired_ReturnsNetworkTimeout()
    {
        var sut = BuildSut(ValidOptions(), new ThrowingHttpMessageHandler(new TaskCanceledException("timeout")));

        var result = await sut.CompleteAsync("What is AI?", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NETWORK_TIMEOUT");
    }

    [Fact]
    public async Task CompleteAsync_TaskCancelledWithTokenFired_ReturnsCancelled()
    {
        using var cts = new CancellationTokenSource();
        var sut = BuildSut(ValidOptions(), new CancelOnSendHandler(cts));

        var result = await sut.CompleteAsync("What is AI?", cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CANCELLED");
    }

    // ---- Thread safety --------------------------------------------------------

    [Fact]
    public async Task CompleteAsync_ConcurrentCallsOnSameInstance_AllSucceed()
    {
        // Each CreateClient call returns a fresh HttpClient so responses are independent
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>())
               .Returns(_ => new HttpClient(new JsonFactoryHttpMessageHandler(ChatResponseJson)));
        var sut = new OpenAIChatClient(factory, ValidOptions());

        var tasks = Enumerable.Range(0, 20).Select(i => sut.CompleteAsync($"question {i}", CancellationToken.None));
        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r =>
        {
            r.IsSuccess.Should().BeTrue();
            r.Value.Should().Be("Hello from OpenAI!");
        });
    }
}
