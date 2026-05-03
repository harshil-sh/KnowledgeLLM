namespace KnowledgeLLM.Core.Chunking;

/// <summary>An immutable text chunk produced by splitting a document.</summary>
/// <param name="Id">Unique identifier in the format <c>{DocumentId}_{Index}</c>.</param>
/// <param name="DocumentId">ID of the source document.</param>
/// <param name="Content">Text content of this chunk.</param>
/// <param name="Index">Zero-based position of this chunk within the document.</param>
/// <param name="Metadata">Optional key/value metadata inherited from the source document.</param>
public sealed record TextChunk(
    string Id,
    string DocumentId,
    string Content,
    int Index,
    IReadOnlyDictionary<string, string>? Metadata = null);
