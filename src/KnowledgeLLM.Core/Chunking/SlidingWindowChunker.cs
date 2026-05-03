using KnowledgeLLM.Core.Documents;
using WeaveLLM.Core.Models;

namespace KnowledgeLLM.Core.Chunking;

/// <summary>Splits document text into overlapping fixed-size character windows.</summary>
public sealed class SlidingWindowChunker : ITextChunker
{
    private readonly int _chunkSize;
    private readonly int _overlap;

    /// <summary>Initialises the chunker.</summary>
    /// <param name="chunkSize">Maximum characters per chunk. Default 500.</param>
    /// <param name="overlap">Character overlap between consecutive chunks. Default 100.</param>
    public SlidingWindowChunker(int chunkSize = 500, int overlap = 100)
    {
        _chunkSize = chunkSize;
        _overlap = overlap;
    }

    /// <summary>Splits <paramref name="document"/> into overlapping <see cref="TextChunk"/> windows.</summary>
    /// <param name="document">Source document.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<ChainResult<IReadOnlyList<TextChunk>>> ChunkAsync(Document document, CancellationToken ct)
    {
        if (_chunkSize < 1)
            return Task.FromResult(ChainResult<IReadOnlyList<TextChunk>>.Failure(
                new WeaveLLMError("chunkSize must be >= 1.", "InvalidConfiguration", null)));

        if (_overlap >= _chunkSize)
            return Task.FromResult(ChainResult<IReadOnlyList<TextChunk>>.Failure(
                new WeaveLLMError("overlap must be less than chunkSize.", "InvalidConfiguration", null)));

        if (string.IsNullOrWhiteSpace(document?.Content))
            return Task.FromResult(ChainResult<IReadOnlyList<TextChunk>>.Failure(
                WeaveLLMError.InvalidInput("Document content must not be null or whitespace.")));

        var content = document.Content;
        var step = _chunkSize - _overlap;
        var chunks = new List<TextChunk>();
        var index = 0;

        for (var pos = 0; pos < content.Length; pos += step)
        {
            if (ct.IsCancellationRequested)
                return Task.FromResult(ChainResult<IReadOnlyList<TextChunk>>.Failure(
                    new WeaveLLMError("Operation was cancelled.", "Cancelled", null)));

            var end = Math.Min(pos + _chunkSize, content.Length);
            var window = content[pos..end];

            if (!string.IsNullOrWhiteSpace(window))
            {
                chunks.Add(new TextChunk(
                    Id: $"{document.Id}_{index}",
                    DocumentId: document.Id,
                    Content: window,
                    Index: index,
                    Metadata: document.Metadata));
                index++;
            }

            if (end == content.Length)
                break;
        }

        return Task.FromResult(ChainResult<IReadOnlyList<TextChunk>>.Success(chunks.AsReadOnly()));
    }
}
