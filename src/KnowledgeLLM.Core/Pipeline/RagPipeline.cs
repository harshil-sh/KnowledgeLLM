using KnowledgeLLM.Core.Chunking;
using KnowledgeLLM.Core.Documents;
using KnowledgeLLM.Core.Embeddings;
using KnowledgeLLM.Core.Retrieval;
using Microsoft.Extensions.Logging;
using WeaveLLM.Core.Models;

namespace KnowledgeLLM.Core.Pipeline;

/// <summary>Orchestrates document loading, chunking, embedding, indexing, and retrieval-augmented answering.</summary>
public sealed class RagPipeline : IRagPipeline
{
    private readonly IDocumentLoader _loader;
    private readonly ITextChunker _chunker;
    private readonly IEmbeddingModel _embeddingModel;
    private readonly IVectorStore _vectorStore;
    private readonly OpenAIChatClient _chatClient;
    private readonly ILogger<RagPipeline> _logger;

    /// <summary>Initialises the pipeline with all required dependencies.</summary>
    public RagPipeline(
        IDocumentLoader loader,
        ITextChunker chunker,
        IEmbeddingModel embeddingModel,
        IVectorStore vectorStore,
        OpenAIChatClient chatClient,
        ILogger<RagPipeline> logger)
    {
        _loader = loader;
        _chunker = chunker;
        _embeddingModel = embeddingModel;
        _vectorStore = vectorStore;
        _chatClient = chatClient;
        _logger = logger;
    }

    /// <summary>
    /// Indexes all documents at <paramref name="source"/>: Load → Chunk → EmbedBatch → Upsert.
    /// Short-circuits on the first failure and returns the propagated error.
    /// </summary>
    /// <param name="source">File or directory path.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ChainResult<int>> IndexAsync(string source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            _logger.LogWarning("IndexAsync called with null or whitespace source.");
            return ChainResult<int>.Failure(WeaveLLMError.InvalidInput("source must not be null or whitespace."));
        }

        _logger.LogInformation("Loading documents from {Source}", source);
        var loadResult = await _loader.LoadAsync(source, ct);
        if (!loadResult.IsSuccess)
        {
            _logger.LogError("Load failed [{Code}]: {Message}", loadResult.Error.Code, loadResult.Error.Message);
            return ChainResult<int>.Failure(loadResult.Error);
        }

        var totalChunks = 0;

        foreach (var document in loadResult.Value)
        {
            _logger.LogDebug("Chunking document {Id}", document.Id);
            var chunkResult = await _chunker.ChunkAsync(document, ct);
            if (!chunkResult.IsSuccess)
            {
                _logger.LogError("Chunk failed for {Id} [{Code}]: {Message}", document.Id, chunkResult.Error.Code, chunkResult.Error.Message);
                return ChainResult<int>.Failure(chunkResult.Error);
            }

            var chunks = chunkResult.Value;
            var texts = chunks.Select(c => c.Content).ToList();

            _logger.LogDebug("Embedding {Count} chunks for {Id}", chunks.Count, document.Id);
            var embedResult = await _embeddingModel.EmbedBatchAsync(texts, ct);
            if (!embedResult.IsSuccess)
            {
                _logger.LogError("Embed failed for {Id} [{Code}]: {Message}", document.Id, embedResult.Error.Code, embedResult.Error.Message);
                return ChainResult<int>.Failure(embedResult.Error);
            }

            var upsertResult = await _vectorStore.UpsertAsync(chunks, embedResult.Value, ct);
            if (!upsertResult.IsSuccess)
            {
                _logger.LogError("Upsert failed for {Id} [{Code}]: {Message}", document.Id, upsertResult.Error.Code, upsertResult.Error.Message);
                return ChainResult<int>.Failure(upsertResult.Error);
            }

            totalChunks += upsertResult.Value;
        }

        _logger.LogInformation("Indexed {Total} chunks from {Source}", totalChunks, source);
        return ChainResult<int>.Success(totalChunks);
    }

    /// <summary>
    /// Answers <paramref name="question"/> via: Embed → Search → BuildPrompt.
    /// The answer is the numbered context list; LLM completion is a Week-2 TODO.
    /// </summary>
    /// <param name="question">User question.</param>
    /// <param name="topK">Number of chunks to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ChainResult<RagAnswer>> AskAsync(string question, int topK, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            _logger.LogWarning("AskAsync called with null or whitespace question.");
            return ChainResult<RagAnswer>.Failure(WeaveLLMError.InvalidInput("question must not be null or whitespace."));
        }

        if (topK < 1)
        {
            _logger.LogWarning("AskAsync called with topK={TopK}", topK);
            return ChainResult<RagAnswer>.Failure(WeaveLLMError.InvalidInput("topK must be >= 1."));
        }

        _logger.LogInformation("Embedding question for retrieval");
        var embedResult = await _embeddingModel.EmbedAsync(question, ct);
        if (!embedResult.IsSuccess)
        {
            _logger.LogError("Question embed failed [{Code}]: {Message}", embedResult.Error.Code, embedResult.Error.Message);
            return ChainResult<RagAnswer>.Failure(embedResult.Error);
        }

        _logger.LogDebug("Searching vector store topK={TopK}", topK);
        var searchResult = await _vectorStore.SearchAsync(embedResult.Value, topK, ct);
        if (!searchResult.IsSuccess)
        {
            _logger.LogError("Search failed [{Code}]: {Message}", searchResult.Error.Code, searchResult.Error.Message);
            return ChainResult<RagAnswer>.Failure(searchResult.Error);
        }

        var sources = searchResult.Value;
        if (sources.Count == 0)
        {
            _logger.LogWarning("No relevant sources found for question.");
            return ChainResult<RagAnswer>.Failure(
                new WeaveLLMError("No relevant context found for the question.", "NotFound", null));
        }

        var prompt = PromptBuilder.BuildRagPrompt(question, sources);
        var completionResult = await _chatClient.CompleteAsync(prompt, ct);
        if (!completionResult.IsSuccess)
            return ChainResult<RagAnswer>.Failure(completionResult.Error);

        _logger.LogInformation("AskAsync returning {Count} source(s)", sources.Count);
        return ChainResult<RagAnswer>.Success(new RagAnswer(completionResult.Value, sources));
    }
}
