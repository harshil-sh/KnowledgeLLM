# KnowledgeLLM — Project Knowledge Base

> Last updated: 17 May 2026 (L-1 partial resolution; L-2 + L-3 resolved; full `WeaveLLMError` factory method inventory reflected from 0.2.1-alpha DLL — L-10 fully resolved; `RagPipeline.cs` streaming tokens and `RagPipeline*Tests.cs` all updated to SCREAMING_SNAKE_CASE; `OpenAIEmbeddingModel.cs` 6 manual `new WeaveLLMError(...)` calls replaced with factory methods — entire codebase now fully clean)
> Single source of truth for project context, architecture, and conventions.

---

## 1. Project Identity

| Field | Value |
|---|---|
| **Repo Name** | KnowledgeLLM |
| **Description** | Ask questions across your documents — a RAG pipeline built on WeaveLLM.Core for .NET |
| **Type** | Real application (not a sample, not a library) |
| **Platform** | .NET 8 / C# |
| **Core Dependency** | `WeaveLLM.Core 0.2.0-alpha` (NuGet — never ProjectReference) |
| **Current Stage** | Phase 4 in progress (4-A done) |

---

## 2. Dependency: WeaveLLM.Core

Published at: https://www.nuget.org/packages/WeaveLLM.Core/0.2.0-alpha

### Key types used in KnowledgeLLM

| Type | Purpose |
|---|---|
| `ChainResult<T>` | Discriminated union — `Success(T)` or `Failure(WeaveLLMError)`. Properties: `.IsSuccess bool`, `.Value T`, `.Error WeaveLLMError`. |
| `WeaveLLMError` | Structured error. Properties: `.Code string`, `.Message string`. Factory: `WeaveLLMError.InvalidInput(msg)`. Constructor: `new WeaveLLMError(string msg, string code, Exception? inner = null)`. |
| `IChain<TIn,TOut>` | `ExecuteAsync(TIn, CancellationToken) → Task<ChainResult<TOut>>` |

### WeaveLLM ecosystem packages (all 0.2.0-alpha)

```
WeaveLLM.Core                              ← used now (models, ChainResult, error types)
WeaveLLM.Providers                         ← used now (OpenAIChatModel — Phase 3+)
WeaveLLM.Memory                            ← not used — Phase 4 uses Npgsql + pgvector directly
WeaveLLM.Observability                     ← OpenTelemetry (Phase 4)
WeaveLLM.Extensions.DependencyInjection    ← fluent DI builder (0.2.0-alpha now published)
```

---

## 2-B. WeaveLLM Package: Discovered API Reference

> Learned during Phase 3 (tasks 3-A, 3-B) and updated for 0.2.0-alpha (07 May 2026) by reflecting the DLLs directly.
> Read this section before touching any WeaveLLM type to avoid repeating the same mistakes.

### Package layout — interfaces vs. implementations (0.2.1-alpha)

| Package | What it contains |
|---|---|
| `WeaveLLM.Core` | `ChainResult<T>`, `WeaveLLMError`, `ChatResponse`, `Message`, `LLMOptions`, `Role` (all in `WeaveLLM.Core.Models`) |
| `WeaveLLM.Core.Providers` (namespace inside `WeaveLLM.Core.dll`) | Provider **interfaces** only: `IChatModel`, `ILanguageModel`, `IStreamingChatModel` — `IEmbeddingModel` moved to `WeaveLLM.Core.Providers.Embeddings` (L-1 fix) |
| `WeaveLLM.Core.Providers.Embeddings` | `IEmbeddingModel` — canonical location since L-1 fix |
| `WeaveLLM.Providers` | Concrete implementations: `OpenAIChatModel` (now accepts `IHttpClientFactory` — L-2 fix), `AnthropicChatModel` |
| `WeaveLLM.Extensions.DependencyInjection` | DI extension methods — `AddWeaveLLM()` → `WeaveLLMBuilder`, then `.AddOpenAI(apiKey, modelId?)` (L-3 fix) |

**Breaking change from 0.1.0-alpha:** `Message`, `LLMOptions`, and `MessageRole` moved from `WeaveLLM.Core.Providers` into `WeaveLLM.Core.Models`. `MessageRole` was renamed to `Role`.

### Full interface inventory (0.2.0-alpha)

| Type | Namespace | Kind | Key members |
|---|---|---|---|
| `IChatModel` | `WeaveLLM.Core.Providers` | Interface | `ChatAsync(IReadOnlyList<Message>, LLMOptions, ct) → Task<ChainResult<ChatResponse>>`<br>`StreamChatAsync(IReadOnlyList<Message>, LLMOptions, ct) → IAsyncEnumerable<string>` |
| `ILanguageModel` | `WeaveLLM.Core.Providers` | Interface (base of `IChatModel`) | `CompleteAsync(string, LLMOptions, ct) → Task<ChainResult<string>>`<br>`StreamCompleteAsync(string, LLMOptions, ct) → IAsyncEnumerable<string>` |
| `IEmbeddingModel` | `WeaveLLM.Core.Providers` AND `WeaveLLM.Core.Models` | Interface | ⚠️ exists in TWO namespaces — see collision note below |
| `LLMOptions` | `WeaveLLM.Core.Models` | Class | `Temperature double?`, `MaxTokens int?`, `StopSequences`, `ProviderSpecific Dictionary<string,object>` (moved from `WeaveLLM.Core.Providers` in 0.2.0) |
| `Message` | `WeaveLLM.Core.Models` | Class | `Role Role`, `Content string`, static factories: `Message.User(content)`, `Message.System(content)`, `Message.Assistant(content)` (moved from `WeaveLLM.Core.Providers` in 0.2.0) |
| `Role` | `WeaveLLM.Core.Models` | Enum | `System`, `User`, `Assistant`, `Tool` (renamed from `MessageRole` in 0.2.0) |
| `ChatResponse` | `WeaveLLM.Core.Models` | Class | `Content string`, `FinishReason string`, `Usage UsageStats`, `ToolCalls IReadOnlyList<ToolCall>` (NEW in 0.2.0 — replaces `Message` as `ChatAsync` return value) |

### Namespace collision status — `WeaveLLM.Core.Providers` risk resolved (L-1 partial fix)

`WeaveLLM.Core.Providers.IEmbeddingModel` has been **removed** from the `WeaveLLM.Core.Providers` namespace and relocated to `WeaveLLM.Core.Providers.Embeddings`. A bare `using WeaveLLM.Core.Providers;` no longer produces CS0104 from that specific collision.

`WeaveLLM.Core.Models.IEmbeddingModel` status is **unverified** in the updated package — until confirmed removed, keep the explicit `IEmbeddingModel` alias to guard against a potential `using WeaveLLM.Core.Models;` collision.

**Rule: keep type aliases in all files that reference WeaveLLM types.**

```csharp
// Active alias pattern (L-1 partial fix applied — WeaveLLM.Core.Providers collision resolved)
using IChatModel      = WeaveLLM.Core.Providers.IChatModel;
using IEmbeddingModel = KnowledgeLLM.Core.Embeddings.IEmbeddingModel;  // still required while WeaveLLM.Core.Models.IEmbeddingModel status unverified
using LLMMessage      = WeaveLLM.Core.Models.Message;
using LLMOptions      = WeaveLLM.Core.Models.LLMOptions;
// MessageRole alias removed — use LLMMessage.User(prompt) factory instead

// If you ever need WeaveLLM's own embedding interface, use the canonical location:
// using IWeaveLLMEmbeddingModel = WeaveLLM.Core.Providers.Embeddings.IEmbeddingModel;
```

Drop the `IEmbeddingModel` alias only when WeaveLLM confirms `WeaveLLM.Core.Models.IEmbeddingModel` is also removed. See L-1.

Applies to: `RagPipeline.cs`, and the three `RagPipeline*Tests.cs` files. `ServiceCollectionExtensions.cs` does not open `WeaveLLM.Core.Models` so it can safely use `using KnowledgeLLM.Core.Embeddings;` directly.

### `IChatModel.ChatAsync` returns `ChainResult<ChatResponse>`, not `ChainResult<Message>`

**Breaking change from 0.1.0-alpha.** `ChatResponse.Content` still returns the answer string.

```csharp
var messages = new List<LLMMessage> { LLMMessage.User(prompt) };   // ← factory method, no MessageRole needed
var chatResult = await _chatModel.ChatAsync(messages, new LLMOptions { MaxTokens = 1024 }, ct);
if (!chatResult.IsSuccess)
    return ChainResult<RagAnswer>.Failure(chatResult.Error);
return ChainResult<RagAnswer>.Success(new RagAnswer(chatResult.Value.Content, sources));
```

NSubstitute mocks must now return `ChainResult<ChatResponse>`:
```csharp
_chatModel.ChatAsync(Arg.Any<IReadOnlyList<LLMMessage>>(), Arg.Any<LLMOptions>(), Arg.Any<CancellationToken>())
          .Returns(ChainResult<ChatResponse>.Success(new ChatResponse { Content = "answer" }));
```

### Streaming — `IChatModel.StreamChatAsync` (no separate `IStreamingChatModel`)

Unchanged from 0.1.0-alpha. Streaming is built directly into `IChatModel`:

```csharp
var messages = new List<LLMMessage> { LLMMessage.User(prompt) };
await foreach (var token in _chatModel.StreamChatAsync(messages, new LLMOptions { MaxTokens = 1024 }, ct)
                                      .WithCancellation(ct))
{
    yield return token;
}
```

`ILanguageModel.StreamCompleteAsync(string, LLMOptions, ct)` also exists for single-turn plain-string flows.

### `IChatModel` DI registration — use `AddWeaveLLM().AddOpenAI()` (L-2 + L-3 fix, 0.2.1-alpha)

`WeaveLLM.Extensions.DependencyInjection 0.2.1-alpha` resolves both L-2 and L-3. The manual `IChatModel` factory delegate is replaced by the fluent builder:

```csharp
// WeaveLLM builder config keys: ApiKey (matches), ModelId (our key is ChatModel — use string overload)
services.AddWeaveLLM()
        .AddOpenAI(
            configuration["KnowledgeLLM:OpenAI:ApiKey"] ?? string.Empty,
            configuration["KnowledgeLLM:OpenAI:ChatModel"] ?? "gpt-4o-mini");
```

Reads `configuration["..."]` directly (not from `IOptions<>`) because the builder runs at registration time, not request time. The `"openai-chat"` named HttpClient registration is no longer needed — the extension manages its own HttpClient via `IHttpClientFactory` internally (L-2 fix).

**`WeaveLLMBuilder.AddOpenAI` overloads:**
- `AddOpenAI(string apiKey, string? modelId = null)` — string params, use when config key names differ
- `AddOpenAI(IConfiguration section)` — reads `ApiKey` and `ModelId` from the section (our key is `ChatModel`, not `ModelId` — **do not use this overload**)

**`AddWeaveLLM` overloads:**
- `AddWeaveLLM()` — no config auto-scan; use this
- `AddWeaveLLM(IConfiguration)` — auto-registers from `WeaveLLM:*` root section (not our config shape)

The old `OpenAIChatModel` constructor (`new OpenAIChatModel(apiKey, modelId, baseUrl, HttpClient)`) is now deprecated in favour of the DI extension. If you ever need to construct it manually (e.g. in tests), use:
```csharp
new OpenAIChatModel(string apiKey, string modelId, string baseUrl, IHttpClientFactory factory, string clientName)
```

### `WeaveLLMError` is the real error type — error code case is split by origin

The actual error type is `WeaveLLMError` (namespace `WeaveLLM.Core.Models`), not `ChainError`.

**All error codes are SCREAMING_SNAKE_CASE — use factory methods exclusively (full inventory reflected from 0.2.1-alpha DLL):**

| Factory method | Signature | Code |
|---|---|---|
| `WeaveLLMError.InvalidInput` | `(string message)` | `"INVALID_INPUT"` |
| `WeaveLLMError.InvalidConfiguration` | `(string message)` | `"INVALID_CONFIGURATION"` |
| `WeaveLLMError.NotFound` | `(string message)` | `"NOT_FOUND"` |
| `WeaveLLMError.AuthenticationFailed` | `(string message)` | `"AUTHENTICATION_FAILED"` |
| `WeaveLLMError.RateLimitExceeded` | `(string message)` | `"RATE_LIMIT_EXCEEDED"` |
| `WeaveLLMError.ProviderError` | `(string provider, string message)` or `(..., Exception inner)` | `"PROVIDER_ERROR"` |
| `WeaveLLMError.Cancelled` | `(string message, Exception inner)` | `"CANCELLED"` |
| `WeaveLLMError.NetworkTimeout` | `(string message, Exception inner)` | `"NETWORK_TIMEOUT"` |
| `WeaveLLMError.RateLimited` | `(string provider)` | `"RATE_LIMITED"` |
| `WeaveLLMError.Timeout` | `(string chainName)` | `"TIMEOUT"` |

All production source files use factory methods exclusively — including `OpenAIEmbeddingModel.cs` (6 manual constructions replaced 17 May 2026). `KnowledgeController.MapError` checks SCREAMING_SNAKE_CASE codes (`"INVALID_INPUT"`, `"INVALID_CONFIGURATION"`, `"NOT_FOUND"`). L-9 and L-10 fully resolved.

**Rule:** never use `new WeaveLLMError(msg, "SomeCode", ex)` directly — always use a factory method.

### Mocking `IChatModel` in tests (NSubstitute)

```csharp
var chatModel = Substitute.For<IChatModel>();

// Blocking chat
chatModel.ChatAsync(Arg.Any<IReadOnlyList<LLMMessage>>(), Arg.Any<LLMOptions>(), Arg.Any<CancellationToken>())
         .Returns(ChainResult<LLMMessage>.Success(new LLMMessage { Role = MessageRole.Assistant, Content = "answer" }));

// Streaming — DO NOT use .ToAsyncEnumerable() — it does not propagate cancellation (see L-8)
// Use a static async iterator helper with [EnumeratorCancellation] instead:
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

chatModel.StreamChatAsync(Arg.Any<IReadOnlyList<LLMMessage>>(), Arg.Any<LLMOptions>(), Arg.Any<CancellationToken>())
         .Returns(TokenStream(new[] { "Hello", " world", "!" }));
```

---

## 2-C. WeaveLLM Package Limitations & NuGet Roadmap Feedback

> Structured findings from dog-fooding `WeaveLLM.Core 0.1.0-alpha` and `WeaveLLM.Providers 0.1.0-alpha`
> inside a real .NET 8 RAG application. Intended as actionable feedback for package evolution.

---

### L-1 — Namespace collision: `WeaveLLM.Core.Providers.IEmbeddingModel`

**Severity:** High — breaks compilation on first import  
**Discovered in:** Task 3-A  
**Status:** **Providers collision resolved (08 May 2026)** — `WeaveLLM.Core.Providers.IEmbeddingModel` has been removed from the old namespace and relocated to `WeaveLLM.Core.Providers.Embeddings.IEmbeddingModel`. A bare `using WeaveLLM.Core.Providers;` no longer produces CS0104. `WeaveLLM.Core.Models.IEmbeddingModel` removal is **unverified** — full L-1 resolution requires confirming that namespace is also clean.

**Problem:**  
`WeaveLLM.Core.Providers` exported `IEmbeddingModel`. Any project that also defines its own
embedding interface (a common pattern in layered architectures) could not open this namespace
without hitting CS0104. The only workaround was to never use `using WeaveLLM.Core.Providers;`
and instead alias every type individually.

**Current status (08 May 2026):**

| Fully qualified name | Status |
|---|---|
| `WeaveLLM.Core.Providers.IEmbeddingModel` | **Removed** — moved to `Embeddings` sub-namespace (L-1 fix) |
| `WeaveLLM.Core.Providers.Embeddings.IEmbeddingModel` | **New canonical location** |
| `WeaveLLM.Core.Models.IEmbeddingModel` | Status unverified — may still collide if `using WeaveLLM.Core.Models;` is opened |

**Impact of the fix on KnowledgeLLM:**  
Zero code changes required. All affected files already alias `IEmbeddingModel = KnowledgeLLM.Core.Embeddings.IEmbeddingModel` and never open `WeaveLLM.Core.Providers` as a bare namespace. `dotnet build` passes with 0 errors before and after the package update.

**Remaining action (when `WeaveLLM.Core.Models.IEmbeddingModel` removal is confirmed):**  
- Replace `using IEmbeddingModel = KnowledgeLLM.Core.Embeddings.IEmbeddingModel;` with `using KnowledgeLLM.Core.Embeddings;` in `RagPipeline.cs` and the 3 test files
- If any file ever needs WeaveLLM's own embedding interface, use `using IWeaveLLMEmbeddingModel = WeaveLLM.Core.Providers.Embeddings.IEmbeddingModel;`

**Suggested fix (original):**  
Move `IEmbeddingModel` into a sub-namespace (`WeaveLLM.Core.Providers.Embeddings`) so the
primary namespace only contains chat/LLM types — **done for `WeaveLLM.Core.Providers`; `WeaveLLM.Core.Models.IEmbeddingModel` removal still pending for full closure.**

---

### L-2 — `OpenAIChatModel` constructor takes `HttpClient`, not `IHttpClientFactory`

**Severity:** Medium — forces boilerplate, breaks clean DI pattern  
**Discovered in:** Task 3-A  
**Resolved (08 May 2026):** `WeaveLLM.Providers 0.2.1-alpha` adds an `IHttpClientFactory` overload. Use `AddWeaveLLM().AddOpenAI()` from `WeaveLLM.Extensions.DependencyInjection` — the raw `HttpClient` constructor is deprecated.

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
**Resolved (08 May 2026):** `WeaveLLM.Extensions.DependencyInjection 0.2.1-alpha` ships the fluent builder. See Section 2-B for the `AddWeaveLLM().AddOpenAI()` pattern and the config key mapping note.

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

### L-9 — `WeaveLLMError` factory methods produce SCREAMING_SNAKE_CASE codes that conflict with PascalCase conventions

**Severity:** High — silent HTTP status miscategorisation in production  
**Discovered in:** Task 3-A cross-checked against task 3-C tests

**Problem:**  
`WeaveLLMError` static factory methods (e.g. `WeaveLLMError.InvalidInput(msg)`) produce
SCREAMING_SNAKE_CASE error codes (`"INVALID_INPUT"`, `"INVALID_CONFIGURATION"`, etc.).
The `KnowledgeController.MapError` method checks for PascalCase strings (`"InvalidInput"`,
`"InvalidConfiguration"`, `"NotFound"`) to decide between HTTP 400 and 500.

Because the codes never match, every error created via a factory method silently becomes
an HTTP 500, regardless of whether the root cause is a client error (400) or a server
error (500). `OpenAIEmbeddingModel` — the component most likely to return validation errors
to the end user — uses factory methods exclusively.

**Confirmed by:**  
`RagPipelineTests.cs`: `result.Error.Code.Should().Be("INVALID_INPUT")` (factory-method output).  
`KnowledgeController.cs`: `code is "InvalidInput" or "InvalidConfiguration" or "NotFound"` (PascalCase check).

**Impact:**  
- All `WeaveLLMError.InvalidInput()` results are mapped to HTTP 500 instead of 400
- Client-error feedback (bad request body, empty API key) looks like a server fault to callers
- Error is silent — nothing in the pipeline logs the wrong status code mapping
- Inconsistency between files makes it hard to predict what HTTP status any given error produces

**Resolved (07 May 2026):**  
All production files (`InMemoryVectorStore`, `PgVectorStore`, `RagPipeline`, `PdfDocumentLoader`,
`PlainTextDocumentLoader`, `CompositeDocumentLoader`) migrated to factory methods.
`KnowledgeController.MapError` updated to check SCREAMING_SNAKE_CASE codes.
All test assertions updated to match. 172/172 tests pass.

**Suggested fix (two options):**  
a) Update `KnowledgeController.MapError` to check SCREAMING_SNAKE_CASE codes to align with the factory methods:
   `code is "INVALID_INPUT" or "INVALID_CONFIGURATION" or "NOT_FOUND"`.  
b) Alternatively, update the factory methods in `WeaveLLM.Core` to emit PascalCase codes and ship a minor version bump.  
Either way, standardise on one casing across the entire package and document it in the README.

---

### L-10 — `WeaveLLMError` only provides factory methods for validation errors; all operational error codes are unconstrained strings

**Severity:** Medium — typo-prone, no type safety, inconsistent naming across the codebase  
**Discovered in:** Phase 2 (`OpenAIEmbeddingModel`), Phase 4 (`PgVectorStore`, `PdfDocumentLoader`, `CompositeDocumentLoader`)

**Problem:**  
`WeaveLLMError` ships exactly two static factory methods: `InvalidInput(msg)` and `InvalidConfiguration(msg)`.
All other error codes — `"Cancelled"`, `"ProviderError"`, `"NotFound"`, `"AUTHENTICATION_FAILED"`,
`"RATE_LIMIT_EXCEEDED"`, `"NETWORK_TIMEOUT"` — must be expressed as raw string literals in
`new WeaveLLMError(msg, "code-string", ex)`.

This creates three compounding problems:

1. **No type safety** — a single typo (`"Cancelled"` vs `"CANCELLED"`) compiles cleanly but
   routes to the wrong HTTP status at runtime, with no warning.
2. **No discoverability** — developers reading the interface cannot tell what codes the package
   expects, nor which ones its implementations may produce.
3. **No consistency** — across six components in this project the same concept ("operation was
   cancelled") is spelled differently because there is no canonical source of truth.

**Resolved (07 May 2026):** All files now use factory methods exclusively — SCREAMING_SNAKE_CASE throughout:

| File | Factory method used | Code produced |
|---|---|---|
| `PdfDocumentLoader.cs` | `WeaveLLMError.Cancelled(msg, ex)` | `"CANCELLED"` |
| `PlainTextDocumentLoader.cs` | `WeaveLLMError.Cancelled(msg, ex)` | `"CANCELLED"` |
| `PgVectorStore.cs` | `WeaveLLMError.Cancelled(msg, ex)` | `"CANCELLED"` |
| `OpenAIEmbeddingModel.cs` | `WeaveLLMError.Cancelled(msg, ex)` | `"CANCELLED"` |
| `PdfDocumentLoader.cs` | `WeaveLLMError.ProviderError("PdfPig", msg, ex)` | `"PROVIDER_ERROR"` |
| `PgVectorStore.cs` | `WeaveLLMError.ProviderError("PostgreSQL", msg, ex)` | `"PROVIDER_ERROR"` |

The inconsistency is eliminated. `KnowledgeController.MapError` now checks SCREAMING_SNAKE_CASE codes.

**Impact:**  
- `KnowledgeController.MapError` may silently route the wrong HTTP status for cancellation and
  provider errors depending on which component raised them
- Refactoring an error code (e.g. standardising `"Cancelled"` → `"CANCELLED"`) requires a
  grep-and-replace across the entire consumer project with no compiler safety net
- Every new component author must study existing files to infer the "right" casing convention,
  and the current files give contradictory signals

**Suggested fix (options):**  
a) Add static factory methods for every standard operational error code:
```csharp
WeaveLLMError.NotFound(msg)              // → "NOT_FOUND"
WeaveLLMError.Cancelled(msg, ex?)        // → "CANCELLED"
WeaveLLMError.ProviderError(msg, ex?)    // → "PROVIDER_ERROR"
WeaveLLMError.AuthenticationFailed(msg)  // → "AUTHENTICATION_FAILED"
WeaveLLMError.RateLimitExceeded(msg)     // → "RATE_LIMIT_EXCEEDED"
WeaveLLMError.NetworkTimeout(msg, ex?)   // → "NETWORK_TIMEOUT"
```
b) Alternatively, ship a `WellKnownErrorCodes` static class so callers can write
`new WeaveLLMError(msg, WellKnownErrorCodes.Cancelled, ex)` without guessing casing.  
Either way, all factory methods and constants must use the **same casing** (SCREAMING_SNAKE_CASE
preferred as it matches the existing `InvalidInput` output) and be documented in the README.

---

### L-11 — `WeaveLLM.Core 0.2.0-alpha` breaks compilation: three types moved namespace, one renamed, `ChatAsync` return type changed

**Severity:** High — hard compile errors on upgrade; no deprecation warnings precede them  
**Discovered in:** WeaveLLM 0.2.0-alpha upgrade (07 May 2026)

**Problem:**  
`WeaveLLM.Core 0.2.0-alpha` contains multiple co-located breaking changes with no deprecation bridge:

1. `WeaveLLM.Core.Providers.Message` → moved to `WeaveLLM.Core.Models.Message`
2. `WeaveLLM.Core.Providers.LLMOptions` → moved to `WeaveLLM.Core.Models.LLMOptions`
3. `WeaveLLM.Core.Providers.MessageRole` → renamed and moved to `WeaveLLM.Core.Models.Role`
4. `IChatModel.ChatAsync` return type changed from `ChainResult<Message>` to `ChainResult<ChatResponse>`
5. `Microsoft.Extensions.*` minimum versions bumped to 9.0.0 (from 8.x), triggering NU1605 downgrade errors

All five changes hit simultaneously on package version bump with no runtime fallback, no `[Obsolete]` attributes, and no migration notes in the package README.

**Impact:**  
- Every file using WeaveLLM chat types had hard compile errors — `RagPipeline.cs`, `ServiceCollectionExtensions.cs`, and all three test files
- The `Microsoft.Extensions.*` bump requires explicit package version updates in consumer projects (NU1605 is treated as an error by default in this solution)
- Tests mocking `ChatAsync` must be updated from `ChainResult<LLMMessage>` to `ChainResult<ChatResponse>` — a type-level change not caught until build
- The `WeaveLLM.Core.Models.IEmbeddingModel` now appears in a second namespace, worsening the existing CS0104 collision (L-1) — any file with `using WeaveLLM.Core.Models;` now requires the `IEmbeddingModel` alias even if it wasn't needed before

**Workaround (applied):**  
- Updated type aliases to `WeaveLLM.Core.Models.*` namespaces across all affected files
- Added `using IEmbeddingModel = KnowledgeLLM.Core.Embeddings.IEmbeddingModel;` alias and removed bare `using KnowledgeLLM.Core.Embeddings;` in affected files
- Replaced `new LLMMessage { Role = MessageRole.User, Content = prompt }` initialiser pattern with `LLMMessage.User(prompt)` factory (avoids the `Role` enum entirely)
- Updated `Microsoft.Extensions.*` pins to `9.0.0` in `KnowledgeLLM.Core.csproj`

**Suggested fix:**  
Provide a `#pragma warning` shim release (a 0.1.x patch) that re-exports renamed types as `[Obsolete]` aliases pointing to the new locations, allowing consumer projects to migrate incrementally. Alternatively, commit to a proper semver major bump (1.0.0) for breaking API changes rather than using alpha patch increments.

---

These apply to every file in this repo, no exceptions:

- **Never throw** — all errors returned as `ChainResult<T>.Failure(new WeaveLLMError(msg, code, inner?))` or via a factory method
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
│   │   │   ├── InMemoryVectorStore.cs     ← Phase 1 concrete impl
│   │   │   └── PgVectorStore.cs           ← Phase 4 (Npgsql + pgvector)
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

### PgVectorStore (Phase 4)

- NuGet: `Npgsql 8.0.5`, `Pgvector 0.3.2` — `Pgvector` includes `UseVector()` extension for `NpgsqlDataSourceBuilder`
- Constructor: `PgVectorStore(string connectionString, int dimensions)` — validates args, builds `NpgsqlDataSource` with `UseVector()`
- Schema created lazily on first use (double-checked lock via `SemaphoreSlim`): table `chunks` with JSONB `metadata` and `vector(N)` `embedding`; index `chunks_embedding_idx` (ivfflat, cosine ops)
- Cosine similarity returned as `1.0 - (embedding <=> $query)` — pgvector `<=>` is cosine *distance*
- `TextChunk.Index` is not stored in the DB schema; reconstructed as `0` on retrieval (not needed for RAG use)
- Requires `CREATE EXTENSION IF NOT EXISTS vector;` to be run in the target Postgres database before first use
- DI wire-up deferred to task 4-D (currently must be constructed directly)

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

`KnowledgeController.MapError` checks the code string literally — case matters.

| `WeaveLLMError.Code` value (exact string) | HTTP Status |
|---|---|
| `"INVALID_INPUT"`, `"INVALID_CONFIGURATION"`, `"NOT_FOUND"` | 400 |
| All others (`"CANCELLED"`, `"PROVIDER_ERROR"`, etc.) | 500 |

All production code uses factory methods (SCREAMING_SNAKE_CASE). Controller updated to match. L-9 resolved.

---

## 9. NuGet Packages

### KnowledgeLLM.Core.csproj
```xml
<PackageReference Include="WeaveLLM.Core" Version="0.2.1-alpha" />
<PackageReference Include="WeaveLLM.Providers" Version="0.2.1-alpha" />
<PackageReference Include="WeaveLLM.Extensions.DependencyInjection" Version="0.2.1-alpha" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Http" Version="9.0.0" />
<PackageReference Include="OpenTelemetry" Version="1.10.0" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.10.0" />
<PackageReference Include="Npgsql" Version="8.0.5" />
<PackageReference Include="Pgvector" Version="0.3.2" />
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
| **4** | Persistent vector store (pgvector), additional doc loaders (PDF, Word), observability | 🔄 In progress (4-A ✅) |

---

## 13. Known Stubs / TODOs

| File | TODO | Unblocked by |
|---|---|---|
| `InMemoryVectorStore.cs` | Replace with `PgVectorStore` via DI (task 4-D) | Phase 4 |
| `PlainTextDocumentLoader.cs` | Add PDF + Word loaders | Phase 4 |
| `RagPipeline.cs` | Add OpenTelemetry spans per stage | Phase 5-B |

---

*Update this file at the end of each working session.*
