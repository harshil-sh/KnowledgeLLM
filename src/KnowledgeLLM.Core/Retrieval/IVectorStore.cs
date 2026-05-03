using KnowledgeLLM.Core.Chunking;
using WeaveLLM.Core.Models;

namespace KnowledgeLLM.Core.Retrieval;

/// <summary>Stores chunk embeddings and supports similarity search.</summary>
public interface IVectorStore
{
    /// <summary>Upserts chunks and their embeddings; returns the number of entries stored.</summary>
    /// <param name="chunks">Chunks to store.</param>
    /// <param name="embeddings">Corresponding embedding vectors — must be the same length as <paramref name="chunks"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ChainResult<int>> UpsertAsync(IReadOnlyList<TextChunk> chunks, IReadOnlyList<float[]> embeddings, CancellationToken ct);

    /// <summary>Returns the top-<paramref name="topK"/> chunks most similar to <paramref name="queryEmbedding"/>.</summary>
    /// <param name="queryEmbedding">Query vector.</param>
    /// <param name="topK">Maximum number of results.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ChainResult<IReadOnlyList<RetrievalResult>>> SearchAsync(float[] queryEmbedding, int topK, CancellationToken ct);

    /// <summary>Removes all chunks belonging to <paramref name="documentId"/>; returns the number of entries deleted.</summary>
    /// <param name="documentId">Source document ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ChainResult<int>> DeleteByDocumentAsync(string documentId, CancellationToken ct);
}
