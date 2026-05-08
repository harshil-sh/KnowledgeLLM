using FluentAssertions;
using KnowledgeLLM.Core.Chunking;
using KnowledgeLLM.Core.Documents;
using KnowledgeLLM.Core.Pipeline;
using KnowledgeLLM.Core.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Diagnostics;
using WeaveLLM.Core.Models;
using IChatModel = WeaveLLM.Core.Providers.IChatModel;
using IEmbeddingModel = KnowledgeLLM.Core.Embeddings.IEmbeddingModel;
using LLMMessage = WeaveLLM.Core.Models.Message;
using LLMOptions = WeaveLLM.Core.Models.LLMOptions;
using MessageRole = WeaveLLM.Core.Models.Role;
using Xunit;

namespace KnowledgeLLM.Core.Tests.Pipeline;

public sealed class RagPipelineTests
{
    private readonly IDocumentLoader _loader = Substitute.For<IDocumentLoader>();
    private readonly ITextChunker _chunker = Substitute.For<ITextChunker>();
    private readonly IEmbeddingModel _embedder = Substitute.For<IEmbeddingModel>();
    private readonly IVectorStore _store = Substitute.For<IVectorStore>();
    private readonly IChatModel _chatClient = Substitute.For<IChatModel>();
    private readonly RagPipeline _sut;

    public RagPipelineTests()
    {
        _sut = new RagPipeline(_loader, _chunker, _embedder, _store, _chatClient,
            NullLogger<RagPipeline>.Instance, new ActivitySource("test"));
    }

    // helpers
    private static Document MakeDoc(string id = "doc1") => new(id, "content");
    private static TextChunk MakeChunk(string docId = "doc1", int idx = 0) =>
        new($"{docId}_{idx}", docId, "chunk content", idx);

    // --- IndexAsync invalid input ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IndexAsync_NullOrWhitespaceSource_ReturnsInvalidInput(string? source)
    {
        var result = await _sut.IndexAsync(source!, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_INPUT");
        await _loader.DidNotReceiveWithAnyArgs().LoadAsync(default!, default);
    }

    // --- IndexAsync propagates downstream errors ---

    [Fact]
    public async Task IndexAsync_LoadFails_PropagatesError()
    {
        _loader.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(ChainResult<IReadOnlyList<Document>>.Failure(
                   WeaveLLMError.NotFound("not found")));

        var result = await _sut.IndexAsync("path", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task IndexAsync_ChunkFails_PropagatesError()
    {
        _loader.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(ChainResult<IReadOnlyList<Document>>.Success(
                   new[] { MakeDoc() }));
        _chunker.ChunkAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
                .Returns(ChainResult<IReadOnlyList<TextChunk>>.Failure(
                    WeaveLLMError.InvalidInput("bad doc")));

        var result = await _sut.IndexAsync("path", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task IndexAsync_EmbedFails_PropagatesError()
    {
        _loader.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(ChainResult<IReadOnlyList<Document>>.Success(new[] { MakeDoc() }));
        _chunker.ChunkAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
                .Returns(ChainResult<IReadOnlyList<TextChunk>>.Success(new[] { MakeChunk() }));
        _embedder.EmbedBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                 .Returns(ChainResult<IReadOnlyList<float[]>>.Failure(
                     WeaveLLMError.ProviderError("mock", "embed error")));

        var result = await _sut.IndexAsync("path", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("PROVIDER_ERROR");
    }

    [Fact]
    public async Task IndexAsync_UpsertFails_PropagatesError()
    {
        _loader.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(ChainResult<IReadOnlyList<Document>>.Success(new[] { MakeDoc() }));
        _chunker.ChunkAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
                .Returns(ChainResult<IReadOnlyList<TextChunk>>.Success(new[] { MakeChunk() }));
        _embedder.EmbedBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                 .Returns(ChainResult<IReadOnlyList<float[]>>.Success(new[] { new[] { 1f, 0f } }));
        _store.UpsertAsync(Arg.Any<IReadOnlyList<TextChunk>>(), Arg.Any<IReadOnlyList<float[]>>(), Arg.Any<CancellationToken>())
              .Returns(ChainResult<int>.Failure(WeaveLLMError.ProviderError("mock", "upsert fail")));

        var result = await _sut.IndexAsync("path", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("PROVIDER_ERROR");
    }

    [Fact]
    public async Task IndexAsync_Success_ReturnsTotalChunkCount()
    {
        _loader.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(ChainResult<IReadOnlyList<Document>>.Success(
                   new[] { MakeDoc("d1"), MakeDoc("d2") }));
        _chunker.ChunkAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
                .Returns(ChainResult<IReadOnlyList<TextChunk>>.Success(
                    new[] { MakeChunk(), MakeChunk() }));
        _embedder.EmbedBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                 .Returns(ChainResult<IReadOnlyList<float[]>>.Success(
                     new[] { new[] { 1f }, new[] { 0f } }));
        _store.UpsertAsync(Arg.Any<IReadOnlyList<TextChunk>>(), Arg.Any<IReadOnlyList<float[]>>(), Arg.Any<CancellationToken>())
              .Returns(ChainResult<int>.Success(2));

        var result = await _sut.IndexAsync("path", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(4); // 2 docs × 2 chunks each
    }

    // --- AskAsync invalid input ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task AskAsync_NullOrWhitespaceQuestion_ReturnsInvalidInput(string? question)
    {
        var result = await _sut.AskAsync(question!, 5, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_INPUT");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AskAsync_InvalidTopK_ReturnsInvalidInput(int topK)
    {
        var result = await _sut.AskAsync("question?", topK, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_INPUT");
    }

    // --- AskAsync propagates downstream errors ---

    [Fact]
    public async Task AskAsync_EmbedFails_PropagatesError()
    {
        _embedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(ChainResult<float[]>.Failure(WeaveLLMError.ProviderError("mock", "embed fail")));

        var result = await _sut.AskAsync("question?", 5, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("PROVIDER_ERROR");
    }

    [Fact]
    public async Task AskAsync_SearchFails_PropagatesError()
    {
        _embedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(ChainResult<float[]>.Success(new[] { 1f, 0f }));
        _store.SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
              .Returns(ChainResult<IReadOnlyList<RetrievalResult>>.Failure(
                  WeaveLLMError.NotFound("empty")));

        var result = await _sut.AskAsync("question?", 5, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    // --- AskAsync success ---

    [Fact]
    public async Task AskAsync_Success_ReturnsAnswerWithSources()
    {
        var chunk = MakeChunk();
        var sources = new List<RetrievalResult> { new(chunk, 0.9f) };
        const string expectedAnswer = "The answer is 42.";

        _embedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(ChainResult<float[]>.Success(new[] { 1f, 0f }));
        _store.SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
              .Returns(ChainResult<IReadOnlyList<RetrievalResult>>.Success(sources));
        _chatClient.ChatAsync(Arg.Any<IReadOnlyList<LLMMessage>>(), Arg.Any<LLMOptions>(), Arg.Any<CancellationToken>())
                   .Returns(ChainResult<ChatResponse>.Success(new ChatResponse { Content = expectedAnswer }));

        var result = await _sut.AskAsync("question?", 5, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Sources.Should().HaveCount(1);
        result.Value.Answer.Should().Be(expectedAnswer);
    }
}
