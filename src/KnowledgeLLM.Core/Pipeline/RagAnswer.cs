using KnowledgeLLM.Core.Retrieval;

namespace KnowledgeLLM.Core.Pipeline;

/// <summary>The answer produced by the RAG pipeline.</summary>
/// <param name="Answer">The generated (or context-stub) answer text.</param>
/// <param name="Sources">Retrieved chunks used to ground the answer.</param>
public sealed record RagAnswer(string Answer, IReadOnlyList<RetrievalResult> Sources);
