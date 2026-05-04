using System.Runtime.CompilerServices;
using System.Text;
using FluentAssertions;
using KnowledgeLLM.Api.Controllers;
using KnowledgeLLM.Core.Pipeline;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace KnowledgeLLM.Core.Tests.Controllers;

public sealed class KnowledgeControllerStreamingTests
{
    private readonly IRagPipeline _pipeline = Substitute.For<IRagPipeline>();
    private readonly KnowledgeController _sut;
    private readonly DefaultHttpContext _httpContext;

    public KnowledgeControllerStreamingTests()
    {
        _httpContext = new DefaultHttpContext();
        _httpContext.Response.Body = new MemoryStream();

        _sut = new KnowledgeController(_pipeline)
        {
            ControllerContext = new ControllerContext { HttpContext = _httpContext }
        };
    }

    private string ReadResponseBody()
    {
        _httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        return new StreamReader(_httpContext.Response.Body, Encoding.UTF8).ReadToEnd();
    }

    /// <summary>Async iterator helper that respects cancellation between tokens.</summary>
    private static async IAsyncEnumerable<string> TokenStream(
        IEnumerable<string> tokens,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var token in tokens)
        {
            ct.ThrowIfCancellationRequested();
            yield return token;
        }
    }

    /// <summary>Async iterator that yields one token then throws <see cref="OperationCanceledException"/>.</summary>
    private static async IAsyncEnumerable<string> CancellingStream(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return "token1";
        await Task.CompletedTask;
        throw new OperationCanceledException("Simulated client disconnect.");
    }

    // --- content-type header ---

    [Fact]
    public async Task AskStreamAsync_SetsSseContentType()
    {
        _pipeline.AskStreamAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                 .Returns(TokenStream(Array.Empty<string>()));

        await _sut.AskStreamAsync(new AskRequest("question?"), CancellationToken.None);

        _httpContext.Response.ContentType.Should().Be("text/event-stream");
    }

    // --- happy path ---

    [Fact]
    public async Task AskStreamAsync_Success_WritesSseEventsAndDone()
    {
        _pipeline.AskStreamAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                 .Returns(TokenStream(new[] { "Hello", " world" }));

        await _sut.AskStreamAsync(new AskRequest("question?"), CancellationToken.None);

        var body = ReadResponseBody();
        body.Should().Contain("data: Hello\n\n");
        body.Should().Contain("data:  world\n\n");
        body.Should().EndWith("data: [DONE]\n\n");
    }

    [Fact]
    public async Task AskStreamAsync_EmptyStream_WritesOnlyDone()
    {
        _pipeline.AskStreamAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                 .Returns(TokenStream(Array.Empty<string>()));

        await _sut.AskStreamAsync(new AskRequest("question?"), CancellationToken.None);

        var body = ReadResponseBody();
        body.Should().Be("data: [DONE]\n\n");
    }

    // --- cancellation / client disconnect ---

    [Fact]
    public async Task AskStreamAsync_PipelineThrowsOperationCancelled_DoesNotWriteDoneToken()
    {
        _pipeline.AskStreamAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                 .Returns(CancellingStream());

        await _sut.AskStreamAsync(new AskRequest("question?"), CancellationToken.None);

        var body = ReadResponseBody();
        body.Should().NotContain("[DONE]");
    }

    [Fact]
    public async Task AskStreamAsync_PipelineThrowsOperationCancelled_DoesNotThrow()
    {
        _pipeline.AskStreamAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                 .Returns(CancellingStream());

        var act = async () => await _sut.AskStreamAsync(new AskRequest("question?"), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
