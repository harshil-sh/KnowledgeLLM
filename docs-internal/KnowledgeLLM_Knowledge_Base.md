# KnowledgeLLM — Project Knowledge Base

> Last updated: 03 May 2026 (updated after Task 3-B)
> Single source of truth for project context, architecture, and conventions.

---

## 1. Project Identity

| Field | Value |
|---|---|
| **Repo Name** | KnowledgeLLM |
| **Description** | Ask questions across your documents — a RAG pipeline built on WeaveLLM.Core for .NET |
| **Type** | Real application (not a sample, not a library) |
| **Platform** | .NET 8 / C# |
| **Core Dependency** | `WeaveLLM.Core 0.1.0-alpha` (NuGet — never ProjectReference) |
| **Current Stage** | Phase 3 in progress (3-A + 3-B done) |

---

## 2. Dependency: WeaveLLM.Core

Published at: https://www.nuget.org/packages/WeaveLLM.Core/0.1.0-alpha

### Key types used in KnowledgeLLM

| Type | Purpose |
|---|---|
| `ChainResult<T>` | Discriminated union — `Success(T)` or `Failure(ChainError)` |
| `ChainError` | Structured error with `.Code` (PascalCase) and `.Message` (actionable) |
| `ChainError.Codes` | Constants: `InvalidInput`, `InvalidConfiguration`, `NetworkTimeout`, `AuthenticationFailed`, `RateLimitExceeded`, `ProviderError`, `NotFound`, `Cancelled` |
| `IChain<TIn,TOut>` | `ExecuteAsync(TIn, CancellationToken) → Task<ChainResult<TOut>>` |

### WeaveLLM ecosystem packages (all 0.1.0-alpha)

```
WeaveLLM.Core                              ← used now (models, ChainResult, error types)
WeaveLLM.Providers                         ← used now (OpenAIChatModel — Phase 3+)
WeaveLLM.Memory                            ← Redis, Postgres, CosmosDB (Phase 4)
WeaveLLM.Observability                     ← OpenTelemetry (Phase 4)
WeaveLLM.Extensions.DependencyInjection    ← fluent DI builder (not yet available)
```

---

## 2-B. WeaveLLM Package: Discovered API Reference

> Learned during Phase 3 (tasks 3-A and 3-B) by reflecting the DLLs directly.
> Read this section before touching any WeaveLLM type to avoid repeating the same mistakes.

### Package layout — interfaces vs. implementations

| Package | What it contains |
|---|---|
| `WeaveLLM.Core` | `ChainResult<T>`, `WeaveLLMError`, core models |
| `WeaveLLM.Core.Providers` (namespace inside `WeaveLLM.Core.dll`) | All provider **interfaces**: `IChatModel`, `ILanguageModel`, `IEmbeddingModel`, `LLMOptions`, `Message`, `MessageRole` |
| `WeaveLLM.Providers` | Concrete implementations only: `OpenAIChatModel`, `AnthropicChatModel` |

`WeaveLLM.Providers` ships **no interfaces** — every interface lives in `WeaveLLM.Core`.

### Full interface inventory (`WeaveLLM.Core.Providers` namespace)

| Type | Kind | Key members |
|---|---|---|
| `IChatModel` | Interface | `ChatAsync(IReadOnlyList<Message>, LLMOptions, ct) → Task<ChainResult<Message>>`<br>`StreamChatAsync(IReadOnlyList<Message>, LLMOptions, ct) → IAsyncEnumerable<string>` |
| `ILanguageModel` | Interface (base of `IChatModel`) | `CompleteAsync(string, LLMOptions, ct) → Task<ChainResult<string>>`<br>`StreamCompleteAsync(string, LLMOptions, ct) → IAsyncEnumerable<string>`<br>`CountTokensAsync(string, ct) → Task<int>`<br>`ProviderName`, `ModelId` properties |
| `IEmbeddingModel` | Interface | ⚠️ name collides with project's own — see below |
| `LLMOptions` | Class | `Temperature double?`, `TopP double?`, `MaxTokens int?`, `StopSequences`, `FrequencyPenalty double?`, `PresencePenalty double?`, `Seed int?`, `ResponseFormat string`, `ProviderSpecific Dictionary<string,object>` |
| `Message` | Class | `Role MessageRole`, `Content string`, `Name string`, `ToolCalls`, `ToolCallId string`, `Timestamp DateTimeOffset` |
| `MessageRole` | Enum | `System`, `User`, `Assistant`, `Tool` |

`OpenAIChatModel` (`WeaveLLM.Providers.OpenAI`) and `AnthropicChatModel` (`WeaveLLM.Providers.Anthropic`) both implement `IChatModel` and `ILanguageModel`.

### CRITICAL: Namespace collision — never `using WeaveLLM.Core.Providers;`

`WeaveLLM.Core.Providers` exports its own `IEmbeddingModel`. Adding a bare
`using WeaveLLM.Core.Providers;` in any file that also uses
`KnowledgeLLM.Core.Embeddings.IEmbeddingModel` produces **CS0104 ambiguous reference**.

**Rule: never open the namespace. Always use type aliases for every WeaveLLM.Core.Providers type.**

```csharp
// Required in every file that uses WeaveLLM chat/language types
using IChatModel    = WeaveLLM.Core.Providers.IChatModel;
using LLMMessage    = WeaveLLM.Core.Providers.Message;
using LLMOptions    = WeaveLLM.Core.Providers.LLMOptions;
using MessageRole   = WeaveLLM.Core.Providers.MessageRole;
```

Applies to: `RagPipeline.cs`, `ServiceCollectionExtensions.cs`, every test file that mocks `IChatModel`.

### `IChatModel.ChatAsync` returns `ChainResult<Message>`, not `ChainResult<string>`

Extract the answer string from `result.Value.Content`. Always check `IsSuccess` first.

```csharp
var messages = new List<LLMMessage> { new() { Role = MessageRole.User, Content = prompt } };
var chatResult = await _chatModel.ChatAsync(messages, new LLMOptions { MaxTokens = 1024 }, ct);
if (!chatResult.IsSuccess)
    return ChainResult<RagAnswer>.Failure(chatResult.Error);
return ChainResult<RagAnswer>.Success(new RagAnswer(chatResult.Value.Content, sources));
```

### Streaming — `IChatModel.StreamChatAsync` (no separate `IStreamingChatModel`)

There is **no `IStreamingChatModel` interface**. Streaming is built directly into `IChatModel`:

```csharp
IAsyncEnumerable<string> StreamChatAsync(
    IReadOnlyList<Message> messages,
    LLMOptions options,
    CancellationToken cancellationToken);
```

Takes a fully-constructed message list. If your input is a raw prompt string, wrap it:

```csharp
var messages = new List<LLMMessage> { new() { Role = MessageRole.User, Content = prompt } };
await foreach (var token in _chatModel.StreamChatAsync(messages, new LLMOptions { MaxTokens = 1024 }, ct)
                                      .WithCancellation(ct))
{
    yield return token;
}
```

`ILanguageModel.StreamCompleteAsync(string, LLMOptions, ct)` also exists and accepts a raw string
directly — use it for single-turn, non-chat flows where you have a plain prompt.

### `OpenAIChatModel` constructor takes a concrete `HttpClient`, not `IHttpClientFactory`

```csharp
new OpenAIChatModel(string apiKey, string modelId, string baseUrl, HttpClient httpClient)
```

Workaround — call `factory.CreateClient(...)` inside the DI factory delegate:

```csharp
services.AddSingleton<IChatModel>(sp =>
{
    var opts    = sp.GetRequiredService<IOptions<KnowledgeLLMOptions>>().Value;
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var http    = factory.CreateClient("openai-chat");
    return new OpenAIChatModel(opts.OpenAI.ApiKey, opts.OpenAI.ChatModel,
                               "https://api.openai.com/v1/", http);
});
```

### `WeaveLLMError` is the real error type (not `ChainError`)

Section 2's table lists `ChainError` — the actual type is `WeaveLLMError` (in `WeaveLLM.Core.Models`).
Error code strings are **SCREAMING_SNAKE_CASE**: `"INVALID_INPUT"`, `"PROVIDER_ERROR"`, `"AUTHENTICATION_FAILED"`, etc.

### Mocking `IChatModel` in tests (NSubstitute)

```csharp
var chatModel = Substitute.For<IChatModel>();
chatModel.ChatAsync(Arg.Any<IReadOnlyList<LLMMessage>>(), Arg.Any<LLMOptions>(), Arg.Any<CancellationToken>())
         .Returns(ChainResult<LLMMessage>.Success(new LLMMessage { Role = MessageRole.Assistant, Content = "answer" }));

// Mocking streaming
chatModel.StreamChatAsync(Arg.Any<IReadOnlyList<LLMMessage>>(), Arg.Any<LLMOptions>(), Arg.Any<CancellationToken>())
         .Returns(new[] { "Hello", " world" }.ToAsyncEnumerable());
```

---

## 2-C. WeaveLLM Package Limitations & NuGet Roadmap Feedback

> Structured findings from dog-fooding `WeaveLLM.Core 0.1.0-alpha` and `WeaveLLM.Providers 0.1.0-alpha`
> inside a real .NET 8 RAG application. Intended as actionable feedback for package evolution.

---

### L-1 — Namespace collision: `WeaveLLM.Core.Providers.IEmbeddingModel`

**Severity:** High — breaks compilation on first import  
**Discovered in:** Task 3-A

**Problem:**  
`WeaveLLM.Core.Providers` exports `IEmbeddingModel`. Any project that also defines its own
embedding interface (a common pattern in layered architectures) cannot open this namespace
without hitting CS0104. The only workaround is to never use `using WeaveLLM.Core.Providers;`
and instead alias every type individually.

**Impact:**  
- Every file touching chat/language types needs boilerplate aliases
- A developer's first instinct (`using WeaveLLM.Core.Providers;`) produces a compiler error
- No IDE warning before the conflict hits build

**Suggested fix:**  
Move `IEmbeddingModel` into a sub-namespace (`WeaveLLM.Core.Providers.Embeddings`) so the
primary namespace only contains chat/LLM types. Projects that need the WeaveLLM embedding
interface can opt into it explicitly, while those with their own `IEmbeddingModel` are unaffected.

---

### L-2 — `OpenAIChatModel` constructor takes `HttpClient`, not `IHttpClientFactory`

**Severity:** Medium — forces boilerplate, breaks clean DI pattern  
**Discovered in:** Task 3-A

**Problem:**  
Constructor signature: `new OpenAIChatModel(string apiKey, string modelId, string baseUrl, HttpClient httpClient)`.
.NET best practice since .NET Core 2.1 is to inject `IHttpClientFactory` and call `CreateClient(name)`,
which enables connection pooling, resilience policies (Polly), and named-client configuration.
Requiring a concrete `HttpClient` forces callers to call `factory.CreateClient(...)` manually inside
a DI factory delegate — the named-client pre-configuration (base address, auth headers) then has to
be set up separately, duplicating logic.

**Impact:**  
- Named `HttpClient` pre-configuration in `AddHttpClient(...)` is bypassed
- No way to attach Polly retry / circuit-breaker to the chat client without extra wrapping
- Differs from `OpenAIEmbeddingModel` (project-owned, takes `IHttpClientFactory`) — inconsistent pattern

**Suggested fix (options, pick one):**  
a) Add a secondary constructor: `OpenAIChatModel(string apiKey, string modelId, string baseUrl, IHttpClientFactory factory, string clientName)`.  
b) Ship an `AddOpenAIChatModel(this IServiceCollection, IConfiguration)` extension that registers the
named client and the model together, hiding the concrete `HttpClient` internally.

---

### L-3 — No `IServiceCollection` extension methods in `WeaveLLM.Providers`

**Severity:** Medium — every project writes the same boilerplate  
**Discovered in:** Task 3-A

**Problem:**  
`WeaveLLM.Providers` ships no DI helpers. Every project must write and maintain:
```csharp
services.AddSingleton<IChatModel>(sp => {
    var opts    = sp.GetRequiredService<IOptions<...>>().Value;
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var http    = factory.CreateClient("openai-chat");
    return new OpenAIChatModel(opts.ApiKey, opts.ModelId, baseUrl, http);
});
```

**Impact:**  
- 15+ lines of DI registration code that is identical across every consumer project
- Configuration key names and base URLs must be remembered by the caller
- Breaking changes in the constructor propagate to every project's DI registration

**Suggested fix:**  
Ship `WeaveLLM.Extensions.DependencyInjection` (already listed in the ecosystem) with:
```csharp
services.AddOpenAIChatModel(configuration.GetSection("KnowledgeLLM:OpenAI"));
services.AddAnthropicChatModel(configuration.GetSection("Anthropic"));
```
These extensions should register the named `HttpClient`, bind configuration, and wire the
concrete implementation automatically.

---

### L-4 — `IChatModel.ChatAsync` returns `ChainResult<Message>`, not `ChainResult<string>`

**Severity:** Low–Medium — extra unwrapping for common RAG use case  
**Discovered in:** Task 3-A

**Problem:**  
For the most common use case — single-turn completion in a RAG pipeline — callers receive
`ChainResult<Message>` and must call `.Value.Content` to get the answer string. The base interface
`ILanguageModel.CompleteAsync` returns `ChainResult<string>` directly, which is simpler.
The asymmetry between `IChatModel` (structured message) and `ILanguageModel` (plain string)
is not documented, leaving it unclear which interface to use for non-conversational flows.

**Impact:**  
- Callers building single-turn RAG must remember the extra `.Content` unwrap
- The `Message` return value carries fields (ToolCalls, Name, Timestamp) that are never needed
  for simple completions, adding noise to the call site

**Suggested fix (options):**  
a) Add `Task<ChainResult<string>> CompleteAsync(string prompt, LLMOptions, ct)` directly on `IChatModel`
as a convenience method (single-turn, no history needed).  
b) Alternatively, clearly document in the XML summary of `ChatAsync` that for single-turn RAG,
prefer `ILanguageModel.CompleteAsync` instead.

---

### L-5 — `LLMOptions.MaxTokens` is `int?` with undocumented `null` behaviour

**Severity:** Low — silent misconfiguration risk  
**Discovered in:** Task 3-A

**Problem:**  
`MaxTokens` is typed as `int?`. It is not documented whether `null` means:
- "use the provider's own default" (e.g. OpenAI defaults to model max), or
- "send no `max_tokens` field in the request" (same outcome, but different semantics), or
- "unlimited" (could cause unexpectedly large/expensive responses).

Without documentation, callers defensively set `MaxTokens = 1024` rather than relying on a
sensible default, making the nullable typing less useful.

**Suggested fix:**  
Add an XML doc comment to `MaxTokens` stating exactly what `null` does per provider.
Alternatively, add `LLMOptions.Default` (a static instance with production-safe defaults)
and `LLMOptions.Unconstrained` (explicit no-limit opt-in), so intent is clear at the call site.

---

### L-6 — No `IStreamingChatModel` — streaming is undiscoverable on `IChatModel`

**Severity:** Low–Medium — discoverability / documentation gap  
**Discovered in:** Task 3-B

**Problem:**  
Documentation and common assumptions suggest a separate `IStreamingChatModel` for streaming.
In reality, streaming is a method on `IChatModel` (`StreamChatAsync`). A developer looking
for `IStreamingChatModel` via NuGet/IntelliSense will find nothing and may incorrectly
conclude the package does not support streaming.

Additionally, `StreamChatAsync` takes `IReadOnlyList<Message>` (requiring message wrapping),
while `ILanguageModel.StreamCompleteAsync` takes a raw string. For RAG pipelines that build
prompts as strings and pass them through, neither method feels naturally discoverable.

**Impact:**  
- Documentation/task specs written against the expected `IStreamingChatModel` don't compile
- New developers discovering the package via interfaces cannot find streaming capability
- Projects that need simple token streaming must either study the full interface hierarchy or
  trial-and-error their way to `StreamChatAsync`

**Suggested fix (options):**  
a) Add a separate `IStreamingChatModel` interface (`StreamAsync(string, LLMOptions, ct)` → `IAsyncEnumerable<string>`)
for projects that only need streaming. `OpenAIChatModel` can implement both.  
b) Promote `StreamCompleteAsync(string prompt, LLMOptions, ct)` (currently only on the base `ILanguageModel`)
as a first-class method on `IChatModel`, and document when to prefer it over `StreamChatAsync`. Projects
building single-turn RAG prompts as plain strings shouldn't need to construct a `Message` list.  
c) At minimum, add a prominent XML doc on `IChatModel` noting it covers both blocking (`ChatAsync`)
and streaming (`StreamChatAsync`) in one interface.

---

### L-7 — Streaming error handling forces C# language-level workarounds (CS1626)

**Severity:** Medium — affects correctness and code quality for streaming callers  
**Discovered in:** Task 3-B

**Problem:**  
`StreamChatAsync` returns `IAsyncEnumerable<string>` — raw tokens with no error signal.
If the provider encounters an error mid-stream, it throws an exception. In C# iterator
methods (`async IAsyncEnumerable<T>`), `yield return` inside a `try` block that has a `catch`
clause is illegal (CS1626). This means:

- Caller cannot wrap `await foreach (var token in stream)` + `yield return token` in a try-catch
- The only workaround is to manually manage `IAsyncEnumerator<string>` (call `MoveNextAsync()` in
  a try-catch, store the current value, yield it outside the catch block)
- Or buffer all tokens before yielding — which defeats streaming latency benefits

**Impact:**  
- Every streaming consumer that wants graceful error handling needs non-obvious boilerplate
- Projects that follow a "never throw" convention (like this one) cannot honour it for streaming errors
  without significant complexity

**Suggested fix:**  
Provide a `ChainResult`-aware streaming variant:
```csharp
IAsyncEnumerable<ChainResult<string>> StreamChatSafeAsync(
    IReadOnlyList<Message> messages, LLMOptions options, CancellationToken ct);
```
Each yielded item is either `Success(token)` or `Failure(WeaveLLMError)`. Callers can
`await foreach` with simple `if (!chunk.IsSuccess) { /* handle */ break; }` logic, avoiding
exception-based flow control and the CS1626 compiler constraint entirely.

---

### L-8 — `StreamChatAsync` returning `IAsyncEnumerable<string>` makes NSubstitute mocking non-obvious

**Severity:** Low — testing friction; no production impact  
**Discovered in:** Task 3-C

**Problem:**  
`IChatModel.StreamChatAsync` returns `IAsyncEnumerable<string>` directly (not `Task<IAsyncEnumerable<string>>`).
When writing unit tests with NSubstitute, developers used to mocking `Task`-returning methods expect patterns like:

```csharp
// Works for Task-returning methods — does NOT work for IAsyncEnumerable
_chatModel.StreamChatAsync(...).Returns(async ct => someStream);
```

For `IAsyncEnumerable<string>`, NSubstitute's `.Returns()` requires a **concrete instance** of
`IAsyncEnumerable<string>`, not a lambda. The only working approach is a static `async IAsyncEnumerable<string>`
helper method with `[EnumeratorCancellation]` on the `CancellationToken` parameter:

```csharp
// Required pattern — not documented anywhere in the package
private static async IAsyncEnumerable<string> TokenStream(
    IEnumerable<string> tokens,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    foreach (var token in tokens)
    {
        ct.ThrowIfCancellationRequested();
        yield return token;
    }
}

_chatModel.StreamChatAsync(Arg.Any<...>(), Arg.Any<...>(), Arg.Any<CancellationToken>())
          .Returns(TokenStream(new[] { "Hello", " world", "!" }));
```

A second subtlety: the `[EnumeratorCancellation]` attribute on the helper's `ct` parameter is **required** for
cancellation to propagate correctly. When the consuming code wraps the stream with `.WithCancellation(ct)`, the
compiler machinery passes `ct` into `GetAsyncEnumerator(ct)` on the returned enumerable, and
`[EnumeratorCancellation]` causes that token to replace the one supplied at call time. Without it,
cancellation tests appear to pass vacuously (the stream is never interrupted).

**Impact:**  
- Developers writing streaming tests for the first time will waste time discovering the correct mocking pattern
- Incorrect `[EnumeratorCancellation]` omission silently produces tests that never actually exercise cancellation
- No existing example in the package documentation or README

**Suggested fix:**  
Add a `README` section or XML doc note on `StreamChatAsync` stating: "To mock this method in NSubstitute or Moq,
provide a concrete `IAsyncEnumerable<string>` via a static async iterator helper. Mark the helper's
`CancellationToken` parameter with `[EnumeratorCancellation]` to ensure `.WithCancellation(ct)` propagates
correctly."  
Shipping a `WeaveLLM.Testing` package with a pre-built `FakeStreamingChatModel` would eliminate the boilerplate
entirely.

---

## 3. Non-Negotiable Code Conventions

These apply to every file in this repo, no exceptions:

- **Never throw** — all errors returned as `ChainResult.Failure(ChainError.XYZ(...))`
- **CancellationToken** on every async method
- **IHttpClientFactory** for all HTTP — never `new HttpClient()`
- **XML doc comments** on all public members
- **Thread-safe** implementations
- **Test naming**: `{Method}_{Condition}_{ExpectedOutcome}`
- **No secrets in code** — API keys via config or environment variables only

---

## 4. Solution Structure

```
KnowledgeLLM/
├── .github/
│   └── workflows/
│       └── ci.yml                         ← build + test + coverage
├── src/
│   ├── KnowledgeLLM.Core/                 ← all domain logic, no HTTP framework deps
│   │   ├── Chunking/
│   │   │   ├── ITextChunker.cs
│   │   │   ├── TextChunk.cs
│   │   │   └── SlidingWindowChunker.cs    ← Phase 1 concrete impl
│   │   ├── Configuration/
│   │   │   └── KnowledgeLLMOptions.cs     ← Phase 2
│   │   ├── Documents/
│   │   │   ├── Document.cs
│   │   │   ├── IDocumentLoader.cs
│   │   │   └── PlainTextDocumentLoader.cs ← Phase 1 concrete impl
│   │   ├── Embeddings/
│   │   │   ├── IEmbeddingModel.cs
│   │   │   └── OpenAIEmbeddingModel.cs    ← Phase 2
│   │   ├── Extensions/
│   │   │   └── ServiceCollectionExtensions.cs
│   │   ├── Pipeline/
│   │   │   ├── IRagPipeline.cs
│   │   │   ├── RagPipeline.cs
│   │   │   ├── RagAnswer.cs
│   │   │   └── PromptBuilder.cs           ← Phase 2
│   │   ├── Retrieval/
│   │   │   ├── IVectorStore.cs
│   │   │   ├── RetrievalResult.cs
│   │   │   └── InMemoryVectorStore.cs     ← Phase 1 concrete impl
│   │   └── KnowledgeLLM.Core.csproj
│   └── KnowledgeLLM.Api/
│       ├── Controllers/
│       │   └── KnowledgeController.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       ├── Program.cs
│       └── KnowledgeLLM.Api.csproj
├── tests/
│   └── KnowledgeLLM.Core.Tests/
│       ├── Chunking/
│       │   └── SlidingWindowChunkerTests.cs
│       ├── Documents/
│       │   └── PlainTextDocumentLoaderTests.cs
│       ├── Embeddings/
│       │   └── OpenAIEmbeddingModelTests.cs   ← Phase 2
│       ├── Pipeline/
│       │   ├── RagPipelineTests.cs
│       │   ├── RagPipelineIntegrationTests.cs
│       │   └── PromptBuilderTests.cs
│       ├── Retrieval/
│       │   └── InMemoryVectorStoreTests.cs
│       └── KnowledgeLLM.Core.Tests.csproj
├── CLAUDE.md
├── KnowledgeLLM.sln
├── NuGet.Config                               ← remove after Phase 1 (package is live)
└── README.md
```

---

## 5. Architecture: RAG Pipeline

### Index flow
```
Source path
  → IDocumentLoader.LoadAsync()       — reads file(s) from disk
  → ITextChunker.ChunkAsync()         — splits into overlapping TextChunks
  → IEmbeddingModel.EmbedBatchAsync() — converts chunks to float[] vectors
  → IVectorStore.UpsertAsync()        — stores chunk + vector pairs
  → returns: total chunks indexed
```

### Query flow (blocking)
```
Question (string)
  → IEmbeddingModel.EmbedAsync()      — embeds the question
  → IVectorStore.SearchAsync()        — cosine similarity top-K retrieval
  → PromptBuilder.BuildRagPrompt()    — formats grounded prompt
  → IChatModel.ChatAsync()            — generates answer (WeaveLLM.Providers.OpenAI)
  → returns: RagAnswer { Answer, Sources[] }
```

### Query flow (streaming)
```
Question (string)
  → IEmbeddingModel.EmbedAsync()      — embeds the question
  → IVectorStore.SearchAsync()        — cosine similarity top-K retrieval
  → PromptBuilder.BuildRagPrompt()    — formats grounded prompt
  → IChatModel.StreamChatAsync()      — yields string tokens as IAsyncEnumerable<string>
  → yields: tokens one-by-one (error token on pre-stream failure, then stops)
```

### Short-circuit rule
Every stage returns `ChainResult<T>`. If any stage fails, the pipeline
returns that error immediately — no subsequent stages are called.

---

## 6. Key Implementation Details

### SlidingWindowChunker
- Constructor params: `chunkSize = 500`, `overlap = 100`
- Configured via `KnowledgeLLMOptions.Chunker` in Phase 2
- Returns `ChainError.InvalidConfiguration` if `overlap >= chunkSize`

### InMemoryVectorStore
- Storage: `ConcurrentDictionary<string, (TextChunk, float[])>` keyed by chunk Id
- Cosine similarity: `dot(a,b) / (|a| * |b|)`
- `SearchAsync` returns `ChainError.NotFound` if store is empty
- Not persistent — cleared on app restart (replace with pgvector in Phase 4)

### OpenAIEmbeddingModel (Phase 2)
- Named HttpClient: `"openai-embeddings"`
- Endpoint: `POST https://api.openai.com/v1/embeddings`
- Default model: `text-embedding-3-small` (1536 dimensions)
- API key validated at call time — app starts with empty key

### IChatModel via WeaveLLM.Providers (Phase 3+)
- Registered as `IChatModel` backed by `OpenAIChatModel` from `WeaveLLM.Providers.OpenAI`
- `OpenAIChatModel` receives an `HttpClient` from the `"openai-chat"` named client
- See section 2-B for the constructor signature and alias pattern

---

## 7. Configuration

### appsettings.json structure
```json
{
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
```

### Setting the API key (never commit it)
```bash
# Option A — environment variable (recommended for CI/prod)
export KNOWLEDGELLM__OPENAI__APIKEY="sk-..."

# Option B — user secrets (local dev)
dotnet user-secrets set "KnowledgeLLM:OpenAI:ApiKey" "sk-..."
```

---

## 8. API Endpoints

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/knowledge/index` | Index a document or directory |
| `POST` | `/api/knowledge/ask` | Ask a question (blocking, full answer) |
| `POST` | `/api/knowledge/ask/stream` | Ask a question (streaming, Server-Sent Events) |

### Index request/response
```json
// Request
{ "source": "docs/manual.txt" }

// Response 200
{ "chunksIndexed": 42, "source": "docs/manual.txt" }

// Response 400
{ "code": "NotFound", "message": "..." }
```

### Ask request/response
```json
// Request
{ "question": "What is the return policy?", "topK": 5 }

// Response 200
{
  "answer": "The return policy allows...",
  "sources": [
    { "chunkId": "...", "documentId": "docs/manual.txt", "content": "...", "score": 0.91 }
  ]
}
```

### Ask/stream — Server-Sent Events
```
// Request body (same shape as /ask)
{ "question": "What is the return policy?", "topK": 5 }

// Response headers
Content-Type: text/event-stream
Cache-Control: no-cache
X-Accel-Buffering: no

// Response body — one SSE event per token
data: The return
data:  policy allows
data:  returns within 30 days...
data: [DONE]

// On pre-stream failure (embed / search / no sources), a single error event is emitted:
data: [ERROR:NotFound] No relevant context found for the question.
data: [DONE]
```

### Error mapping (blocking endpoints only)
| WeaveLLMError code | HTTP Status |
|---|---|
| `INVALID_INPUT`, `INVALID_CONFIGURATION`, `NOT_FOUND` | 400 |
| All others | 500 |

---

## 9. NuGet Packages

### KnowledgeLLM.Core.csproj
```xml
<PackageReference Include="WeaveLLM.Core" Version="0.1.0-alpha" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.2" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.2" />
<PackageReference Include="Microsoft.Extensions.Http" Version="8.0.1" />
```

### KnowledgeLLM.Core.Tests.csproj
```xml
<PackageReference Include="xunit" Version="2.9.0" />
<PackageReference Include="FluentAssertions" Version="6.12.0" />
<PackageReference Include="NSubstitute" Version="5.1.0" />
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
<PackageReference Include="coverlet.collector" Version="6.0.2" />
```

---

## 10. DI Registration

```csharp
// Program.cs
builder.Services.AddKnowledgeLLM(builder.Configuration);

// What AddKnowledgeLLM registers:
// Scoped:    IRagPipeline         → RagPipeline
// Singleton: IDocumentLoader      → PlainTextDocumentLoader
// Singleton: ITextChunker         → SlidingWindowChunker (from config)
// Singleton: IVectorStore         → InMemoryVectorStore
// Singleton: IEmbeddingModel      → OpenAIEmbeddingModel       (Phase 2+)
// Singleton: IChatModel           → OpenAIChatModel (WeaveLLM)  (Phase 3+)
// Named HttpClients: "openai-embeddings", "openai-chat"          (Phase 2+)
```

---

## 11. Tool Assignment

| Task | Tool | Reason |
|---|---|---|
| Core interfaces + pipeline design | Claude Code | Architecture decisions |
| OpenAI HTTP client implementation | Claude Code | Error handling + HTTP patterns |
| DI wiring + configuration | Claude Code | Cross-cutting, easy to get wrong |
| Unit tests for all classes | GitHub Copilot | Pure pattern fill |
| XML doc comments | GitHub Copilot | Pure boilerplate |
| README polish | GitHub Copilot | No reasoning needed |
| Additional vector store backends | Claude Code | New domain (pgvector, Redis) |
| WeaveLLM.Providers integration | Claude Code | New package, new patterns |

---

## 12. Phase Status

| Phase | Goal | Status |
|---|---|---|
| **1** | Solution skeleton, core interfaces, concrete impls (loader, chunker, vector store), API shell | ✅ Complete |
| **2** | OpenAI embedding, chat completion, configuration, answer generation, integration tests | ✅ Complete |
| **3** | Replace `OpenAIChatClient` with `WeaveLLM.Providers` (`IChatModel`), streaming endpoint | ✅ Complete (3-A ✅ 3-B ✅ 3-C ✅) |
| **4** | Persistent vector store (pgvector), additional doc loaders (PDF, Word), observability | ⬜ Pending |

---

## 13. Known Stubs / TODOs

| File | TODO | Unblocked by |
|---|---|---|
| `InMemoryVectorStore.cs` | Replace with pgvector for persistence | Phase 4 |
| `PlainTextDocumentLoader.cs` | Add PDF + Word loaders | Phase 4 |
| `RagPipeline.cs` | Add OpenTelemetry spans per stage | Phase 5-B |

---

*Update this file at the end of each working session.*
