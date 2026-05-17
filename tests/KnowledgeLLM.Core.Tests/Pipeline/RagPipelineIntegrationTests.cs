using FluentAssertions;
using KnowledgeLLM.Core.Chunking;
using KnowledgeLLM.Core.Documents;
using KnowledgeLLM.Core.Pipeline;
using KnowledgeLLM.Core.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Diagnostics;
using WeaveLLM.Core.Models;
using IChatModel = WeaveLLM.Core.Providers.IChatModel;
using IEmbeddingModel = KnowledgeLLM.Core.Embeddings.IEmbeddingModel;
using LLMMessage = WeaveLLM.Core.Models.Message;
using LLMOptions = WeaveLLM.Core.Models.LLMOptions;
using MessageRole = WeaveLLM.Core.Models.Role;
using Xunit;

namespace KnowledgeLLM.Core.Tests.Pipeline;

public sealed class RagPipelineIntegrationTests
{
    [Fact]
    public async Task IndexThenAsk_WithMockedEmbeddingsAndChat_ReturnsAnswer()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        try
        {
            await File.WriteAllTextAsync(tempFile, "The sky is blue. Water is wet. Fire is hot.");

            var loader = new PlainTextDocumentLoader();
            var chunker = new SlidingWindowChunker(chunkSize: 200, overlap: 50);
            var vectorStore = new InMemoryVectorStore();

            var embeddingModel = Substitute.For<IEmbeddingModel>();
            embeddingModel.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                          .Returns(ChainResult<float[]>.Success(new float[] { 0.1f, 0.5f, 0.9f }));
            embeddingModel.EmbedBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                          .Returns(ci =>
                          {
                              var texts = ci.ArgAt<IReadOnlyList<string>>(0);
                              IReadOnlyList<float[]> vectors = texts.Select(_ => new float[] { 0.1f, 0.5f, 0.9f }).ToList();
                              return ChainResult<IReadOnlyList<float[]>>.Success(vectors);
                          });

            var chatClient = Substitute.For<IChatModel>();
            chatClient.ChatAsync(Arg.Any<IReadOnlyList<LLMMessage>>(), Arg.Any<LLMOptions>(), Arg.Any<CancellationToken>())
                      .Returns(ChainResult<ChatResponse>.Success(new ChatResponse { Content = "This is a test answer." }));

            var pipeline = new RagPipeline(
                loader, chunker, embeddingModel, vectorStore, chatClient,
                NullLogger<RagPipeline>.Instance, new ActivitySource("test"));

            var indexResult = await pipeline.IndexAsync(tempFile, CancellationToken.None);
            indexResult.IsSuccess.Should().BeTrue();
            indexResult.Value.Should().BeGreaterThan(0);

            var askResult = await pipeline.AskAsync("What colour is the sky?", topK: 3, CancellationToken.None);
            askResult.IsSuccess.Should().BeTrue();
            askResult.Value.Answer.Should().Be("This is a test answer.");
            askResult.Value.Sources.Count.Should().BeGreaterThan(0);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
