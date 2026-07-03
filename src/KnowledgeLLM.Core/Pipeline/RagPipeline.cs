using System.Diagnostics;
using System.Runtime.CompilerServices;
using KnowledgeLLM.Core.Chunking;
using KnowledgeLLM.Core.Documents;
using KnowledgeLLM.Core.Retrieval;
using Microsoft.Extensions.Logging;
using WeaveLLM.Core.Models;
using IChatModel = WeaveLLM.Core.Providers.IChatModel;
using IEmbeddingModel = KnowledgeLLM.Core.Embeddings.IEmbeddingModel;
using LLMMessage = WeaveLLM.Core.Models.Message;
using LLMOptions = WeaveLLM.Core.Models.LLMOptions;

namespace KnowledgeLLM.Core.Pipeline;

/// <summary>Orchestrates document loading, chunking, embedding, indexing, and retrieval-augmented answering.</summary>
public sealed class RagPipeline : IRagPipeline
{
    private readonly IDocumentLoader _loader;
    private readonly ITextChunker _chunker;
    private readonly IEmbeddingModel _embeddingModel;
    private readonly IVectorStore _vectorStore;
    private readonly IChatModel _chatModel;
    private readonly ILogger<RagPipeline> _logger;
    private readonly ActivitySource _activitySource;
    private readonly KnowledgeLlmMetrics _metrics;

    /// <summary>Initialises the pipeline with all required dependencies.</summary>
    public RagPipeline(
        IDocumentLoader loader,
        ITextChunker chunker,
        IEmbeddingModel embeddingModel,
        IVectorStore vectorStore,
        IChatModel chatModel,
        ILogger<RagPipeline> logger,
        ActivitySource activitySource,
        KnowledgeLlmMetrics metrics)
    {
        _loader = loader;
        _chunker = chunker;
        _embeddingModel = embeddingModel;
        _vectorStore = vectorStore;
        _chatModel = chatModel;
        _logger = logger;
        _activitySource = activitySource;
        _metrics = metrics;
    }

    /// <summary>
    /// Indexes all documents at <paramref name="source"/>: Load → Chunk → EmbedBatch → Upsert.
    /// Short-circuits on the first failure and returns the propagated error.
    /// </summary>
    /// <param name="source">File or directory path.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ChainResult<int>> IndexAsync(string source, CancellationToken ct)
    {
        var indexStart = Stopwatch.GetTimestamp();
        var indexSucceeded = false;
        using var indexActivity = _activitySource.StartActivity("knowledge.index");
        indexActivity?.SetTag("document.source", source);

        try
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                _logger.LogWarning("IndexAsync called with null or whitespace source.");
                var err = WeaveLLMError.InvalidInput("source must not be null or whitespace.");
                MarkError(indexActivity, err.Code, err.Message);
                return ChainResult<int>.Failure(err);
            }

            _logger.LogInformation("Loading documents from {Source}", source);
            ChainResult<IReadOnlyList<Document>> loadResult;
            using (var loadActivity = _activitySource.StartActivity("knowledge.load"))
            {
                loadResult = await _loader.LoadAsync(source, ct);
                if (!loadResult.IsSuccess)
                {
                    _logger.LogError("Load failed [{Code}]: {Message}", loadResult.Error.Code, loadResult.Error.Message);
                    MarkError(loadActivity, loadResult.Error.Code, loadResult.Error.Message);
                    MarkError(indexActivity, loadResult.Error.Code, loadResult.Error.Message);
                    return ChainResult<int>.Failure(loadResult.Error);
                }
            }

            var totalChunks = 0;

            foreach (var document in loadResult.Value)
            {
                _logger.LogDebug("Chunking document {Id}", document.Id);
                IReadOnlyList<TextChunk> chunks;
                using (var chunkActivity = _activitySource.StartActivity("knowledge.chunk"))
                {
                    chunkActivity?.SetTag("document.id", document.Id);
                    var chunkResult = await _chunker.ChunkAsync(document, ct);
                    if (!chunkResult.IsSuccess)
                    {
                        _logger.LogError("Chunk failed for {Id} [{Code}]: {Message}", document.Id, chunkResult.Error.Code, chunkResult.Error.Message);
                        MarkError(chunkActivity, chunkResult.Error.Code, chunkResult.Error.Message);
                        MarkError(indexActivity, chunkResult.Error.Code, chunkResult.Error.Message);
                        return ChainResult<int>.Failure(chunkResult.Error);
                    }

                    chunks = chunkResult.Value;
                }

                var texts = chunks.Select(c => c.Content).ToList();

                _logger.LogDebug("Embedding {Count} chunks for {Id}", chunks.Count, document.Id);
                IReadOnlyList<float[]> embeddings;
                using (var embedActivity = _activitySource.StartActivity("knowledge.embed"))
                {
                    embedActivity?.SetTag("document.id", document.Id);
                    embedActivity?.SetTag("chunks.count", chunks.Count);
                    var embedResult = await _embeddingModel.EmbedBatchAsync(texts, ct);
                    if (!embedResult.IsSuccess)
                    {
                        _logger.LogError("Embed failed for {Id} [{Code}]: {Message}", document.Id, embedResult.Error.Code, embedResult.Error.Message);
                        MarkError(embedActivity, embedResult.Error.Code, embedResult.Error.Message);
                        MarkError(indexActivity, embedResult.Error.Code, embedResult.Error.Message);
                        return ChainResult<int>.Failure(embedResult.Error);
                    }

                    embeddings = embedResult.Value;
                }

                using (var upsertActivity = _activitySource.StartActivity("knowledge.upsert"))
                {
                    upsertActivity?.SetTag("document.id", document.Id);
                    upsertActivity?.SetTag("chunks.count", chunks.Count);
                    var upsertResult = await _vectorStore.UpsertAsync(chunks, embeddings, ct);
                    if (!upsertResult.IsSuccess)
                    {
                        _logger.LogError("Upsert failed for {Id} [{Code}]: {Message}", document.Id, upsertResult.Error.Code, upsertResult.Error.Message);
                        MarkError(upsertActivity, upsertResult.Error.Code, upsertResult.Error.Message);
                        MarkError(indexActivity, upsertResult.Error.Code, upsertResult.Error.Message);
                        return ChainResult<int>.Failure(upsertResult.Error);
                    }

                    totalChunks += upsertResult.Value;
                }
            }

            indexActivity?.SetTag("chunks.count", totalChunks);
            indexSucceeded = true;
            _logger.LogInformation("Indexed {Total} chunks from {Source}", totalChunks, source);
            return ChainResult<int>.Success(totalChunks);
        }
        finally
        {
            _metrics.RecordIndexingLatency(indexStart, indexSucceeded);
        }
    }

    /// <summary>
    /// Answers <paramref name="question"/> via: Embed → Search → BuildPrompt → ChatAsync.
    /// Short-circuits on the first failure and returns the propagated error.
    /// </summary>
    /// <param name="question">User question.</param>
    /// <param name="topK">Number of chunks to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ChainResult<RagAnswer>> AskAsync(string question, int topK, CancellationToken ct)
    {
        using var askActivity = _activitySource.StartActivity("knowledge.ask");
        askActivity?.SetTag("topk", topK);

        if (string.IsNullOrWhiteSpace(question))
        {
            _logger.LogWarning("AskAsync called with null or whitespace question.");
            var err = WeaveLLMError.InvalidInput("question must not be null or whitespace.");
            MarkError(askActivity, err.Code, err.Message);
            return ChainResult<RagAnswer>.Failure(err);
        }

        if (topK < 1)
        {
            _logger.LogWarning("AskAsync called with topK={TopK}", topK);
            var err = WeaveLLMError.InvalidInput("topK must be >= 1.");
            MarkError(askActivity, err.Code, err.Message);
            return ChainResult<RagAnswer>.Failure(err);
        }

        IReadOnlyList<RetrievalResult> sources = Array.Empty<RetrievalResult>();
        var retrievalStart = Stopwatch.GetTimestamp();
        var retrievalSucceeded = false;
        try
        {
            float[] queryEmbedding;
            using (var embedActivity = _activitySource.StartActivity("knowledge.embed_question"))
            {
                embedActivity?.SetTag("topk", topK);

                _logger.LogInformation("Embedding question for retrieval");
                var embedResult = await _embeddingModel.EmbedAsync(question, ct);
                if (!embedResult.IsSuccess)
                {
                    _logger.LogError("Question embed failed [{Code}]: {Message}", embedResult.Error.Code, embedResult.Error.Message);
                    MarkError(embedActivity, embedResult.Error.Code, embedResult.Error.Message);
                    MarkError(askActivity, embedResult.Error.Code, embedResult.Error.Message);
                    return ChainResult<RagAnswer>.Failure(embedResult.Error);
                }

                queryEmbedding = embedResult.Value;
            }

            using (var searchActivity = _activitySource.StartActivity("knowledge.search"))
            {
                searchActivity?.SetTag("topk", topK);

                _logger.LogDebug("Searching vector store topK={TopK}", topK);
                var searchResult = await _vectorStore.SearchAsync(queryEmbedding, topK, ct);
                if (!searchResult.IsSuccess)
                {
                    _logger.LogError("Search failed [{Code}]: {Message}", searchResult.Error.Code, searchResult.Error.Message);
                    MarkError(searchActivity, searchResult.Error.Code, searchResult.Error.Message);
                    MarkError(askActivity, searchResult.Error.Code, searchResult.Error.Message);
                    return ChainResult<RagAnswer>.Failure(searchResult.Error);
                }

                sources = searchResult.Value;
                retrievalSucceeded = true;
                searchActivity?.SetTag("sources.count", sources.Count);
            }
        }
        finally
        {
            _metrics.RecordRetrievalLatency(retrievalStart, retrievalSucceeded);
        }

        if (sources.Count == 0)
        {
            _logger.LogWarning("No relevant sources found for question.");
            var err = WeaveLLMError.NotFound("No relevant context found for the question.");
            MarkError(askActivity, err.Code, err.Message);
            return ChainResult<RagAnswer>.Failure(err);
        }

        var prompt = PromptBuilder.BuildRagPrompt(question, sources);
        var messages = new List<LLMMessage> { LLMMessage.User(prompt) };

        using (var completeActivity = _activitySource.StartActivity("knowledge.complete"))
        {
            completeActivity?.SetTag("sources.count", sources.Count);
            var llmStart = Stopwatch.GetTimestamp();
            var chatResult = await _chatModel.ChatAsync(messages, new LLMOptions { MaxTokens = 1024 }, ct);
            if (!chatResult.IsSuccess)
            {
                MarkError(completeActivity, chatResult.Error.Code, chatResult.Error.Message);
                MarkError(askActivity, chatResult.Error.Code, chatResult.Error.Message);
                _metrics.RecordLlmResponseLatency(llmStart, success: false, mode: "complete");
                return ChainResult<RagAnswer>.Failure(chatResult.Error);
            }

            _metrics.RecordLlmResponseLatency(llmStart, success: true, mode: "complete");
            _logger.LogInformation("AskAsync returning {Count} source(s)", sources.Count);
            return ChainResult<RagAnswer>.Success(new RagAnswer(chatResult.Value.Content, sources));
        }
    }

    /// <summary>
    /// Streams answer tokens for <paramref name="question"/> via: Embed → Search → BuildPrompt → StreamChatAsync.
    /// On any pre-stream failure, yields a single <c>[ERROR:Code] message</c> token and stops. Never throws.
    /// </summary>
    /// <param name="question">User question.</param>
    /// <param name="topK">Number of chunks to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    public async IAsyncEnumerable<string> AskStreamAsync(
        string question,
        int topK = 5,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            yield return "[ERROR:INVALID_INPUT] question must not be null or whitespace.";
            yield break;
        }

        if (topK < 1)
        {
            yield return "[ERROR:INVALID_INPUT] topK must be >= 1.";
            yield break;
        }

        using var askActivity = _activitySource.StartActivity("knowledge.ask_stream");
        askActivity?.SetTag("topk", topK);

        var retrievalStart = Stopwatch.GetTimestamp();
        var retrievalSucceeded = false;
        ChainResult<IReadOnlyList<RetrievalResult>>? searchResult = null;
        try
        {
            float[] queryEmbedding;
            using (var embedActivity = _activitySource.StartActivity("knowledge.embed_question"))
            {
                embedActivity?.SetTag("topk", topK);

                _logger.LogInformation("Embedding question for streaming retrieval");
                var embedResult = await _embeddingModel.EmbedAsync(question, ct);
                if (!embedResult.IsSuccess)
                {
                    _logger.LogError("Question embed failed [{Code}]: {Message}", embedResult.Error.Code, embedResult.Error.Message);
                    MarkError(embedActivity, embedResult.Error.Code, embedResult.Error.Message);
                    MarkError(askActivity, embedResult.Error.Code, embedResult.Error.Message);
                    yield return $"[ERROR:{embedResult.Error.Code}] {embedResult.Error.Message}";
                    yield break;
                }

                queryEmbedding = embedResult.Value;
            }

            using (var searchActivity = _activitySource.StartActivity("knowledge.search"))
            {
                searchActivity?.SetTag("topk", topK);

                _logger.LogDebug("Searching vector store topK={TopK}", topK);
                searchResult = await _vectorStore.SearchAsync(queryEmbedding, topK, ct);
                if (!searchResult.IsSuccess)
                {
                    _logger.LogError("Search failed [{Code}]: {Message}", searchResult.Error.Code, searchResult.Error.Message);
                    MarkError(searchActivity, searchResult.Error.Code, searchResult.Error.Message);
                    MarkError(askActivity, searchResult.Error.Code, searchResult.Error.Message);
                    yield return $"[ERROR:{searchResult.Error.Code}] {searchResult.Error.Message}";
                    yield break;
                }

                retrievalSucceeded = true;
                searchActivity?.SetTag("sources.count", searchResult.Value.Count);
            }
        }
        finally
        {
            _metrics.RecordRetrievalLatency(retrievalStart, retrievalSucceeded);
        }

        var sources = searchResult!.Value;
        if (sources.Count == 0)
        {
            _logger.LogWarning("No relevant sources found for streaming question.");
            var err = WeaveLLMError.NotFound("No relevant context found for the question.");
            MarkError(askActivity, err.Code, err.Message);
            yield return "[ERROR:NOT_FOUND] No relevant context found for the question.";
            yield break;
        }

        var prompt = PromptBuilder.BuildRagPrompt(question, sources);
        var messages = new List<LLMMessage> { LLMMessage.User(prompt) };

        _logger.LogInformation("Streaming answer for question");
        var llmStart = Stopwatch.GetTimestamp();
        var llmSucceeded = false;
        using var completeActivity = _activitySource.StartActivity("knowledge.complete_stream");
        completeActivity?.SetTag("sources.count", sources.Count);
        try
        {
            await foreach (var result in _chatModel.StreamChatSafeAsync(messages, new LLMOptions { MaxTokens = 1024 }, ct).WithCancellation(ct))
            {
                if (!result.IsSuccess)
                {
                    if (result.Error.Code == "CANCELLED")
                        yield break;
                    _logger.LogError("Streaming chat failed [{Code}]: {Message}", result.Error.Code, result.Error.Message);
                    MarkError(completeActivity, result.Error.Code, result.Error.Message);
                    MarkError(askActivity, result.Error.Code, result.Error.Message);
                    yield return $"[ERROR:{result.Error.Code}] {result.Error.Message}";
                    yield break;
                }

                yield return result.Value;
            }

            llmSucceeded = true;
        }
        finally
        {
            _metrics.RecordLlmResponseLatency(llmStart, llmSucceeded, "stream");
        }
    }

    /// <summary>Marks an activity as failed with the given error code and description.</summary>
    private static void MarkError(Activity? activity, string code, string message)
    {
        activity?.SetStatus(ActivityStatusCode.Error, message);
        activity?.SetTag("error.code", code);
    }
}
