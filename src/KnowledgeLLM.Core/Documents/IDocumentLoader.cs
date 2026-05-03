using WeaveLLM.Core.Models;

namespace KnowledgeLLM.Core.Documents;

/// <summary>Loads documents from a given source path.</summary>
public interface IDocumentLoader
{
    /// <summary>Loads one or more documents from <paramref name="source"/>.</summary>
    /// <param name="source">File path or directory path.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ChainResult<IReadOnlyList<Document>>> LoadAsync(string source, CancellationToken ct);
}
