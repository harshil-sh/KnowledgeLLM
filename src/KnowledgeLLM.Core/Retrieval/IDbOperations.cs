namespace KnowledgeLLM.Core.Retrieval;

/// <summary>
/// Internal abstraction over raw Npgsql operations.
/// Enables unit-testing <see cref="PgVectorStore"/> without a live database.
/// </summary>
internal interface IDbOperations : IAsyncDisposable
{
    /// <summary>Creates the <c>chunks</c> table and ivfflat index if they do not already exist.</summary>
    Task InitSchemaAsync(int dimensions, CancellationToken ct);

    /// <summary>Inserts or replaces a single chunk row.</summary>
    Task UpsertRowAsync(
        string id,
        string documentId,
        string content,
        string? metadataJson,
        float[] embedding,
        CancellationToken ct);

    /// <summary>Returns rows ordered by ascending cosine distance (i.e. descending similarity).</summary>
    Task<List<SearchRow>> SearchRowsAsync(float[] queryEmbedding, int topK, CancellationToken ct);

    /// <summary>Deletes all rows for the given document and returns the number of rows deleted.</summary>
    Task<int> DeleteRowsByDocumentAsync(string documentId, CancellationToken ct);
}

/// <summary>Raw row returned by a vector search query.</summary>
internal record SearchRow(
    string Id,
    string DocumentId,
    string Content,
    string? MetadataJson,
    float Score);
