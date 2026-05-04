# KnowledgeLLM

[![CI](https://github.com/harshil-sh/KnowledgeLLM/actions/workflows/ci.yml/badge.svg)](https://github.com/harshil-sh/KnowledgeLLM/actions/workflows/ci.yml)
[![NuGet – WeaveLLM.Core](https://img.shields.io/badge/nuget-WeaveLLM.Core%200.1.0--alpha-blue)](https://www.nuget.org/packages/WeaveLLM.Core/0.1.0-alpha)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/8)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A lightweight **Retrieval-Augmented Generation (RAG)** API built on .NET 8 and [WeaveLLM.Core](https://www.nuget.org/packages/WeaveLLM.Core/0.1.0-alpha). Point it at a folder of `.txt` or `.pdf` files, call `/index`, then ask questions against the indexed content via `/ask` or stream tokens from `/ask/stream`. The pipeline embeds chunks with OpenAI, stores them in-memory (or in PostgreSQL/pgvector), and grounds answers using a retrieved-context prompt.

---

## Prerequisites

| Requirement | Notes |
|---|---|
| [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) | `dotnet --version` should print `8.*` |
| OpenAI API key | `sk-…` — used for embeddings and chat completion |
| PostgreSQL + pgvector *(optional)* | Only needed when `KnowledgeLLM:PgVector:Enabled` is `true` |

---

## Quick Start

```bash
# 1. Clone
git clone https://github.com/harshil-sh/KnowledgeLLM.git
cd KnowledgeLLM

# 2. Set your API key (stored in user-secrets, never committed)
dotnet user-secrets --project src/KnowledgeLLM.Api \
  set "KnowledgeLLM:OpenAI:ApiKey" "sk-..."

# 3. Run
dotnet run --project src/KnowledgeLLM.Api
# → Swagger UI: http://localhost:5000/swagger
```

### Index documents

```bash
curl -s -X POST http://localhost:5000/api/knowledge/index \
  -H "Content-Type: application/json" \
  -d '{"source": "/path/to/docs"}' | jq
# {"chunksIndexed": 42, "source": "/path/to/docs"}
```

`source` may be a directory (all `.txt` / `.pdf` files are loaded recursively) or a single file path.

### Ask a question

```bash
curl -s -X POST http://localhost:5000/api/knowledge/ask \
  -H "Content-Type: application/json" \
  -d '{"question": "What is the refund policy?", "topK": 5}' | jq
# {"answer": "...", "sources": [...]}
```

### Stream tokens (Server-Sent Events)

```bash
curl -N -X POST http://localhost:5000/api/knowledge/ask/stream \
  -H "Content-Type: application/json" \
  -d '{"question": "Summarise the onboarding guide", "topK": 3}'
# data: The
# data:  onboarding
# ...
# data: [DONE]
```

---

## Configuration

All keys live under the `KnowledgeLLM` section in `appsettings.json` or environment variables (e.g. `KNOWLEDGELLM__OPENAI__APIKEY`).

| Key | Default | Description |
|---|---|---|
| `KnowledgeLLM:OpenAI:ApiKey` | *(required)* | OpenAI API key |
| `KnowledgeLLM:OpenAI:EmbeddingModel` | `text-embedding-3-small` | Embedding model name |
| `KnowledgeLLM:OpenAI:EmbeddingDimensions` | `1536` | Vector dimensions — must match the model |
| `KnowledgeLLM:OpenAI:ChatModel` | `gpt-4o-mini` | Chat completion model name |
| `KnowledgeLLM:Chunker:ChunkSize` | `500` | Max characters per chunk |
| `KnowledgeLLM:Chunker:Overlap` | `100` | Overlap characters between consecutive chunks |
| `KnowledgeLLM:PgVector:Enabled` | `false` | `true` → use PostgreSQL/pgvector; `false` → in-memory |
| `KnowledgeLLM:PgVector:ConnectionString` | *(empty)* | Npgsql connection string (required when enabled) |

**PDF support** is opt-in. Call `services.AddPdfDocumentLoader()` after `AddKnowledgeLLM(...)` in `Program.cs` to enable loading `.pdf` files alongside `.txt`.

---

## Architecture

```
INDEX FLOW
──────────
  source path
    │
    ▼
  IDocumentLoader.LoadAsync()          reads .txt / .pdf files from disk
    │
    ▼
  ITextChunker.ChunkAsync()            sliding-window split → TextChunk[]
    │
    ▼
  IEmbeddingModel.EmbedBatchAsync()    OpenAI embeddings → float[][]
    │
    ▼
  IVectorStore.UpsertAsync()           store (chunk, vector) pairs
    │
    ▼
  int  (chunks indexed)


QUERY FLOW
──────────
  question (string)
    │
    ▼
  IEmbeddingModel.EmbedAsync()         embed the question → float[]
    │
    ▼
  IVectorStore.SearchAsync()           cosine similarity, top-K results
    │
    ▼
  PromptBuilder.BuildRagPrompt()       format grounded prompt (static)
    │
    ▼
  IChatModel.ChatAsync()               OpenAI chat completion
    │
    ▼
  RagAnswer { Answer, Sources[] }
```

`RagPipeline` orchestrates both flows. Any stage failure short-circuits immediately — no subsequent stages run.

---

## Project Structure

```
KnowledgeLLM/
├── src/
│   ├── KnowledgeLLM.Api/
│   │   ├── Controllers/
│   │   │   └── KnowledgeController.cs   # POST /index  /ask  /ask/stream
│   │   └── Program.cs
│   └── KnowledgeLLM.Core/
│       ├── Chunking/                    # SlidingWindowChunker
│       ├── Configuration/               # KnowledgeLLMOptions (+ PgVectorOptions)
│       ├── Documents/                   # PlainTextDocumentLoader, PdfDocumentLoader,
│       │                                #   CompositeDocumentLoader
│       ├── Embeddings/                  # OpenAIEmbeddingModel
│       ├── Extensions/                  # AddKnowledgeLLM(), AddPdfDocumentLoader()
│       ├── Pipeline/                    # RagPipeline, PromptBuilder
│       └── Retrieval/                   # InMemoryVectorStore, PgVectorStore
└── tests/
    └── KnowledgeLLM.Core.Tests/         # xUnit — mirrors src/KnowledgeLLM.Core/
```

---

## Roadmap

| Phase | Status | Scope |
|---|---|---|
| 1 | ✅ Complete | Core interfaces, plain-text loader, sliding-window chunker, in-memory vector store, API shell |
| 2 | ✅ Complete | OpenAI embedding model, config binding, HTTP client factory wiring |
| 3 | ✅ Complete | `IChatModel` via `WeaveLLM.Providers` (OpenAIChatModel), streaming SSE endpoint |
| 4 | ✅ Complete | PostgreSQL/pgvector store, PDF document loader, composite loader DI extension |
| 5 | ⏳ Pending | PDF/Word loader improvements, OpenTelemetry observability, rate-limit retry policy |

---

## Contributing

1. Fork and create a feature branch
2. Run `dotnet test` — all tests must pass
3. Follow the conventions in [`CLAUDE.md`](CLAUDE.md): no exceptions, `CancellationToken` on every async method, XML doc on all public members
4. Open a pull request against `main`

---

## License

MIT © Harshil Shah — see [LICENSE](LICENSE) for full text.
