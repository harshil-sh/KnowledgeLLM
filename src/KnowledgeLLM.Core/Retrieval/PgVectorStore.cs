using System.Text.Json;
using KnowledgeLLM.Core.Chunking;
using Npgsql;
using Pgvector.Npgsql;
using WeaveLLM.Core.Models;

namespace KnowledgeLLM.Core.Retrieval;

/// <summary>PostgreSQL/pgvector-backed vector store for persistent chunk storage and cosine-similarity search.</summary>
public sealed class PgVectorStore : IVectorStore, IAsyncDisposable
{
    private readonly IDbOperations _db;
    private readonly int _dimensions;
    private volatile bool _schemaInitialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <summary>Initialises the store with a PostgreSQL connection string and embedding dimensions.</summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <param name="dimensions">Embedding vector dimensions — must match the model used during indexing.</param>
    public PgVectorStore(string connectionString, int dimensions)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("connectionString must not be null or whitespace.", nameof(connectionString));
        if (dimensions <= 0)
            throw new ArgumentException("dimensions must be positive.", nameof(dimensions));

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        _db = new NpgsqlDbOperations(builder.Build());
        _dimensions = dimensions;
    }

    /// <summary>Internal constructor that injects a pre-built <see cref="IDbOperations"/> — for unit testing only.</summary>
    internal PgVectorStore(IDbOperations db, int dimensions = 1536)
    {
        _db = db;
        _dimensions = dimensions;
    }

    /// <summary>Creates the chunks table and ivfflat index on first use; subsequent calls are no-ops.</summary>
    private async Task<ChainResult<bool>> EnsureSchemaAsync(CancellationToken ct)
    {
        if (_schemaInitialized) return ChainResult<bool>.Success(true);

        try
        {
            await _initLock.WaitAsync(ct);
        }
        catch (OperationCanceledException ex)
        {
            return ChainResult<bool>.Failure(WeaveLLMError.Cancelled("Operation was cancelled.", ex));
        }

        try
        {
            if (_schemaInitialized) return ChainResult<bool>.Success(true);

            await _db.InitSchemaAsync(_dimensions, ct);
            _schemaInitialized = true;
            return ChainResult<bool>.Success(true);
        }
        catch (OperationCanceledException ex)
        {
            return ChainResult<bool>.Failure(WeaveLLMError.Cancelled("Operation was cancelled.", ex));
        }
        catch (Exception ex)
        {
            return ChainResult<bool>.Failure(
                WeaveLLMError.ProviderError("PostgreSQL", $"Failed to initialise database schema: {ex.Message}", ex));
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Upserts all chunk/embedding pairs into the database.
    /// On conflict (same <c>id</c>) all columns are overwritten.
    /// </summary>
    /// <param name="chunks">Chunks to store.</param>
    /// <param name="embeddings">Corresponding embedding vectors — must have the same count as <paramref name="chunks"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ChainResult<int>> UpsertAsync(
        IReadOnlyList<TextChunk> chunks,
        IReadOnlyList<float[]> embeddings,
        CancellationToken ct)
    {
        if (chunks.Count != embeddings.Count)
            return ChainResult<int>.Failure(WeaveLLMError.InvalidInput(
                $"chunks.Count ({chunks.Count}) must equal embeddings.Count ({embeddings.Count})."));

        var schemaResult = await EnsureSchemaAsync(ct);
        if (!schemaResult.IsSuccess) return ChainResult<int>.Failure(schemaResult.Error);

        try
        {
            var inserted = 0;
            for (var i = 0; i < chunks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var chunk = chunks[i];
                var metadataJson = chunk.Metadata is not null
                    ? JsonSerializer.Serialize(chunk.Metadata)
                    : null;

                await _db.UpsertRowAsync(chunk.Id, chunk.DocumentId, chunk.Content, metadataJson, embeddings[i], ct);
                inserted++;
            }

            return ChainResult<int>.Success(inserted);
        }
        catch (OperationCanceledException ex)
        {
            return ChainResult<int>.Failure(WeaveLLMError.Cancelled("Operation was cancelled.", ex));
        }
        catch (Exception ex)
        {
            return ChainResult<int>.Failure(
                WeaveLLMError.ProviderError("PostgreSQL", $"Database error during upsert: {ex.Message}", ex));
        }
    }

    /// <summary>
    /// Returns the top-<paramref name="topK"/> chunks most similar to <paramref name="queryEmbedding"/>,
    /// ordered by descending cosine similarity.
    /// Returns <c>NotFound</c> when the store contains no chunks.
    /// </summary>
    /// <param name="queryEmbedding">Query vector.</param>
    /// <param name="topK">Maximum number of results.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ChainResult<IReadOnlyList<RetrievalResult>>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken ct)
    {
        if (queryEmbedding is not { Length: > 0 })
            return ChainResult<IReadOnlyList<RetrievalResult>>.Failure(
                WeaveLLMError.InvalidInput("queryEmbedding must not be null or empty."));
        if (topK <= 0)
            return ChainResult<IReadOnlyList<RetrievalResult>>.Failure(
                WeaveLLMError.InvalidInput("topK must be positive."));

        var schemaResult = await EnsureSchemaAsync(ct);
        if (!schemaResult.IsSuccess)
            return ChainResult<IReadOnlyList<RetrievalResult>>.Failure(schemaResult.Error);

        try
        {
            var rows = await _db.SearchRowsAsync(queryEmbedding, topK, ct);

            if (rows.Count == 0)
                return ChainResult<IReadOnlyList<RetrievalResult>>.Failure(
                    WeaveLLMError.NotFound("The vector store contains no chunks."));

            var results = rows.Select(row =>
            {
                IReadOnlyDictionary<string, string>? metadata = null;
                if (row.MetadataJson is not null)
                    metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(row.MetadataJson);
                return new RetrievalResult(new TextChunk(row.Id, row.DocumentId, row.Content, 0, metadata), row.Score);
            }).ToList();

            return ChainResult<IReadOnlyList<RetrievalResult>>.Success(results.AsReadOnly());
        }
        catch (OperationCanceledException ex)
        {
            return ChainResult<IReadOnlyList<RetrievalResult>>.Failure(
                WeaveLLMError.Cancelled("Operation was cancelled.", ex));
        }
        catch (Exception ex)
        {
            return ChainResult<IReadOnlyList<RetrievalResult>>.Failure(
                WeaveLLMError.ProviderError("PostgreSQL", $"Database error during search: {ex.Message}", ex));
        }
    }

    /// <summary>Removes all chunks belonging to <paramref name="documentId"/>; returns the number of rows deleted.</summary>
    /// <param name="documentId">Source document ID.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ChainResult<int>> DeleteByDocumentAsync(string documentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return ChainResult<int>.Failure(WeaveLLMError.InvalidInput("documentId must not be null or whitespace."));

        var schemaResult = await EnsureSchemaAsync(ct);
        if (!schemaResult.IsSuccess) return ChainResult<int>.Failure(schemaResult.Error);

        try
        {
            var deleted = await _db.DeleteRowsByDocumentAsync(documentId, ct);
            return ChainResult<int>.Success(deleted);
        }
        catch (OperationCanceledException ex)
        {
            return ChainResult<int>.Failure(WeaveLLMError.Cancelled("Operation was cancelled.", ex));
        }
        catch (Exception ex)
        {
            return ChainResult<int>.Failure(
                WeaveLLMError.ProviderError("PostgreSQL", $"Database error during delete: {ex.Message}", ex));
        }
    }

    /// <summary>Disposes the underlying database operations and semaphore.</summary>
    public async ValueTask DisposeAsync()
    {
        _initLock.Dispose();
        await _db.DisposeAsync();
    }
}
