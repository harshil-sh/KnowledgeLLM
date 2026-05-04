using Npgsql;
using Pgvector;
using Pgvector.Npgsql;

namespace KnowledgeLLM.Core.Retrieval;

/// <summary>Npgsql-backed implementation of <see cref="IDbOperations"/>.</summary>
internal sealed class NpgsqlDbOperations : IDbOperations
{
    private readonly NpgsqlDataSource _dataSource;

    internal NpgsqlDbOperations(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <inheritdoc/>
    public async Task InitSchemaAsync(int dimensions, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS chunks (
                id TEXT PRIMARY KEY,
                document_id TEXT NOT NULL,
                content TEXT NOT NULL,
                metadata JSONB,
                embedding vector({dimensions})
            );
            CREATE INDEX IF NOT EXISTS chunks_embedding_idx
                ON chunks USING ivfflat (embedding vector_cosine_ops);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task UpsertRowAsync(
        string id,
        string documentId,
        string content,
        string? metadataJson,
        float[] embedding,
        CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO chunks (id, document_id, content, metadata, embedding)
            VALUES ($1, $2, $3, $4::jsonb, $5)
            ON CONFLICT (id) DO UPDATE SET
                document_id = EXCLUDED.document_id,
                content = EXCLUDED.content,
                metadata = EXCLUDED.metadata,
                embedding = EXCLUDED.embedding
            """;
        cmd.Parameters.AddWithValue(id);
        cmd.Parameters.AddWithValue(documentId);
        cmd.Parameters.AddWithValue(content);
        if (metadataJson is null)
            cmd.Parameters.Add(new NpgsqlParameter { Value = DBNull.Value, DataTypeName = "jsonb" });
        else
            cmd.Parameters.Add(new NpgsqlParameter { Value = metadataJson, DataTypeName = "jsonb" });
        cmd.Parameters.AddWithValue(new Vector(embedding));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<List<SearchRow>> SearchRowsAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // <=> returns cosine distance; 1 - distance = cosine similarity
        cmd.CommandText = """
            SELECT id, document_id, content, metadata,
                   1.0 - (embedding <=> $1) AS score
            FROM chunks
            ORDER BY embedding <=> $1
            LIMIT $2
            """;
        cmd.Parameters.AddWithValue(new Vector(queryEmbedding));
        cmd.Parameters.AddWithValue(topK);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<SearchRow>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new SearchRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                (float)reader.GetDouble(4)));
        }

        return rows;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteRowsByDocumentAsync(string documentId, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM chunks WHERE document_id = $1";
        cmd.Parameters.AddWithValue(documentId);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await _dataSource.DisposeAsync();
}
