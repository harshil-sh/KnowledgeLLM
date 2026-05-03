using System.Collections.Concurrent;
using KnowledgeLLM.Core.Chunking;
using WeaveLLM.Core.Models;

namespace KnowledgeLLM.Core.Retrieval;

/// <summary>Thread-safe in-memory vector store backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>.</summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, (TextChunk Chunk, float[] Embedding)> _store = new();

    /// <summary>Upserts all chunk/embedding pairs and returns the number stored.</summary>
    /// <param name="chunks">Chunks to store.</param>
    /// <param name="embeddings">Corresponding embeddings — must have the same count as <paramref name="chunks"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<ChainResult<int>> UpsertAsync(
        IReadOnlyList<TextChunk> chunks,
        IReadOnlyList<float[]> embeddings,
        CancellationToken ct)
    {
        if (chunks.Count != embeddings.Count)
            return Task.FromResult(ChainResult<int>.Failure(
                WeaveLLMError.InvalidInput(
                    $"chunks.Count ({chunks.Count}) must equal embeddings.Count ({embeddings.Count}).")));

        for (var i = 0; i < chunks.Count; i++)
        {
            if (ct.IsCancellationRequested)
                return Task.FromResult(ChainResult<int>.Failure(
                    new WeaveLLMError("Operation was cancelled.", "Cancelled", null)));

            _store[chunks[i].Id] = (chunks[i], embeddings[i]);
        }

        return Task.FromResult(ChainResult<int>.Success(chunks.Count));
    }

    /// <summary>
    /// Returns the top-<paramref name="topK"/> results ordered by descending cosine similarity.
    /// Returns <c>NotFound</c> when the store is empty.
    /// </summary>
    /// <param name="queryEmbedding">Query vector.</param>
    /// <param name="topK">Maximum result count.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<ChainResult<IReadOnlyList<RetrievalResult>>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken ct)
    {
        if (queryEmbedding is not { Length: > 0 })
            return Task.FromResult(ChainResult<IReadOnlyList<RetrievalResult>>.Failure(
                WeaveLLMError.InvalidInput("queryEmbedding must not be null or empty.")));

        if (_store.IsEmpty)
            return Task.FromResult(ChainResult<IReadOnlyList<RetrievalResult>>.Failure(
                new WeaveLLMError("The vector store is empty.", "NotFound", null)));

        var results = _store.Values
            .Select(entry => new RetrievalResult(entry.Chunk, CosineSimilarity(queryEmbedding, entry.Embedding)))
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();

        return Task.FromResult(
            ChainResult<IReadOnlyList<RetrievalResult>>.Success(results.AsReadOnly()));
    }

    /// <summary>Removes all entries whose <see cref="TextChunk.DocumentId"/> matches <paramref name="documentId"/>.</summary>
    /// <param name="documentId">Source document ID.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<ChainResult<int>> DeleteByDocumentAsync(string documentId, CancellationToken ct)
    {
        var keysToRemove = _store
            .Where(kv => kv.Value.Chunk.DocumentId == documentId)
            .Select(kv => kv.Key)
            .ToList();

        var removed = 0;
        foreach (var key in keysToRemove)
        {
            if (_store.TryRemove(key, out _))
                removed++;
        }

        return Task.FromResult(ChainResult<int>.Success(removed));
    }

    /// <summary>Cosine similarity: dot(a, b) / (|a| * |b|). Returns 0 when either vector has zero magnitude.</summary>
    private static float CosineSimilarity(float[] a, float[] b)
    {
        var dot = 0.0;
        var magA = 0.0;
        var magB = 0.0;
        var len = Math.Min(a.Length, b.Length);

        for (var i = 0; i < len; i++)
        {
            dot += (double)a[i] * b[i];
            magA += (double)a[i] * a[i];
            magB += (double)b[i] * b[i];
        }

        var denom = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denom == 0.0 ? 0f : (float)(dot / denom);
    }
}
