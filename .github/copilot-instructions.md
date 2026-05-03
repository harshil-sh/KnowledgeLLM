# KnowledgeLLM — Copilot Instructions

**KnowledgeLLM** is a .NET 8 RAG (Retrieval-Augmented Generation) pipeline. It indexes documents and answers questions against them via two HTTP endpoints. All domain logic lives in `KnowledgeLLM.Core`; `KnowledgeLLM.Api` is a thin ASP.NET wrapper.

---

## Commands

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run a single test class
dotnet test --filter "FullyQualifiedName~SlidingWindowChunkerTests"

# Run a single test method
dotnet test --filter "FullyQualifiedName~EmbedAsync_401Unauthorized_ReturnsAuthenticationFailed"

# Run the API  (Swagger UI at http://localhost:5000/swagger)
dotnet run --project src/KnowledgeLLM.Api

# Set the OpenAI API key for local dev
dotnet user-secrets --project src/KnowledgeLLM.Api set "KnowledgeLLM:OpenAI:ApiKey" "sk-..."
```

---

## Architecture

### Index flow
```
Source path
  → IDocumentLoader.LoadAsync()        — reads .txt file(s) from disk
  → ITextChunker.ChunkAsync()          — splits into overlapping TextChunks
  → IEmbeddingModel.EmbedBatchAsync()  — converts chunk text to float[] vectors
  → IVectorStore.UpsertAsync()         — stores (TextChunk, float[]) pairs
  → returns: total chunks indexed (int)
```

### Query flow
```
Question (string)
  → IEmbeddingModel.EmbedAsync()       — embeds the question
  → IVectorStore.SearchAsync()         — cosine similarity, top-K
  → PromptBuilder.BuildRagPrompt()     — formats grounded prompt (internal static)
  → OpenAIChatClient.CompleteAsync()   — generates answer
  → returns: RagAnswer { Answer, Sources[] }
```

`RagPipeline` is the stateless orchestrator for both flows. If any stage returns a failure, the pipeline **short-circuits immediately** — no subsequent stages run.

### HTTP layer
`KnowledgeController` has two endpoints: `POST /api/knowledge/index` and `POST /api/knowledge/ask`. It maps `ChainError.Code` to HTTP status: `InvalidInput`, `InvalidConfiguration`, `NotFound` → 400; everything else → 500.

---

## Error handling — ChainResult\<T\>

Every method in Core returns `ChainResult<T>` from `WeaveLLM.Core` — a discriminated union of `Success(T)` or `Failure(ChainError)`.

**Never throw exceptions.** Return `ChainResult<T>.Failure(...)` instead.

```csharp
// Using the WeaveLLM factory (preferred when the code exists)
return ChainResult<T>.Failure(WeaveLLMError.InvalidInput("message"));

// Constructing manually
return ChainResult<T>.Failure(new WeaveLLMError("message", "AUTHENTICATION_FAILED"));
```

Error codes in use (exact strings matter — they are matched by `KnowledgeController`):

| Situation | Code string |
|---|---|
| Bad user input | `"INVALID_INPUT"` (via `WeaveLLMError.InvalidInput()`) |
| Missing/invalid config | `"INVALID_CONFIGURATION"` |
| Resource not found | `"NotFound"` |
| OpenAI 401 | `"AUTHENTICATION_FAILED"` |
| OpenAI 429 | `"RATE_LIMIT_EXCEEDED"` |
| OpenAI 5xx / HTTP error | `"PROVIDER_ERROR"` |
| External cancellation | `"CANCELLED"` |
| HttpClient timeout | `"NETWORK_TIMEOUT"` |
| Operation cancelled | `"Cancelled"` |

---

## Key conventions

- **Every async method** must accept and forward a `CancellationToken`.
- **All public members** require XML doc comments (`/// <summary>`).
- **Never use `new HttpClient()`** — inject `IHttpClientFactory` and use the named clients `"openai-embeddings"` or `"openai-chat"`.
- **No secrets in source code** — API key comes from `KNOWLEDGELLM__OPENAI__APIKEY` env var or `dotnet user-secrets`.
- **Chunk IDs** follow the pattern `{DocumentId}_{Index}` (e.g. `"doc1_0"`, `"doc1_1"`).
- **Config** always binds from the `"KnowledgeLLM"` section via `KnowledgeLLMOptions`.

---

## DI registration

Registered by `builder.Services.AddKnowledgeLLM(configuration)` (extension in `Core/Extensions/`):

| Service | Implementation | Lifetime |
|---|---|---|
| `IDocumentLoader` | `PlainTextDocumentLoader` | Singleton |
| `ITextChunker` | `SlidingWindowChunker` | Singleton |
| `IVectorStore` | `InMemoryVectorStore` | Singleton |
| `IEmbeddingModel` | `OpenAIEmbeddingModel` | Singleton |
| `OpenAIChatClient` | concrete | Singleton |
| `IRagPipeline` | `RagPipeline` | Scoped |

`SlidingWindowChunker` is constructed with `ChunkSize`/`Overlap` from `KnowledgeLLMOptions.Chunker`. Adding new services: register here, not in `Program.cs`.

---

## Testing

Framework: **xUnit + FluentAssertions + NSubstitute**  
Test project: `tests/KnowledgeLLM.Core.Tests/` — mirrors `src/KnowledgeLLM.Core/` folder structure.

**Test naming:** `{Method}_{Condition}_{ExpectedOutcome}`
```
EmbedAsync_401Unauthorized_ReturnsAuthenticationFailed
ChunkAsync_OverlapExceedsChunkSize_ReturnsInvalidInput
```

### Mocking IHttpClientFactory

`HttpMessageHandler.SendAsync` is protected and cannot be mocked directly. Use the fake handlers in `tests/KnowledgeLLM.Core.Tests/Helpers/HttpTestHelpers.cs`:

```csharp
// Return a fixed response
var sut = BuildSut(ValidOptions(), new FakeHttpMessageHandler(JsonOk(json)));

// Simulate an HttpClient timeout (external CT not fired → NETWORK_TIMEOUT)
var sut = BuildSut(ValidOptions(), new ThrowingHttpMessageHandler(new TaskCanceledException("timeout")));

// Simulate external cancellation (external CT fired → CANCELLED)
using var cts = new CancellationTokenSource();
var sut = BuildSut(ValidOptions(), new CancelOnSendHandler(cts));
var result = await sut.EmbedAsync("text", cts.Token);

// Concurrent-safe: creates a fresh HttpResponseMessage per call
factory.CreateClient(Arg.Any<string>())
       .Returns(_ => new HttpClient(new JsonFactoryHttpMessageHandler(json)));
```

### Testing internal classes

`PromptBuilder` is `internal`. The Core project has `<InternalsVisibleTo Include="KnowledgeLLM.Core.Tests" />` so it is directly accessible in tests.

### OpenAIChatClient

Has a `protected` parameterless constructor specifically so NSubstitute can create a proxy:
```csharp
var chatClient = Substitute.For<OpenAIChatClient>();
```

---

## Development phases

| Phase | Status | Focus |
|---|---|---|
| 1 | ✅ Complete | Interfaces, loader, chunker, vector store, API shell |
| 2 | 🔄 In progress | OpenAI embedding + chat completion, config binding |
| 3 | ⏳ Pending | Replace `OpenAIChatClient` with `WeaveLLM.Providers` `IChatModel` |
| 4 | ⏳ Pending | pgvector persistence, PDF/Word loaders, observability |

Phase 3 note: `OpenAIChatClient` is a deliberate stub. When Phase 3 arrives, replace it with the `WeaveLLM.Providers` package — do not extend it further.
