using WeaveLLM.Core.Models;

namespace KnowledgeLLM.Core.Pipeline;

/// <summary>Indexes documents and answers questions using retrieval-augmented generation.</summary>
public interface IRagPipeline
{
    /// <summary>Indexes all documents found at <paramref name="source"/> and returns the total chunks stored.</summary>
    /// <param name="source">File path or directory path.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ChainResult<int>> IndexAsync(string source, CancellationToken ct);

    /// <summary>Answers <paramref name="question"/> by retrieving the top-<paramref name="topK"/> relevant chunks.</summary>
    /// <param name="question">User question.</param>
    /// <param name="topK">Number of chunks to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ChainResult<RagAnswer>> AskAsync(string question, int topK, CancellationToken ct);

    /// <summary>
    /// Streams answer tokens for <paramref name="question"/> by retrieving the top-<paramref name="topK"/> relevant chunks.
    /// Yields response tokens as they arrive. On any pre-stream failure, yields a single error token and stops.
    /// Never throws.
    /// </summary>
    /// <param name="question">User question.</param>
    /// <param name="topK">Number of chunks to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    IAsyncEnumerable<string> AskStreamAsync(string question, int topK = 5, CancellationToken ct = default);
}
