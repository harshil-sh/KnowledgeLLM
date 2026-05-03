namespace KnowledgeLLM.Core.Documents;

/// <summary>An immutable document loaded into the pipeline for indexing or querying.</summary>
/// <param name="Id">Unique identifier — typically the file path.</param>
/// <param name="Content">Raw text content of the document.</param>
/// <param name="Metadata">Optional key/value metadata.</param>
public sealed record Document(
    string Id,
    string Content,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    /// <summary>Returns <see cref="Metadata"/> or an empty dictionary when null.</summary>
    public IReadOnlyDictionary<string, string> SafeMetadata =>
        Metadata ?? new Dictionary<string, string>();
}
