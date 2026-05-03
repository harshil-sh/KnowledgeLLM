using FluentAssertions;
using KnowledgeLLM.Core.Chunking;
using KnowledgeLLM.Core.Retrieval;
using Xunit;

namespace KnowledgeLLM.Core.Tests.Retrieval;

public sealed class InMemoryVectorStoreTests
{
    private static TextChunk MakeChunk(string docId, int index) =>
        new($"{docId}_{index}", docId, $"content {index}", index);

    // --- UpsertAsync ---

    [Fact]
    public async Task UpsertAsync_CountMismatch_ReturnsInvalidInput()
    {
        var sut = new InMemoryVectorStore();
        var chunks = new[] { MakeChunk("d1", 0) };
        var embeddings = Array.Empty<float[]>();

        var result = await sut.UpsertAsync(chunks, embeddings, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task UpsertAsync_ValidData_ReturnsStoredCount()
    {
        var sut = new InMemoryVectorStore();
        var chunks = new[] { MakeChunk("d1", 0), MakeChunk("d1", 1) };
        var embeddings = new[] { new[] { 1f, 0f }, new[] { 0f, 1f } };

        var result = await sut.UpsertAsync(chunks, embeddings, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
    }

    [Fact]
    public async Task UpsertAsync_CalledTwice_UpsertsByChunkId()
    {
        var sut = new InMemoryVectorStore();
        var chunk = MakeChunk("d1", 0);
        await sut.UpsertAsync(new[] { chunk }, new[] { new[] { 1f, 0f } }, CancellationToken.None);

        var result = await sut.UpsertAsync(new[] { chunk }, new[] { new[] { 0f, 1f } }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }

    // --- SearchAsync ---

    [Fact]
    public async Task SearchAsync_EmptyStore_ReturnsNotFound()
    {
        var sut = new InMemoryVectorStore();

        var result = await sut.SearchAsync(new[] { 1f, 0f }, topK: 3, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NotFound");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new float[0])]
    public async Task SearchAsync_EmptyOrNullQueryEmbedding_ReturnsInvalidInput(float[]? query)
    {
        var sut = new InMemoryVectorStore();
        await sut.UpsertAsync(new[] { MakeChunk("d1", 0) }, new[] { new[] { 1f, 0f } }, CancellationToken.None);

        var result = await sut.SearchAsync(query!, topK: 1, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task SearchAsync_ValidQuery_ReturnsTopKResults()
    {
        var sut = new InMemoryVectorStore();
        var chunks = Enumerable.Range(0, 5).Select(i => MakeChunk("d1", i)).ToArray();
        var embeddings = Enumerable.Range(0, 5).Select(i => new[] { (float)i, 0f }).ToArray();
        await sut.UpsertAsync(chunks, embeddings, CancellationToken.None);

        var result = await sut.SearchAsync(new[] { 4f, 0f }, topK: 2, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchAsync_CosineSimilarity_ReturnsMostSimilarFirst()
    {
        var sut = new InMemoryVectorStore();
        var identical = MakeChunk("d1", 0);
        var orthogonal = MakeChunk("d1", 1);
        await sut.UpsertAsync(
            new[] { identical, orthogonal },
            new[] { new[] { 1f, 0f }, new[] { 0f, 1f } },
            CancellationToken.None);

        var result = await sut.SearchAsync(new[] { 1f, 0f }, topK: 2, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Chunk.Id.Should().Be(identical.Id);
        result.Value[0].Score.Should().BeApproximately(1f, 0.001f);
        result.Value[1].Score.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public async Task SearchAsync_ZeroMagnitudeVectors_ReturnsZeroScore()
    {
        var sut = new InMemoryVectorStore();
        var chunk = MakeChunk("d1", 0);
        await sut.UpsertAsync(new[] { chunk }, new[] { new[] { 0f, 0f } }, CancellationToken.None);

        var result = await sut.SearchAsync(new[] { 1f, 0f }, topK: 1, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Score.Should().Be(0f);
    }

    // --- DeleteByDocumentAsync ---

    [Fact]
    public async Task DeleteByDocumentAsync_ExistingDocument_RemovesAllChunks()
    {
        var sut = new InMemoryVectorStore();
        await sut.UpsertAsync(
            new[] { MakeChunk("doc1", 0), MakeChunk("doc1", 1), MakeChunk("doc2", 0) },
            new[] { new[] { 1f, 0f }, new[] { 0f, 1f }, new[] { 0.5f, 0.5f } },
            CancellationToken.None);

        var deleteResult = await sut.DeleteByDocumentAsync("doc1", CancellationToken.None);
        var searchResult = await sut.SearchAsync(new[] { 1f, 0f }, topK: 10, CancellationToken.None);

        deleteResult.IsSuccess.Should().BeTrue();
        deleteResult.Value.Should().Be(2);
        searchResult.IsSuccess.Should().BeTrue();
        searchResult.Value.Should().AllSatisfy(r => r.Chunk.DocumentId.Should().Be("doc2"));
    }

    [Fact]
    public async Task DeleteByDocumentAsync_NonExistentDocument_ReturnsZero()
    {
        var sut = new InMemoryVectorStore();
        await sut.UpsertAsync(new[] { MakeChunk("d1", 0) }, new[] { new[] { 1f, 0f } }, CancellationToken.None);

        var result = await sut.DeleteByDocumentAsync("no-such-doc", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    // --- Concurrency ---

    [Fact]
    public async Task UpsertAsync_ConcurrentWrites_AllChunksStored()
    {
        var sut = new InMemoryVectorStore();
        var tasks = Enumerable.Range(0, 20).Select(i =>
            sut.UpsertAsync(
                new[] { MakeChunk($"doc{i}", 0) },
                new[] { new[] { (float)i, 0f } },
                CancellationToken.None));

        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());

        var search = await sut.SearchAsync(new[] { 1f, 0f }, topK: 100, CancellationToken.None);
        search.Value.Should().HaveCount(20);
    }
}
