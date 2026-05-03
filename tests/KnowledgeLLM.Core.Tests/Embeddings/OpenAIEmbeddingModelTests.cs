using System.Net;
using FluentAssertions;
using KnowledgeLLM.Core.Configuration;
using KnowledgeLLM.Core.Embeddings;
using KnowledgeLLM.Core.Tests.Helpers;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace KnowledgeLLM.Core.Tests.Embeddings;

public sealed class OpenAIEmbeddingModelTests
{
    // JSON bodies that use exact integer-valued floats to avoid floating-point rounding surprises
    private const string SingleEmbeddingJson =
        """{"data":[{"index":0,"embedding":[1.0,2.0,3.0]}]}""";

    // Index order reversed intentionally — code must sort by index before returning
    private const string BatchEmbeddingJson =
        """{"data":[{"index":1,"embedding":[4.0,5.0,6.0]},{"index":0,"embedding":[1.0,2.0,3.0]}]}""";

    // ---- Builder helpers -------------------------------------------------------

    private static IOptions<KnowledgeLLMOptions> ValidOptions(int dims = 3) =>
        Options.Create(new KnowledgeLLMOptions
        {
            OpenAI = new OpenAIOptions
            {
                ApiKey = "sk-test",
                EmbeddingModel = "text-embedding-3-small",
                EmbeddingDimensions = dims
            }
        });

    private static IOptions<KnowledgeLLMOptions> OptionsWithKey(string? apiKey) =>
        Options.Create(new KnowledgeLLMOptions
        {
            OpenAI = new OpenAIOptions { ApiKey = apiKey! }
        });

    private static OpenAIEmbeddingModel BuildSut(
        IOptions<KnowledgeLLMOptions> options,
        HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(httpClient);
        return new OpenAIEmbeddingModel(factory, options);
    }

    // ---- Dimensions property ---------------------------------------------------

    [Fact]
    public void Dimensions_ReflectsConfiguredValue()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var sut = new OpenAIEmbeddingModel(factory, ValidOptions(dims: 1536));

        sut.Dimensions.Should().Be(1536);
    }

    // ---- EmbedAsync: invalid input --------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmbedAsync_NullOrWhitespaceText_ReturnsInvalidInput(string? text)
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await sut.EmbedAsync(text!, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_INPUT");
    }

    // ---- EmbedAsync: missing / invalid API key --------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmbedAsync_MissingApiKey_ReturnsInvalidConfiguration(string? apiKey)
    {
        var sut = BuildSut(OptionsWithKey(apiKey), new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await sut.EmbedAsync("hello world", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_CONFIGURATION");
    }

    // ---- EmbedAsync: HTTP success ---------------------------------------------

    [Fact]
    public async Task EmbedAsync_200Ok_ReturnsEmbeddingVector()
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(HttpClientFactoryHelper.JsonOk(SingleEmbeddingJson)));

        var result = await sut.EmbedAsync("hello world", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(1.0f, 2.0f, 3.0f);
    }

    // ---- EmbedAsync: HTTP error codes -----------------------------------------

    [Fact]
    public async Task EmbedAsync_401Unauthorized_ReturnsAuthenticationFailed()
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var result = await sut.EmbedAsync("hello", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AUTHENTICATION_FAILED");
    }

    [Fact]
    public async Task EmbedAsync_429TooManyRequests_ReturnsRateLimitExceeded()
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));

        var result = await sut.EmbedAsync("hello", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("RATE_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task EmbedAsync_500InternalServerError_ReturnsProviderError()
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await sut.EmbedAsync("hello", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("PROVIDER_ERROR");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task EmbedAsync_Other5xxStatusCode_ReturnsProviderError(HttpStatusCode statusCode)
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(statusCode)));

        var result = await sut.EmbedAsync("hello", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("PROVIDER_ERROR");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]       // 403
    [InlineData(HttpStatusCode.UnprocessableEntity)] // 422
    public async Task EmbedAsync_Unexpected4xxStatusCode_ReturnsProviderError(HttpStatusCode statusCode)
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(statusCode)));

        var result = await sut.EmbedAsync("hello", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("PROVIDER_ERROR");
    }

    // ---- EmbedAsync: TaskCanceledException ------------------------------------

    [Fact]
    public async Task EmbedAsync_TimeoutWithExternalTokenNotFired_ReturnsNetworkTimeout()
    {
        var sut = BuildSut(ValidOptions(), new ThrowingHttpMessageHandler(new TaskCanceledException("timeout")));

        var result = await sut.EmbedAsync("hello", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NETWORK_TIMEOUT");
    }

    [Fact]
    public async Task EmbedAsync_TaskCancelledWithTokenFired_ReturnsCancelled()
    {
        using var cts = new CancellationTokenSource();
        var sut = BuildSut(ValidOptions(), new CancelOnSendHandler(cts));

        var result = await sut.EmbedAsync("hello", cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CANCELLED");
    }

    // ---- EmbedBatchAsync: invalid input ---------------------------------------

    [Fact]
    public async Task EmbedBatchAsync_NullTexts_ReturnsInvalidInput()
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await sut.EmbedBatchAsync(null!, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task EmbedBatchAsync_EmptyTexts_ReturnsInvalidInput()
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await sut.EmbedBatchAsync(Array.Empty<string>(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_INPUT");
    }

    // ---- EmbedBatchAsync: missing API key ------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmbedBatchAsync_MissingApiKey_ReturnsInvalidConfiguration(string? apiKey)
    {
        var sut = BuildSut(OptionsWithKey(apiKey), new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await sut.EmbedBatchAsync(new[] { "hello" }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_CONFIGURATION");
    }

    // ---- EmbedBatchAsync: HTTP success ----------------------------------------

    [Fact]
    public async Task EmbedBatchAsync_200Ok_ReturnsVectorsInInputOrder()
    {
        // BatchEmbeddingJson has index 1 before index 0 — code must re-sort
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(HttpClientFactoryHelper.JsonOk(BatchEmbeddingJson)));

        var result = await sut.EmbedBatchAsync(new[] { "first", "second" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].Should().Equal(1.0f, 2.0f, 3.0f); // original index 0
        result.Value[1].Should().Equal(4.0f, 5.0f, 6.0f); // original index 1
    }

    // ---- EmbedBatchAsync: HTTP error codes ------------------------------------

    [Fact]
    public async Task EmbedBatchAsync_401Unauthorized_ReturnsAuthenticationFailed()
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var result = await sut.EmbedBatchAsync(new[] { "hello" }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AUTHENTICATION_FAILED");
    }

    [Fact]
    public async Task EmbedBatchAsync_429TooManyRequests_ReturnsRateLimitExceeded()
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));

        var result = await sut.EmbedBatchAsync(new[] { "hello" }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("RATE_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task EmbedBatchAsync_500InternalServerError_ReturnsProviderError()
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await sut.EmbedBatchAsync(new[] { "hello" }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("PROVIDER_ERROR");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task EmbedBatchAsync_Other5xxStatusCode_ReturnsProviderError(HttpStatusCode statusCode)
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(statusCode)));

        var result = await sut.EmbedBatchAsync(new[] { "hello" }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("PROVIDER_ERROR");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task EmbedBatchAsync_Unexpected4xxStatusCode_ReturnsProviderError(HttpStatusCode statusCode)
    {
        var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(new HttpResponseMessage(statusCode)));

        var result = await sut.EmbedBatchAsync(new[] { "hello" }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("PROVIDER_ERROR");
    }

    // ---- EmbedBatchAsync: TaskCanceledException --------------------------------

    [Fact]
    public async Task EmbedBatchAsync_TimeoutWithExternalTokenNotFired_ReturnsNetworkTimeout()
    {
        var sut = BuildSut(ValidOptions(), new ThrowingHttpMessageHandler(new TaskCanceledException("timeout")));

        var result = await sut.EmbedBatchAsync(new[] { "hello" }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NETWORK_TIMEOUT");
    }

    [Fact]
    public async Task EmbedBatchAsync_TaskCancelledWithTokenFired_ReturnsCancelled()
    {
        using var cts = new CancellationTokenSource();
        var sut = BuildSut(ValidOptions(), new CancelOnSendHandler(cts));

        var result = await sut.EmbedBatchAsync(new[] { "hello" }, cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CANCELLED");
    }

    // ---- Thread safety --------------------------------------------------------

    [Fact]
    public async Task EmbedAsync_ConcurrentCallsOnSameInstance_AllSucceed()
    {
        // Each CreateClient call returns a fresh HttpClient so responses are independent
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>())
               .Returns(_ => new HttpClient(new JsonFactoryHttpMessageHandler(SingleEmbeddingJson)));
        var sut = new OpenAIEmbeddingModel(factory, ValidOptions());

        var tasks = Enumerable.Range(0, 20).Select(i => sut.EmbedAsync($"text {i}", CancellationToken.None));
        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }

    [Fact]
    public async Task EmbedBatchAsync_ConcurrentCallsOnSameInstance_AllSucceed()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>())
               .Returns(_ => new HttpClient(new JsonFactoryHttpMessageHandler(SingleEmbeddingJson)));
        var sut = new OpenAIEmbeddingModel(factory, ValidOptions());

        var tasks = Enumerable.Range(0, 20).Select(i =>
            sut.EmbedBatchAsync(new[] { $"text {i}" }, CancellationToken.None));
        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }
}
