using FluentAssertions;
using KnowledgeLLM.Core.Chunking;
using KnowledgeLLM.Core.Documents;
using Xunit;

namespace KnowledgeLLM.Core.Tests.Chunking;

public sealed class SlidingWindowChunkerTests
{
    // --- InvalidConfiguration ---

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    public async Task ChunkAsync_InvalidChunkSize_ReturnsInvalidConfiguration(int chunkSize, int overlap)
    {
        var sut = new SlidingWindowChunker(chunkSize, overlap);
        var doc = new Document("id", "some content");

        var result = await sut.ChunkAsync(doc, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("InvalidConfiguration");
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(10, 15)]
    public async Task ChunkAsync_OverlapGreaterThanOrEqualToChunkSize_ReturnsInvalidConfiguration(int chunkSize, int overlap)
    {
        var sut = new SlidingWindowChunker(chunkSize, overlap);
        var doc = new Document("id", "some content");

        var result = await sut.ChunkAsync(doc, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("InvalidConfiguration");
    }

    // --- InvalidInput ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ChunkAsync_EmptyOrWhitespaceContent_ReturnsInvalidInput(string content)
    {
        var sut = new SlidingWindowChunker();
        var doc = new Document("id", content);

        var result = await sut.ChunkAsync(doc, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task ChunkAsync_NullDocument_ReturnsInvalidInput()
    {
        var sut = new SlidingWindowChunker();

        var result = await sut.ChunkAsync(null!, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_INPUT");
    }

    // --- Happy path ---

    [Fact]
    public async Task ChunkAsync_ContentShorterThanChunkSize_ReturnsSingleChunk()
    {
        var sut = new SlidingWindowChunker(chunkSize: 500, overlap: 100);
        var doc = new Document("doc1", "short content");

        var result = await sut.ChunkAsync(doc, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Content.Should().Be("short content");
    }

    [Fact]
    public async Task ChunkAsync_LongContent_ProducesMultipleChunks()
    {
        var content = new string('x', 1000);
        var sut = new SlidingWindowChunker(chunkSize: 300, overlap: 50);
        var doc = new Document("doc1", content);

        var result = await sut.ChunkAsync(doc, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task ChunkAsync_OverlapApplied_ConsecutiveChunksShareContent()
    {
        var content = new string('a', 100) + new string('b', 100) + new string('c', 100);
        var sut = new SlidingWindowChunker(chunkSize: 150, overlap: 50);
        var doc = new Document("doc1", content);

        var result = await sut.ChunkAsync(doc, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var chunks = result.Value;
        for (var i = 1; i < chunks.Count; i++)
        {
            var prevTail = chunks[i - 1].Content[^50..];
            var currHead = chunks[i].Content[..Math.Min(50, chunks[i].Content.Length)];
            currHead.Should().Be(prevTail);
        }
    }

    [Fact]
    public async Task ChunkAsync_ChunkIds_HaveExpectedFormat()
    {
        var content = new string('x', 600);
        var sut = new SlidingWindowChunker(chunkSize: 300, overlap: 50);
        var doc = new Document("myDoc", content);

        var result = await sut.ChunkAsync(doc, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(c => c.Id).Should().Contain("myDoc_0");
        result.Value.Select(c => c.Id).Should().Contain("myDoc_1");
    }

    [Fact]
    public async Task ChunkAsync_MetadataInherited_FromSourceDocument()
    {
        var meta = new Dictionary<string, string> { ["key"] = "val" };
        var doc = new Document("doc1", new string('z', 600), meta);
        var sut = new SlidingWindowChunker(chunkSize: 300, overlap: 50);

        var result = await sut.ChunkAsync(doc, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().AllSatisfy(c => c.Metadata.Should().ContainKey("key"));
    }

    // --- Cancellation ---

    [Fact]
    public async Task ChunkAsync_CancelledToken_ReturnsCancelled()
    {
        var content = new string('x', 100_000);
        var sut = new SlidingWindowChunker(chunkSize: 10, overlap: 1);
        var doc = new Document("doc1", content);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await sut.ChunkAsync(doc, cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Cancelled");
    }
}
