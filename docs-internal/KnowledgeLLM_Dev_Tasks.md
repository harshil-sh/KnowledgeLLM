# KnowledgeLLM — Development Task List

> All remaining tasks in correct sequence.
> 🤖 = Claude Code CLI | 🟡 = GitHub Copilot
> Break each Claude prompt into its own session to preserve quota.

---

## PHASE 2 — OpenAI Integration + Answer Generation

---

### 2-A 🤖 Claude Code — Configuration classes

```
I am working on KnowledgeLLM, a .NET 8 RAG pipeline app.
Phase 1 is complete. Add strongly-typed configuration.

Create src/KnowledgeLLM.Core/Configuration/KnowledgeLLMOptions.cs

Three classes in one file (no records — needs property setters for IOptions<T>):

public class KnowledgeLLMOptions
{
    public const string SectionName = "KnowledgeLLM";
    public OpenAIOptions OpenAI { get; set; } = new();
    public ChunkerOptions Chunker { get; set; } = new();
}

public class OpenAIOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public string ChatModel { get; set; } = "gpt-4o-mini";
    public int EmbeddingDimensions { get; set; } = 1536;
}

public class ChunkerOptions
{
    public int ChunkSize { get; set; } = 500;
    public int Overlap { get; set; } = 100;
}

XML doc comments on all public members.
Namespace: KnowledgeLLM.Core.Configuration
```

---

### 2-B 🤖 Claude Code — appsettings files

```
I am working on KnowledgeLLM, a .NET 8 RAG pipeline app.

Create two files:

1. src/KnowledgeLLM.Api/appsettings.json
   Replace the existing file entirely with:
   {
     "Logging": {
       "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" }
     },
     "AllowedHosts": "*",
     "KnowledgeLLM": {
       "OpenAI": {
         "ApiKey": "",
         "EmbeddingModel": "text-embedding-3-small",
         "ChatModel": "gpt-4o-mini",
         "EmbeddingDimensions": 1536
       },
       "Chunker": {
         "ChunkSize": 500,
         "Overlap": 100
       }
     }
   }

2. src/KnowledgeLLM.Api/appsettings.Development.json
   Same structure, ApiKey still empty string.

Never commit real API keys.
```

---

### 2-C 🤖 Claude Code — OpenAI Embedding Model

```
I am working on KnowledgeLLM, a .NET 8 RAG pipeline app.
Existing types: IEmbeddingModel, ChainResult<T>, ChainError, KnowledgeLLMOptions.

Create src/KnowledgeLLM.Core/Embeddings/OpenAIEmbeddingModel.cs

Implements IEmbeddingModel.

Constructor: IHttpClientFactory httpClientFactory, IOptions<KnowledgeLLMOptions> options
  Use named HttpClient "openai-embeddings".

int Dimensions => options.Value.OpenAI.EmbeddingDimensions

EmbedAsync(string text, CancellationToken ct):
- Validate text not null/whitespace → ChainError.InvalidInput
- Validate ApiKey not empty → ChainError.InvalidConfiguration with message:
  "OpenAI API key is not configured. Set KnowledgeLLM:OpenAI:ApiKey in
   appsettings.json or via KNOWLEDGELLM__OPENAI__APIKEY environment variable."
- POST https://api.openai.com/v1/embeddings
  Body: { "input": text, "model": embeddingModel }
- HTTP 401 → ChainError.AuthenticationFailed("OpenAI rejected the API key.
  Verify it is valid at platform.openai.com/api-keys.")
- HTTP 429 → ChainError.RateLimitExceeded("OpenAI returned 429.
  Reduce request frequency or add retry logic.")
- HTTP 5xx → ChainError.ProviderError("OpenAI returned {statusCode}.
  This is likely transient — retry the request.", inner)
- TaskCanceledException where ct.IsCancellationRequested → ChainError.Cancelled()
- TaskCanceledException otherwise → ChainError.NetworkTimeout("Request to OpenAI
  timed out. Check network connectivity or increase HttpClient timeout.", inner)
- Parse: data[0].embedding as float[]
- Never throw — all errors as ChainResult.Failure

EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct):
- Validate texts not null/empty → ChainError.InvalidInput
- POST same endpoint with "input": [array]
- Parse: order results by data[].index, return as IReadOnlyList<float[]>
- Same error handling

Private DTOs inside the file (sealed records with JsonPropertyName).
Use System.Text.Json. IHttpClientFactory only — never new HttpClient().
XML doc comments on all public members.
Namespace: KnowledgeLLM.Core.Embeddings
```

---

### 2-D 🤖 Claude Code — PromptBuilder

```
I am working on KnowledgeLLM, a .NET 8 RAG pipeline app.
Existing types: RetrievalResult, TextChunk.

Create src/KnowledgeLLM.Core/Pipeline/PromptBuilder.cs

Internal static class with one method:

  internal static string BuildRagPrompt(string question, IReadOnlyList<RetrievalResult> sources)

Prompt format:
  "You are a helpful assistant. Answer the question using ONLY the context
   provided below. If the answer is not found in the context, say so clearly.

   CONTEXT:
   [1] {sources[0].Chunk.Content}
   [2] {sources[1].Chunk.Content}
   ...

   QUESTION: {question}

   ANSWER:"

Validate: question not null/whitespace, sources not null/empty.
Throw ArgumentException for invalid args (this is an internal helper,
not a public API — ArgumentException is acceptable here).
XML doc comment on the method.
Namespace: KnowledgeLLM.Core.Pipeline
```

---

### 2-E 🤖 Claude Code — OpenAIChatClient

```
I am working on KnowledgeLLM, a .NET 8 RAG pipeline app.
Existing types: ChainResult<T>, ChainError, KnowledgeLLMOptions.

Create src/KnowledgeLLM.Core/Pipeline/OpenAIChatClient.cs

This is a temporary stub — it will be replaced in Phase 3 with
WeaveLLM.Providers IChatModel. Build it as a thin internal HTTP client.

Constructor: IHttpClientFactory httpClientFactory, IOptions<KnowledgeLLMOptions> options
  Use named HttpClient "openai-chat".

Method: Task<ChainResult<string>> CompleteAsync(string prompt, CancellationToken ct)
- Validate prompt not null/whitespace → ChainError.InvalidInput
- Validate ApiKey not empty → ChainError.InvalidConfiguration (same message pattern
  as OpenAIEmbeddingModel)
- POST https://api.openai.com/v1/chat/completions
  Body: { "model": chatModel, "messages": [{"role":"user","content":prompt}],
          "max_tokens": 1024 }
- Parse: choices[0].message.content as string
- Same HTTP error mapping as OpenAIEmbeddingModel
  (401 AuthenticationFailed, 429 RateLimitExceeded, 5xx ProviderError,
   timeout NetworkTimeout, ct cancelled → Cancelled)
- Never throw

Private DTOs sealed records inside file (JsonPropertyName).
System.Text.Json only. IHttpClientFactory only.
XML doc comments.
Namespace: KnowledgeLLM.Core.Pipeline
// TODO Phase 3: replace with WeaveLLM.Providers IChatModel
```

---

### 2-F 🤖 Claude Code — Wire RagPipeline + DI update

```
I am working on KnowledgeLLM, a .NET 8 RAG pipeline app.

Two changes:

CHANGE 1 — Update src/KnowledgeLLM.Core/Pipeline/RagPipeline.cs
Add OpenAIChatClient as a constructor parameter.
Replace the Week 2 stub comment in AskAsync with:
  var prompt = PromptBuilder.BuildRagPrompt(question, sources);
  var completionResult = await _chatClient.CompleteAsync(prompt, ct);
  if (completionResult.IsFailure)
      return ChainResult<RagAnswer>.Failure(completionResult.Error);
  return ChainResult<RagAnswer>.Success(new RagAnswer(completionResult.Value, sources));

CHANGE 2 — Replace src/KnowledgeLLM.Core/Extensions/ServiceCollectionExtensions.cs
New signature: AddKnowledgeLLM(this IServiceCollection services, IConfiguration configuration)

Register:
  services.Configure<KnowledgeLLMOptions>(configuration.GetSection(KnowledgeLLMOptions.SectionName));

  services.AddHttpClient("openai-embeddings", (sp, client) => {
      var opts = sp.GetRequiredService<IOptions<KnowledgeLLMOptions>>().Value;
      client.BaseAddress = new Uri("https://api.openai.com/v1/");
      client.DefaultRequestHeaders.Authorization =
          new AuthenticationHeaderValue("Bearer", opts.OpenAI.ApiKey);
  });

  services.AddHttpClient("openai-chat", (sp, client) => {
      var opts = sp.GetRequiredService<IOptions<KnowledgeLLMOptions>>().Value;
      client.BaseAddress = new Uri("https://api.openai.com/v1/");
      client.DefaultRequestHeaders.Authorization =
          new AuthenticationHeaderValue("Bearer", opts.OpenAI.ApiKey);
  });

  services.AddSingleton<IDocumentLoader, PlainTextDocumentLoader>();
  services.AddSingleton<ITextChunker>(sp => {
      var opts = sp.GetRequiredService<IOptions<KnowledgeLLMOptions>>().Value;
      return new SlidingWindowChunker(opts.Chunker.ChunkSize, opts.Chunker.Overlap);
  });
  services.AddSingleton<IVectorStore, InMemoryVectorStore>();
  services.AddSingleton<IEmbeddingModel, OpenAIEmbeddingModel>();
  services.AddSingleton<OpenAIChatClient>();
  services.AddScoped<IRagPipeline, RagPipeline>();
  return services;

Also update src/KnowledgeLLM.Api/Program.cs:
  Change: builder.Services.AddKnowledgeLLM()
  To:     builder.Services.AddKnowledgeLLM(builder.Configuration)

Verify dotnet build passes after changes.
```

---

### 2-G 🤖 Claude Code — Integration test

```
I am working on KnowledgeLLM, a .NET 8 RAG pipeline app.
Phase 2 concrete classes are all implemented.

Create tests/KnowledgeLLM.Core.Tests/Pipeline/RagPipelineIntegrationTests.cs

One test class using real implementations (no mocks except embeddings + chat):
  - PlainTextDocumentLoader (real)
  - SlidingWindowChunker(chunkSize: 200, overlap: 50) (real)
  - InMemoryVectorStore (real)
  - IEmbeddingModel — NSubstitute mock returning deterministic float[3] vectors:
      EmbedAsync returns new float[]{ 0.1f, 0.5f, 0.9f }
      EmbedBatchAsync returns same vector repeated for each input
  - OpenAIChatClient — NSubstitute mock returning
      ChainResult<string>.Success("This is a test answer.")

Test method: IndexThenAsk_WithMockedEmbeddingsAndChat_ReturnsAnswer
  1. Create a temp .txt file in Path.GetTempPath() containing:
     "The sky is blue. Water is wet. Fire is hot."
  2. Wire all components manually (no DI container)
  3. Call pipeline.IndexAsync(tempFilePath) — assert IsSuccess, Value > 0
  4. Call pipeline.AskAsync("What colour is the sky?", topK: 3)
     — assert IsSuccess
     — assert Answer == "This is a test answer."
     — assert Sources.Count > 0
  5. Delete temp file in finally block

xUnit + FluentAssertions + NSubstitute.
Naming: {Method}_{Condition}_{ExpectedOutcome}
```

---

### 2-H 🟡 GitHub Copilot — Unit tests for Phase 2 classes

```
Using #file:OpenAIEmbeddingModel.cs #file:OpenAIChatClient.cs
#file:PromptBuilder.cs #file:SlidingWindowChunker.cs
#file:PlainTextDocumentLoader.cs #file:InMemoryVectorStore.cs

Write comprehensive unit tests for all six classes.
Framework: xUnit + FluentAssertions + NSubstitute
Naming: {Method}_{Condition}_{ExpectedOutcome}

For OpenAIEmbeddingModel and OpenAIChatClient:
  Mock IHttpClientFactory to return HttpResponseMessage with:
  - 200 OK with valid JSON body
  - 401 Unauthorized
  - 429 Too Many Requests
  - 500 Internal Server Error
  - TaskCanceledException (timeout)
  - TaskCanceledException with CancellationToken fired

Coverage per class:
  - Happy path
  - Each ChainError.Code error path
  - Null / empty / whitespace inputs
  - CancellationToken respected
  - Thread safety (concurrent calls where relevant)

Use [Theory] + [InlineData] for multiple input variants.
Place in tests/KnowledgeLLM.Core.Tests/ mirroring src folder structure.
```

---

### 2-I 🟡 GitHub Copilot — XML doc sweep Phase 2 (*)

```
Using #file:OpenAIEmbeddingModel.cs #file:OpenAIChatClient.cs
#file:KnowledgeLLMOptions.cs #file:PromptBuilder.cs
#file:ServiceCollectionExtensions.cs

Add or complete XML doc comments on all public and internal members.
Rules:
- Methods: document each param, return value, and both ChainResult cases
- Classes: one-line <summary>
- Properties: one-line <summary>
- Do not add comments to private members
```

---

## PHASE 3 — WeaveLLM.Providers Integration + Streaming (*)

---

### 3-A 🤖 Claude Code — Replace OpenAIChatClient with WeaveLLM.Providers

```
I am working on KnowledgeLLM, a .NET 8 RAG pipeline app.
WeaveLLM.Providers 0.1.0-alpha is now available on NuGet.
It provides IChatModel and IStreamingChatModel.

STEP 1 — Add package to KnowledgeLLM.Core.csproj:
  <PackageReference Include="WeaveLLM.Providers" Version="0.1.0-alpha" />

STEP 2 — Delete src/KnowledgeLLM.Core/Pipeline/OpenAIChatClient.cs
  (functionality replaced by WeaveLLM.Providers)

STEP 3 — Update RagPipeline.cs
  Replace OpenAIChatClient constructor parameter with IChatModel from WeaveLLM.Providers.
  Update AskAsync to call IChatModel instead of OpenAIChatClient.
  Keep all ChainResult error handling — never throw.

STEP 4 — Update ServiceCollectionExtensions.cs
  Remove: services.AddSingleton<OpenAIChatClient>()
  Add:    services.AddSingleton<IChatModel>(sp => { /* wire from WeaveLLM.Providers */ })
  Use WeaveLLM.Providers DI helpers if available, otherwise construct directly.

STEP 5 — Verify dotnet build passes.
```

---

### 3-B 🤖 Claude Code — Streaming endpoint

```
I am working on KnowledgeLLM, a .NET 8 RAG pipeline app.
WeaveLLM.Providers IStreamingChatModel is available.

Add a streaming ask endpoint to the pipeline and API.

STEP 1 — Add to IRagPipeline.cs:
  IAsyncEnumerable<string> AskStreamAsync(
      string question, int topK = 5, CancellationToken ct = default)

STEP 2 — Implement in RagPipeline.cs:
  - Same embed + retrieve steps as AskAsync
  - Build prompt via PromptBuilder.BuildRagPrompt
  - Yield tokens from IStreamingChatModel.StreamAsync(prompt, ct)
  - On any failure yield a single error token and stop — never throw

STEP 3 — Add to KnowledgeController.cs:
  POST /api/knowledge/ask/stream
  Content-Type: text/event-stream
  Body: { question: string, topK: int = 5 }
  Stream tokens back as Server-Sent Events:
    data: {token}\n\n
  On completion send: data: [DONE]\n\n

Conventions: CancellationToken on all methods, IAsyncEnumerable throughout,
XML doc comments, never throw.
```

---

### 3-C 🟡 GitHub Copilot — Tests for streaming

```
Using #file:RagPipeline.cs #file:IRagPipeline.cs #file:KnowledgeController.cs

Write unit tests for AskStreamAsync in RagPipeline and the streaming
endpoint in KnowledgeController.

For RagPipeline streaming tests:
- Mock IStreamingChatModel to yield 3 tokens then complete
- Assert all tokens are yielded in order
- Assert embed + retrieve called once each
- Test cancellation mid-stream

Naming: {Method}_{Condition}_{ExpectedOutcome}
xUnit + FluentAssertions + NSubstitute
```

---

## PHASE 4 — Persistence + Additional Loaders

---

### 4-A 🤖 Claude Code — pgvector store

```
I am working on KnowledgeLLM, a .NET 8 RAG pipeline app.
InMemoryVectorStore works but data is lost on restart.

Add src/KnowledgeLLM.Core/Retrieval/PgVectorStore.cs

Implements IVectorStore using Npgsql + pgvector extension.

Constructor: string connectionString, int dimensions
  Use NpgsqlDataSource (not new NpgsqlConnection directly).

Schema (create if not exists on first use):
  CREATE TABLE IF NOT EXISTS chunks (
    id TEXT PRIMARY KEY,
    document_id TEXT NOT NULL,
    content TEXT NOT NULL,
    metadata JSONB,
    embedding vector({dimensions})
  );
  CREATE INDEX IF NOT EXISTS chunks_embedding_idx
    ON chunks USING ivfflat (embedding vector_cosine_ops);

UpsertAsync: INSERT ... ON CONFLICT (id) DO UPDATE
SearchAsync: SELECT ... ORDER BY embedding <=> $query LIMIT topK
DeleteByDocumentAsync: DELETE WHERE document_id = $documentId

NuGet to add to Core.csproj:
  Npgsql 8.x
  Npgsql.EntityFrameworkCore.PostgreSQL (if needed)
  pgvector 0.x

All errors as ChainResult.Failure. Never throw. CancellationToken everywhere.
XML doc comments.
```

---

### 4-B 🤖 Claude Code — PDF document loader

```
I am working on KnowledgeLLM, a .NET 8 RAG pipeline app.

Add src/KnowledgeLLM.Core/Documents/PdfDocumentLoader.cs

Implements IDocumentLoader.
Uses PdfPig NuGet package (UglyToad.PdfPig).

LoadAsync(string source, CancellationToken ct):
- source is a file path to a .pdf file
- Validate source not null/whitespace → ChainError.InvalidInput
- Validate file exists → ChainError.NotFound
- Extract text page by page using PdfPig
- Return one Document per PDF (Id = full path, Content = all pages joined with \n\n)
- Include metadata: { "pages": pageCount.ToString(), "source": "pdf" }
- ChainError.ProviderError for any PdfPig exception (with inner)
- Never throw. CancellationToken.ThrowIfCancellationRequested() between pages.
- XML doc comments.

Add to Core.csproj: <PackageReference Include="PdfPig" Version="0.1.9" />
```

---

### 4-C 🟡 GitHub Copilot — Tests for Phase 4 classes (*)

```
Using #file:PgVectorStore.cs #file:PdfDocumentLoader.cs

Write unit tests.
For PgVectorStore: mock NpgsqlDataSource/connection — test UpsertAsync,
  SearchAsync (returns ordered results), DeleteByDocumentAsync,
  invalid inputs, cancellation.
For PdfDocumentLoader: use a real minimal PDF byte array in tests
  (construct programmatically or embed as resource), test happy path,
  file not found, invalid path, cancellation.

xUnit + FluentAssertions + NSubstitute
Naming: {Method}_{Condition}_{ExpectedOutcome}
```

---

### 4-D 🟡 GitHub Copilot — DI registration update for Phase 4

```
Using #file:ServiceCollectionExtensions.cs #file:PgVectorStore.cs
#file:PdfDocumentLoader.cs #file:KnowledgeLLMOptions.cs

Update AddKnowledgeLLM() to support optional pgvector and PDF loader:

1. Add PgVectorOptions to KnowledgeLLMOptions:
   public class PgVectorOptions {
       public string ConnectionString { get; set; } = string.Empty;
       public bool Enabled { get; set; } = false;
   }

2. In AddKnowledgeLLM:
   If options.PgVector.Enabled → register PgVectorStore as IVectorStore
   Else → keep InMemoryVectorStore

3. Add a separate extension:
   services.AddPdfDocumentLoader()
   which replaces IDocumentLoader with a CompositeDocumentLoader that
   tries PlainTextDocumentLoader for .txt and PdfDocumentLoader for .pdf.

XML doc comments on new public members.
```

---

## PHASE 5 — Observability + README Polish

---

### 5-A 🟡 GitHub Copilot — README final polish

```
Using #file:README.md #file:Program.cs #file:KnowledgeLLMOptions.cs
#file:KnowledgeController.cs

Rewrite README.md to include:
1. Badges: CI status, NuGet (WeaveLLM.Core), .NET 8, MIT license
2. One-paragraph description
3. Prerequisites: .NET 8 SDK, OpenAI API key, optional Postgres for pgvector
4. Quick Start:
   - clone → set API key (user-secrets command) → dotnet run
   - curl examples for /index and /ask
5. Configuration table (all KnowledgeLLM:* keys with descriptions and defaults)
6. Architecture diagram (ASCII — index flow and query flow)
7. Project structure tree
8. Roadmap with Phase 1-4 status and Phase 5 pending
9. Contributing + License sections
Keep it concise — developers should be running in under 5 minutes.
```

---

### 5-B 🤖 Claude Code — OpenTelemetry integration

```
I am working on KnowledgeLLM, a .NET 8 RAG pipeline app.
Add OpenTelemetry tracing to the RAG pipeline so each stage
(load, chunk, embed, upsert, search, complete) is a named span.

STEP 1 — Add to Core.csproj:
  OpenTelemetry 1.8.x
  OpenTelemetry.Extensions.Hosting 1.8.x

STEP 2 — Instrument RagPipeline.cs
  Inject ActivitySource (named "KnowledgeLLM.Pipeline").
  Wrap each stage in IndexAsync and AskAsync with Activity spans:
    "knowledge.index", "knowledge.load", "knowledge.chunk",
    "knowledge.embed", "knowledge.upsert",
    "knowledge.ask", "knowledge.search", "knowledge.complete"
  Add span attributes: document.id, chunks.count, topk, error.code on failure.
  Never throw. Spans must end even on failure.

STEP 3 — Update Program.cs
  Add AddOpenTelemetry() with ActivitySource "KnowledgeLLM.Pipeline".
  Export to console in Development, OTLP in Production.

XML doc comments. All conventions apply.
```

---

### 5-C 🟡 GitHub Copilot — CLAUDE.md final update

```
Using #file:CLAUDE.md

Update CLAUDE.md to reflect the completed project state:
- Update Phase Status table (all phases complete)
- Add build/test/run commands
- Add environment variable reference
- Add the known-stub table with all items marked resolved
- Keep under 60 lines total
```

---

## QUICK REFERENCE — Prompt Checklist

| # | Task | Tool | Phase | Done |
|---|---|---|---|---|
| 2-A | Configuration classes | 🤖 Claude | 2 | ⬜ |
| 2-B | appsettings files | 🤖 Claude | 2 | ⬜ |
| 2-C | OpenAI Embedding Model | 🤖 Claude | 2 | ⬜ |
| 2-D | PromptBuilder | 🤖 Claude | 2 | ⬜ |
| 2-E | OpenAIChatClient | 🤖 Claude | 2 | ⬜ |
| 2-F | Wire RagPipeline + DI | 🤖 Claude | 2 | ⬜ |
| 2-G | Integration test | 🤖 Claude | 2 | ⬜ |
| 2-H | Unit tests Phase 2 | 🟡 Copilot | 2 | ⬜ |
| 2-I | XML docs Phase 2 | 🟡 Copilot | 2 | ⬜ |
| 3-A | WeaveLLM.Providers swap | 🤖 Claude | 3 | ⬜ |
| 3-B | Streaming endpoint | 🤖 Claude | 3 | ⬜ |
| 3-C | Streaming tests | 🟡 Copilot | 3 | ⬜ |
| 4-A | pgvector store | 🤖 Claude | 4 | ✅ |
| 4-B | PDF document loader | 🤖 Claude | 4 | ⬜ |
| 4-C | Phase 4 tests | 🟡 Copilot | 4 | ⬜ |
| 4-D | DI update Phase 4 | 🟡 Copilot | 4 | ⬜ |
| 5-A | README polish | 🟡 Copilot | 5 | ⬜ |
| 5-B | OpenTelemetry | 🤖 Claude | 5 | ⬜ |
| 5-C | CLAUDE.md update | 🟡 Copilot | 5 | ⬜ |

**Claude sessions: 10 | Copilot sessions: 9**

---

## WeaveLLM Package Feedback

### 0.2.0-alpha upgrade — 07 May 2026

- **`Message`, `LLMOptions` moved to `WeaveLLM.Core.Models`** — the `WeaveLLM.Core.Providers` namespace no longer exports these types. Any alias pointing to `WeaveLLM.Core.Providers.Message` or `WeaveLLM.Core.Providers.LLMOptions` breaks at compile time. Updated all aliases to `WeaveLLM.Core.Models.*` across `RagPipeline.cs` and all three test files.
- **`MessageRole` renamed to `WeaveLLM.Core.Models.Role`** — the old `WeaveLLM.Core.Providers.MessageRole` enum no longer exists. New preferred API is the `Message.User(content)` static factory method (avoids the enum entirely).
- **`IChatModel.ChatAsync` return type changed to `ChainResult<ChatResponse>`** — was `ChainResult<Message>`. `ChatResponse` has a `.Content string` property (same as `Message`), so the call site `chatResult.Value.Content` still compiles. NSubstitute mocks in tests must be updated from `ChainResult<LLMMessage>` to `ChainResult<ChatResponse>`.
- **`WeaveLLM.Core.Models.IEmbeddingModel` now conflicts in more files** — importing `using WeaveLLM.Core.Models;` (needed for `ChainResult<T>`) now pulls in `WeaveLLM.Core.Models.IEmbeddingModel`, causing CS0104 in any file that also has `using KnowledgeLLM.Core.Embeddings;`. Fix: replace the `using KnowledgeLLM.Core.Embeddings;` directive with an explicit alias `using IEmbeddingModel = KnowledgeLLM.Core.Embeddings.IEmbeddingModel;`.
- **`Microsoft.Extensions.*` dependencies bumped to 9.0.x** — `WeaveLLM.Core 0.2.0-alpha` and `WeaveLLM.Providers 0.2.0-alpha` require `Microsoft.Extensions.DependencyInjection.Abstractions >= 9.0.0`, `Logging.Abstractions >= 9.0.0`, and `Http >= 9.0.0`. Our pinned 8.x versions triggered NU1605 (downgrade detected, treated as error). Updated all three to `9.0.0` in `KnowledgeLLM.Core.csproj`.

### L-1 resolution verification — 08 May 2026

- **`WeaveLLM.Core.Providers.IEmbeddingModel` moved to `WeaveLLM.Core.Providers.Embeddings`** — No code changes required. All four affected files (`RagPipeline.cs`, `RagPipelineTests.cs`, `RagPipelineIntegrationTests.cs`, `ServiceCollectionExtensions.cs`) already alias `IEmbeddingModel = KnowledgeLLM.Core.Embeddings.IEmbeddingModel` (our own interface) — the WeaveLLM-internal move has zero impact on the call sites. `dotnet build` passes with 0 errors.
- **Step 2 check (bare `using WeaveLLM.Core.Providers;`)** — A bare `using WeaveLLM.Core.Providers;` no longer causes CS0104 from the `IEmbeddingModel` collision in that namespace. Aliases are kept for clarity. Whether `WeaveLLM.Core.Models.IEmbeddingModel` has also been removed is not yet verified; the existing explicit-alias pattern is safe either way.
- **CLAUDE.md updated** — Convention note updated: the specific CS0104 risk from `WeaveLLM.Core.Providers` is resolved; correct fully-qualified alias paths are now documented inline.

### WeaveLLMError factory-method migration — 07 May 2026

- **Migrated all `new WeaveLLMError(...)` manual constructions to factory methods** — `InMemoryVectorStore`, `RagPipeline`, `PdfDocumentLoader`, `PlainTextDocumentLoader`, `PgVectorStore`, and `CompositeDocumentLoader` now all call `WeaveLLMError.NotFound()`, `WeaveLLMError.Cancelled(msg, ex)`, and `WeaveLLMError.ProviderError(provider, msg, ex)`. The entire codebase is now consistently SCREAMING_SNAKE_CASE. Resolves L-10.
- **Updated `KnowledgeController.MapError` to SCREAMING_SNAKE_CASE** — changed `"InvalidInput" or "InvalidConfiguration" or "NotFound"` to `"INVALID_INPUT" or "INVALID_CONFIGURATION" or "NOT_FOUND"`. HTTP 400/500 routing now aligns with factory method output across all components. Resolves L-9.
- **Updated all test assertions** — `"NotFound"` → `"NOT_FOUND"`, `"Cancelled"` → `"CANCELLED"`, `"ProviderError"` → `"PROVIDER_ERROR"` across `InMemoryVectorStoreTests.cs`, `PgVectorStoreTests.cs`, `PdfDocumentLoaderTests.cs`, `PlainTextDocumentLoaderTests.cs`, and `CompositeDocumentLoaderTests.cs`. All 172 tests pass.

---

## Rules for Running Claude Prompts

1. **One prompt = one fresh Claude Code session.** Never continue from the previous session — start clean each time.
2. **Paste the prompt as the very first message.** No warm-up chat before the prompt.
3. **Verify after each prompt:** `dotnet build` must exit 0 before moving to the next.
4. **If a prompt fails:** reduce scope, not session length. Split into two prompts.
5. **Copilot prompts:** run inside VS Code Copilot Chat with the named `#file:` references attached. No file context = poor output.
