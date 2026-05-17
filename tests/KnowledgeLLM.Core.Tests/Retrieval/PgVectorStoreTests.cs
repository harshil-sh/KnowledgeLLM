using FluentAssertions;
using KnowledgeLLM.Core.Chunking;
using KnowledgeLLM.Core.Retrieval;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace KnowledgeLLM.Core.Tests.Retrieval;

public sealed class PgVectorStoreTests
{
    private static TextChunk MakeChunk(string docId, int index) =>
        new($"{docId}_{index}", docId, $"content {index}", index);

    private static PgVectorStore BuildSut(IDbOperations? db = null) =>
        new(db ?? Substitute.For<IDbOperations>());

    // ── Invalid inputs ────────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertAsync_CountMismatch_ReturnsInvalidInput()
    {
        var result = await BuildSut().UpsertAsync(
            new[] { MakeChunk("d1", 0) }, Array.Empty<float[]>(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_INPUT");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new float[0])]
    public async Task SearchAsync_EmptyOrNullQueryEmbedding_ReturnsInvalidInput(float[]? query)
    {
        var result = await BuildSut().SearchAsync(query!, 1, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task SearchAsync_ZeroTopK_ReturnsInvalidInput()
    {
        var result = await BuildSut().SearchAsync(new[] { 1f, 0f }, 0, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_INPUT");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeleteByDocumentAsync_NullOrWhitespaceDocumentId_ReturnsInvalidInput(string? documentId)
    {
        var result = await BuildSut().DeleteByDocumentAsync(documentId!, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_INPUT");
    }

    // ── UpsertAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertAsync_ValidTwoChunks_CallsUpsertRowTwiceAndReturnsCount()
    {
        var db = Substitute.For<IDbOperations>();
        var sut = new PgVectorStore(db);
        var chunks = new[] { MakeChunk("doc1", 0), MakeChunk("doc1", 1) };
        var embeddings = new[] { new[] { 1f, 0f }, new[] { 0f, 1f } };

        var result = await sut.UpsertAsync(chunks, embeddings, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
        await db.Received(2).UpsertRowAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<float[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertAsync_EmptyLists_ReturnsZeroWithoutCallingDb()
    {
        var db = Substitute.For<IDbOperations>();
        var sut = new PgVectorStore(db);

        var result = await sut.UpsertAsync(
            Array.Empty<TextChunk>(), Array.Empty<float[]>(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
        await db.DidNotReceive().UpsertRowAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<float[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertAsync_ChunkWithMetadata_SerializesMetadataJson()
    {
        var db = Substitute.For<IDbOperations>();
        var sut = new PgVectorStore(db);
        var metadata = new Dictionary<string, string> { ["source"] = "file.pdf" };
        var chunk = new TextChunk("d1_0", "d1", "text", 0, metadata);

        await sut.UpsertAsync(new[] { chunk }, new[] { new[] { 1f } }, CancellationToken.None);

        await db.Received(1).UpsertRowAsync(
            "d1_0", "d1", "text",
            Arg.Is<string?>(j => j != null && j.Contains("file.pdf")),
            Arg.Any<float[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertAsync_SchemaInitFails_ReturnsProviderError()
    {
        var db = Substitute.For<IDbOperations>();
        db.InitSchemaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("connection refused"));
        var sut = new PgVectorStore(db);

        var result = await sut.UpsertAsync(
            new[] { MakeChunk("d1", 0) }, new[] { new[] { 1f, 0f } }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("PROVIDER_ERROR");
    }

    [Fact]
    public async Task UpsertAsync_DbThrowsOnRow_ReturnsProviderError()
    {
        var db = Substitute.For<IDbOperations>();
        db.UpsertRowAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<float[]>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("disk full"));
        var sut = new PgVectorStore(db);

        var result = await sut.UpsertAsync(
            new[] { MakeChunk("d1", 0) }, new[] { new[] { 1f, 0f } }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("PROVIDER_ERROR");
    }

    [Fact]
    public async Task UpsertAsync_CalledTwice_InitialisesSchemaOnlyOnce()
    {
        var db = Substitute.For<IDbOperations>();
        var sut = new PgVectorStore(db);
        var chunks = new[] { MakeChunk("d1", 0) };
        var embeddings = new[] { new[] { 1f, 0f } };

        await sut.UpsertAsync(chunks, embeddings, CancellationToken.None);
        await sut.UpsertAsync(chunks, embeddings, CancellationToken.None);

        await db.Received(1).InitSchemaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ── SearchAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_ValidQuery_ReturnsResultsInDescendingScoreOrder()
    {
        var db = Substitute.For<IDbOperations>();
        db.SearchRowsAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<SearchRow>
            {
                new("d1_0", "d1", "best match",  null, 0.99f),
                new("d1_1", "d1", "good match",  null, 0.75f),
                new("d1_2", "d1", "ok match",    null, 0.50f),
            });
        var sut = new PgVectorStore(db);

        var result = await sut.SearchAsync(new[] { 1f, 0f }, 3, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value[0].Score.Should().BeGreaterThan(result.Value[1].Score);
        result.Value[1].Score.Should().BeGreaterThan(result.Value[2].Score);
    }

    [Fact]
    public async Task SearchAsync_EmptyDbResult_ReturnsNotFound()
    {
        var db = Substitute.For<IDbOperations>();
        db.SearchRowsAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<SearchRow>());
        var sut = new PgVectorStore(db);

        var result = await sut.SearchAsync(new[] { 1f, 0f }, 3, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task SearchAsync_RowWithMetadataJson_DeserializesMetadata()
    {
        var db = Substitute.For<IDbOperations>();
        db.SearchRowsAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<SearchRow>
            {
                new("d1_0", "d1", "content", """{"source":"report.pdf"}""", 0.9f)
            });
        var sut = new PgVectorStore(db);

        var result = await sut.SearchAsync(new[] { 1f, 0f }, 1, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Chunk.Metadata.Should()
            .ContainKey("source").WhoseValue.Should().Be("report.pdf");
    }

    [Fact]
    public async Task SearchAsync_RowWithNullMetadataJson_ReturnsNullMetadata()
    {
        var db = Substitute.For<IDbOperations>();
        db.SearchRowsAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<SearchRow> { new("d1_0", "d1", "text", null, 0.8f) });
        var sut = new PgVectorStore(db);

        var result = await sut.SearchAsync(new[] { 1f, 0f }, 1, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Chunk.Metadata.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_DbThrows_ReturnsProviderError()
    {
        var db = Substitute.For<IDbOperations>();
        db.SearchRowsAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("timeout"));
        var sut = new PgVectorStore(db);

        var result = await sut.SearchAsync(new[] { 1f, 0f }, 1, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("PROVIDER_ERROR");
    }

    // ── DeleteByDocumentAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteByDocumentAsync_ExistingDocument_ReturnsDeletedRowCount()
    {
        var db = Substitute.For<IDbOperations>();
        db.DeleteRowsByDocumentAsync("doc1", Arg.Any<CancellationToken>()).Returns(3);
        var sut = new PgVectorStore(db);

        var result = await sut.DeleteByDocumentAsync("doc1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(3);
    }

    [Fact]
    public async Task DeleteByDocumentAsync_NonExistentDocument_ReturnsZero()
    {
        var db = Substitute.For<IDbOperations>();
        db.DeleteRowsByDocumentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(0);
        var sut = new PgVectorStore(db);

        var result = await sut.DeleteByDocumentAsync("missing-doc", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task DeleteByDocumentAsync_DbThrows_ReturnsProviderError()
    {
        var db = Substitute.For<IDbOperations>();
        db.DeleteRowsByDocumentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("db error"));
        var sut = new PgVectorStore(db);

        var result = await sut.DeleteByDocumentAsync("doc1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("PROVIDER_ERROR");
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertAsync_AlreadyCancelledToken_ReturnsCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await BuildSut().UpsertAsync(
            new[] { MakeChunk("d1", 0) }, new[] { new[] { 1f, 0f } }, cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CANCELLED");
    }

    [Fact]
    public async Task SearchAsync_AlreadyCancelledToken_ReturnsCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await BuildSut().SearchAsync(new[] { 1f, 0f }, 1, cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CANCELLED");
    }

    [Fact]
    public async Task DeleteByDocumentAsync_AlreadyCancelledToken_ReturnsCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await BuildSut().DeleteByDocumentAsync("doc1", cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CANCELLED");
    }

    [Fact]
    public async Task UpsertAsync_DbThrowsOperationCancelled_ReturnsCancelled()
    {
        var db = Substitute.For<IDbOperations>();
        db.UpsertRowAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<float[]>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var sut = new PgVectorStore(db);

        var result = await sut.UpsertAsync(
            new[] { MakeChunk("d1", 0) }, new[] { new[] { 1f, 0f } }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CANCELLED");
    }

    [Fact]
    public async Task SearchAsync_DbThrowsOperationCancelled_ReturnsCancelled()
    {
        var db = Substitute.For<IDbOperations>();
        db.SearchRowsAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var sut = new PgVectorStore(db);

        var result = await sut.SearchAsync(new[] { 1f, 0f }, 1, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CANCELLED");
    }

    [Fact]
    public async Task DeleteByDocumentAsync_DbThrowsOperationCancelled_ReturnsCancelled()
    {
        var db = Substitute.For<IDbOperations>();
        db.DeleteRowsByDocumentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var sut = new PgVectorStore(db);

        var result = await sut.DeleteByDocumentAsync("doc1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CANCELLED");
    }
}
