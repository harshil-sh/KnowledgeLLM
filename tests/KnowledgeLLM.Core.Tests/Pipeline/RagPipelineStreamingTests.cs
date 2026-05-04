using System.Runtime.CompilerServices;
using FluentAssertions;
using KnowledgeLLM.Core.Chunking;
using KnowledgeLLM.Core.Embeddings;
using KnowledgeLLM.Core.Pipeline;
using KnowledgeLLM.Core.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WeaveLLM.Core.Models;
using Xunit;
using IChatModel = WeaveLLM.Core.Providers.IChatModel;
using LLMMessage = WeaveLLM.Core.Providers.Message;
using LLMOptions = WeaveLLM.Core.Providers.LLMOptions;

namespace KnowledgeLLM.Core.Tests.Pipeline;

public sealed class RagPipelineStreamingTests
{
    private readonly IEmbeddingModel _embedder = Substitute.For<IEmbeddingModel>();
    private readonly IVectorStore _store = Substitute.For<IVectorStore>();
    private readonly IChatModel _chatModel = Substitute.For<IChatModel>();
    private readonly RagPipeline _sut;

    public RagPipelineStreamingTests()
    {
        _sut = new RagPipeline(
            Substitute.For<KnowledgeLLM.Core.Documents.IDocumentLoader>(),
            Substitute.For<KnowledgeLLM.Core.Chunking.ITextChunker>(),
            _embedder,
            _store,
            _chatModel,
            NullLogger<RagPipeline>.Instance);
    }

    private static TextChunk MakeChunk(string docId = "doc1", int idx = 0) =>
        new($"{docId}_{idx}", docId, "chunk content", idx);

    private void SetupSuccessfulEmbedAndSearch()
    {
        var sources = new List<RetrievalResult> { new(MakeChunk(), 0.9f) };
        _embedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(ChainResult<float[]>.Success(new[] { 1f, 0f }));
        _store.SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
              .Returns(ChainResult<IReadOnlyList<RetrievalResult>>.Success(sources));
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

    // --- invalid input ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AskStreamAsync_NullOrWhitespaceQuestion_YieldsErrorToken(string? question)
    {
        var tokens = new List<string>();
        await foreach (var t in _sut.AskStreamAsync(question!, ct: CancellationToken.None))
            tokens.Add(t);

        tokens.Should().HaveCount(1);
        tokens[0].Should().StartWith("[ERROR:InvalidInput]");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AskStreamAsync_InvalidTopK_YieldsErrorToken(int topK)
    {
        var tokens = new List<string>();
        await foreach (var t in _sut.AskStreamAsync("question?", topK, CancellationToken.None))
            tokens.Add(t);

        tokens.Should().HaveCount(1);
        tokens[0].Should().StartWith("[ERROR:InvalidInput]");
    }

    // --- propagates downstream errors as error tokens ---

    [Fact]
    public async Task AskStreamAsync_EmbedFails_YieldsErrorToken()
    {
        _embedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(ChainResult<float[]>.Failure(
                     new WeaveLLMError("embed fail", "PROVIDER_ERROR", null)));

        var tokens = new List<string>();
        await foreach (var t in _sut.AskStreamAsync("question?", ct: CancellationToken.None))
            tokens.Add(t);

        tokens.Should().HaveCount(1);
        tokens[0].Should().StartWith("[ERROR:PROVIDER_ERROR]");
        await _store.DidNotReceiveWithAnyArgs().SearchAsync(default!, default, default);
    }

    [Fact]
    public async Task AskStreamAsync_SearchFails_YieldsErrorToken()
    {
        _embedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(ChainResult<float[]>.Success(new[] { 1f, 0f }));
        _store.SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
              .Returns(ChainResult<IReadOnlyList<RetrievalResult>>.Failure(
                  new WeaveLLMError("search fail", "PROVIDER_ERROR", null)));

        var tokens = new List<string>();
        await foreach (var t in _sut.AskStreamAsync("question?", ct: CancellationToken.None))
            tokens.Add(t);

        tokens.Should().HaveCount(1);
        tokens[0].Should().StartWith("[ERROR:PROVIDER_ERROR]");
    }

    [Fact]
    public async Task AskStreamAsync_NoSources_YieldsNotFoundErrorToken()
    {
        _embedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(ChainResult<float[]>.Success(new[] { 1f, 0f }));
        _store.SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
              .Returns(ChainResult<IReadOnlyList<RetrievalResult>>.Success(
                  new List<RetrievalResult>()));

        var tokens = new List<string>();
        await foreach (var t in _sut.AskStreamAsync("question?", ct: CancellationToken.None))
            tokens.Add(t);

        tokens.Should().HaveCount(1);
        tokens[0].Should().StartWith("[ERROR:NotFound]");
    }

    // --- success: yields all tokens in order ---

    [Fact]
    public async Task AskStreamAsync_Success_YieldsAllTokensInOrder()
    {
        SetupSuccessfulEmbedAndSearch();
        _chatModel.StreamChatAsync(
                Arg.Any<IReadOnlyList<LLMMessage>>(),
                Arg.Any<LLMOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(TokenStream(new[] { "Hello", " world", "!" }));

        var tokens = new List<string>();
        await foreach (var t in _sut.AskStreamAsync("question?", ct: CancellationToken.None))
            tokens.Add(t);

        tokens.Should().Equal("Hello", " world", "!");
        await _embedder.Received(1).EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _store.Received(1).SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // --- cancellation mid-stream ---

    [Fact]
    public async Task AskStreamAsync_CancellationMidStream_StopsYielding()
    {
        SetupSuccessfulEmbedAndSearch();
        _chatModel.StreamChatAsync(
                Arg.Any<IReadOnlyList<LLMMessage>>(),
                Arg.Any<LLMOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(TokenStream(new[] { "token1", "token2", "token3" }));

        using var cts = new CancellationTokenSource();
        var tokens = new List<string>();
        try
        {
            await foreach (var t in _sut.AskStreamAsync("question?", ct: cts.Token))
            {
                tokens.Add(t);
                await cts.CancelAsync(); // cancel after the first token
            }
        }
        catch (OperationCanceledException) { }

        tokens.Should().HaveCount(1);
        tokens[0].Should().Be("token1");
    }
}
