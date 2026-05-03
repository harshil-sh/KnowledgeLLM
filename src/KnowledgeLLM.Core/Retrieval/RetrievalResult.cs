using KnowledgeLLM.Core.Chunking;

namespace KnowledgeLLM.Core.Retrieval;

/// <summary>A retrieved chunk paired with its cosine similarity score.</summary>
/// <param name="Chunk">The retrieved text chunk.</param>
/// <param name="Score">Cosine similarity score (0.0–1.0).</param>
public sealed record RetrievalResult(TextChunk Chunk, float Score);
