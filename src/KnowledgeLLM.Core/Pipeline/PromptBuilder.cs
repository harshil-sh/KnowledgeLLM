using KnowledgeLLM.Core.Retrieval;
using System.Text;

namespace KnowledgeLLM.Core.Pipeline;

/// <summary>Builds RAG prompts from retrieved context and a user question.</summary>
internal static class PromptBuilder
{
    /// <summary>
    /// Builds a RAG prompt that instructs the model to answer using only the provided context.
    /// </summary>
    /// <param name="question">The user's question. Must not be null or whitespace.</param>
    /// <param name="sources">The retrieved chunks to use as context. Must not be null or empty.</param>
    /// <returns>A formatted prompt string ready to send to a chat completion model.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="question"/> is null or whitespace,
    /// or when <paramref name="sources"/> is null or empty.</exception>
    internal static string BuildRagPrompt(string question, IReadOnlyList<RetrievalResult> sources)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question must not be null or whitespace.", nameof(question));
        if (sources is null || sources.Count == 0)
            throw new ArgumentException("Sources must not be null or empty.", nameof(sources));

        var sb = new StringBuilder();
        sb.AppendLine("You are a helpful assistant. Answer the question using ONLY the context");
        sb.AppendLine("provided below. If the answer is not found in the context, say so clearly.");
        sb.AppendLine();
        sb.AppendLine("CONTEXT:");

        for (int i = 0; i < sources.Count; i++)
            sb.AppendLine($"[{i + 1}] {sources[i].Chunk.Content}");

        sb.AppendLine();
        sb.AppendLine($"QUESTION: {question}");
        sb.AppendLine();
        sb.Append("ANSWER:");

        return sb.ToString();
    }
}
