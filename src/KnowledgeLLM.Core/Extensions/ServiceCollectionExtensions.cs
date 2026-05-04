using System.Net.Http.Headers;
using KnowledgeLLM.Core.Chunking;
using KnowledgeLLM.Core.Configuration;
using KnowledgeLLM.Core.Documents;
using KnowledgeLLM.Core.Embeddings;
using KnowledgeLLM.Core.Pipeline;
using KnowledgeLLM.Core.Retrieval;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using IChatModel = WeaveLLM.Core.Providers.IChatModel;
using WeaveLLM.Providers.OpenAI;

namespace KnowledgeLLM.Core.Extensions;

/// <summary>Extension methods for registering KnowledgeLLM services with the DI container.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all KnowledgeLLM Core services, including configuration, named HTTP clients,
    /// embedding model, chat client, vector store, and pipeline.
    /// When <see cref="PgVectorOptions.Enabled"/> is <see langword="true"/> in configuration,
    /// <see cref="PgVectorStore"/> is registered as <c>IVectorStore</c>; otherwise
    /// <see cref="InMemoryVectorStore"/> is used.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration used to bind <see cref="KnowledgeLLMOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, for call chaining.</returns>
    public static IServiceCollection AddKnowledgeLLM(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<KnowledgeLLMOptions>(
            configuration.GetSection(KnowledgeLLMOptions.SectionName));

        services.AddHttpClient("openai-embeddings", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<KnowledgeLLMOptions>>().Value;
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", opts.OpenAI.ApiKey);
        });

        services.AddHttpClient("openai-chat", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<KnowledgeLLMOptions>>().Value;
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", opts.OpenAI.ApiKey);
        });

        services.AddSingleton<IDocumentLoader, PlainTextDocumentLoader>();
        services.AddSingleton<ITextChunker>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<KnowledgeLLMOptions>>().Value;
            return new SlidingWindowChunker(opts.Chunker.ChunkSize, opts.Chunker.Overlap);
        });
        services.AddSingleton<IVectorStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<KnowledgeLLMOptions>>().Value;
            if (opts.PgVector.Enabled)
                return new PgVectorStore(opts.PgVector.ConnectionString, opts.OpenAI.EmbeddingDimensions);
            return new InMemoryVectorStore();
        });
        services.AddSingleton<IEmbeddingModel, OpenAIEmbeddingModel>();
        services.AddSingleton<IChatModel>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<KnowledgeLLMOptions>>().Value;
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("openai-chat");
            return new OpenAIChatModel(
                opts.OpenAI.ApiKey,
                opts.OpenAI.ChatModel,
                "https://api.openai.com/v1/",
                httpClient);
        });
        services.AddScoped<IRagPipeline, RagPipeline>();

        return services;
    }

    /// <summary>
    /// Replaces the registered <c>IDocumentLoader</c> with a <see cref="CompositeDocumentLoader"/>
    /// that handles both <c>.txt</c> files (via <see cref="PlainTextDocumentLoader"/>) and
    /// <c>.pdf</c> files (via <see cref="PdfDocumentLoader"/>).
    /// Must be called after <see cref="AddKnowledgeLLM"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, for call chaining.</returns>
    public static IServiceCollection AddPdfDocumentLoader(this IServiceCollection services)
    {
        services.TryAddSingleton<PlainTextDocumentLoader>();
        services.TryAddSingleton<PdfDocumentLoader>();
        services.AddSingleton<IDocumentLoader>(sp =>
            new CompositeDocumentLoader(
                sp.GetRequiredService<PlainTextDocumentLoader>(),
                sp.GetRequiredService<PdfDocumentLoader>()));

        return services;
    }
}

