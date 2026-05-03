using KnowledgeLLM.Core.Documents;
using WeaveLLM.Core.Models;

namespace KnowledgeLLM.Core.Chunking;

/// <summary>Splits a document into chunks suitable for embedding.</summary>
public interface ITextChunker
{
    /// <summary>Splits <paramref name="document"/> into a list of <see cref="TextChunk"/> values.</summary>
    /// <param name="document">Source document.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ChainResult<IReadOnlyList<TextChunk>>> ChunkAsync(Document document, CancellationToken ct);
}
