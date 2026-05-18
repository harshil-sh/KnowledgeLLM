namespace KnowledgeLLM.Core.Configuration;

/// <summary>Root configuration options for KnowledgeLLM, bound from the "KnowledgeLLM" config section.</summary>
public class KnowledgeLLMOptions
{
    /// <summary>The configuration section name used when binding from appsettings.</summary>
    public const string SectionName = "KnowledgeLLM";

    /// <summary>OpenAI API and model settings.</summary>
    public OpenAIOptions OpenAI { get; set; } = new();

    /// <summary>Text chunking settings used by the ingestion pipeline.</summary>
    public ChunkerOptions Chunker { get; set; } = new();

    /// <summary>Optional PostgreSQL/pgvector vector store settings.</summary>
    public PgVectorOptions PgVector { get; set; } = new();

    /// <summary>HTTP API authentication settings.</summary>
    public ApiOptions Api { get; set; } = new();
}

/// <summary>Configuration options for the OpenAI client.</summary>
public class OpenAIOptions
{
    /// <summary>OpenAI API key used to authenticate requests.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Name of the embedding model to use for vector generation.</summary>
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    /// <summary>Name of the chat completion model used for answer generation.</summary>
    public string ChatModel { get; set; } = "gpt-4o-mini";

    /// <summary>Number of dimensions in the embedding vectors produced by <see cref="EmbeddingModel"/>.</summary>
    public int EmbeddingDimensions { get; set; } = 1536;
}

/// <summary>Configuration options for the sliding-window text chunker.</summary>
public class ChunkerOptions
{
    /// <summary>Maximum number of characters in each chunk.</summary>
    public int ChunkSize { get; set; } = 500;

    /// <summary>Number of characters that consecutive chunks share as overlap.</summary>
    public int Overlap { get; set; } = 100;
}

/// <summary>Configuration options for HTTP API authentication.</summary>
public class ApiOptions
{
    /// <summary>
    /// Expected value of the <c>X-Api-Key</c> request header.
    /// When empty, authentication is bypassed entirely (zero-config local dev).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>Configuration options for the PostgreSQL/pgvector vector store.</summary>
public class PgVectorOptions
{
    /// <summary>
    /// When <see langword="true"/>, <see cref="KnowledgeLLM.Core.Retrieval.PgVectorStore"/> is registered
    /// as <c>IVectorStore</c>; otherwise the in-memory store is used.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>PostgreSQL connection string used to connect to the pgvector-enabled database.</summary>
    public string ConnectionString { get; set; } = string.Empty;
}
