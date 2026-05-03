using WeaveLLM.Core.Models;

namespace KnowledgeLLM.Core.Embeddings;

/// <summary>Generates vector embeddings for text.</summary>
public interface IEmbeddingModel
{
    /// <summary>Dimensionality of the embedding vectors produced by this model.</summary>
    int Dimensions { get; }

    /// <summary>Embeds a single text string into a float vector.</summary>
    /// <param name="text">Input text.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ChainResult<float[]>> EmbedAsync(string text, CancellationToken ct);

    /// <summary>Embeds a batch of text strings into float vectors.</summary>
    /// <param name="texts">Input texts.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ChainResult<IReadOnlyList<float[]>>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct);
}
