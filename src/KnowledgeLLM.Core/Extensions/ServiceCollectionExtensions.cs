using System.Diagnostics;
using System.Diagnostics.Metrics;
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
using WeaveLLM.Extensions.DependencyInjection;

namespace KnowledgeLLM.Core.Extensions;

/// <summary>Extension methods for registering KnowledgeLLM services with the DI container.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>The <see cref="ActivitySource"/> name used for all pipeline traces.</summary>
    public const string PipelineActivitySourceName = "KnowledgeLLM.Pipeline";

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

        services.AddSingleton(new ActivitySource(PipelineActivitySourceName));
        services.AddSingleton(new Meter(KnowledgeLlmMetrics.MeterName));
        services.AddSingleton<KnowledgeLlmMetrics>();

        services.AddHttpClient("openai-embeddings", (sp, client) =>
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

        // WeaveLLM.Extensions.DependencyInjection 0.2.1-alpha — L-2 + L-3 fix.
        // Registers IChatModel backed by OpenAIChatModel via IHttpClientFactory (no raw HttpClient).
        // Key mapping: WeaveLLM expects ApiKey (matches) and ModelId (our key is ChatModel —
        // using string-param overload to avoid binding the mismatched config key name).
        services.AddWeaveLLM()
                .AddOpenAI(
                    configuration["KnowledgeLLM:OpenAI:ApiKey"] ?? string.Empty,
                    configuration["KnowledgeLLM:OpenAI:ChatModel"] ?? "gpt-4o-mini");

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

