# KnowledgeLLM

[![CI](https://github.com/harshil-sh/KnowledgeLLM/actions/workflows/ci.yml/badge.svg)](https://github.com/harshil-sh/KnowledgeLLM/actions/workflows/ci.yml)
[![NuGet – WeaveLLM.Core](https://img.shields.io/badge/nuget-WeaveLLM.Core%200.1.0--alpha-blue)](https://www.nuget.org/packages/WeaveLLM.Core/0.1.0-alpha)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/8)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**KnowledgeLLM** is a lightweight RAG (Retrieval-Augmented Generation) API built on .NET 8 and [WeaveLLM.Core](https://www.nuget.org/packages/WeaveLLM.Core/0.1.0-alpha). Point it at a folder of `.txt`, `.pdf`, or `.docx` files, call `/index` to embed and store document chunks, then call `/ask` (or `/ask/stream` for Server-Sent Events) to get answers grounded in that content — powered by OpenAI embeddings and chat completion, with an optional PostgreSQL/pgvector backend for persistent storage.

---

## Why this project exists

KnowledgeLLM exists to demonstrate a practical, document-grounded question answering application built on top of [WeaveLLM.Core](https://www.nuget.org/packages/WeaveLLM.Core/0.1.0-alpha). Rather than treating retrieval-augmented generation as an abstract pattern, the API gives teams a focused workflow: index local `.txt`, `.pdf`, and `.docx` knowledge sources, retrieve the most relevant chunks for a user question, and generate an answer that is explicitly grounded in those retrieved sources.

This makes the project useful for scenarios such as internal policy lookup, onboarding guides, support knowledge bases, and technical documentation assistants where answers should stay tied to source material instead of relying on model memory alone.

---

## Production-Oriented Capabilities

KnowledgeLLM is intentionally structured as more than a local RAG prototype:

- **CI/CD:** GitHub Actions runs the repository test suite on every change through the `ci.yml` workflow surfaced by the README badge.
- **Automated testing:** xUnit tests cover the core RAG building blocks, including chunking, document loading, prompt construction, pipeline behavior, and vector-store retrieval.
- **Environment-based configuration:** All runtime settings are bound from the `KnowledgeLLM` configuration section and can be supplied through `appsettings.json`, user-secrets, or environment variables such as `KNOWLEDGELLM__OPENAI__APIKEY`.
- **PostgreSQL/pgvector persistence:** The vector store can run in ephemeral in-memory mode for development or persist embeddings and chunks in PostgreSQL with pgvector when `KnowledgeLLM:PgVector:Enabled` is set to `true`.
- **SSE streaming:** The `/api/knowledge/ask/stream` endpoint streams generated answer tokens with Server-Sent Events for responsive client experiences.
- **Source-grounded responses:** `/api/knowledge/ask` returns both the generated answer and the retrieved source chunks used to ground that answer, making responses easier to inspect and validate.

---

## Prerequisites

| Requirement | Notes |
|---|---|
| [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) | `dotnet --version` → `8.*` |
| OpenAI API key | `sk-…` — used for embeddings and chat completion |
| PostgreSQL + pgvector *(optional)* | Only required when `KnowledgeLLM:PgVector:Enabled` is `true` |

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
# Swagger UI → http://localhost:5000/swagger
```

### Index documents

```bash
curl -s -X POST http://localhost:5000/api/knowledge/index \
  -H "Content-Type: application/json" \
  -d '{"source": "/path/to/docs"}' | jq
# { "chunksIndexed": 42, "source": "/path/to/docs" }
```

`source` can be a directory (`.txt` / `.pdf` / `.docx` files loaded recursively) or a single file path.

### Ask a question

```bash
curl -s -X POST http://localhost:5000/api/knowledge/ask \
  -H "Content-Type: application/json" \
  -d '{"question": "What is the refund policy?", "topK": 5}' | jq
# { "answer": "...", "sources": [ { "chunkId": "...", "score": 0.91, ... } ] }
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

Keys live under the `KnowledgeLLM` section in `appsettings.json`, or as environment variables using double-underscore separators (e.g. `KNOWLEDGELLM__OPENAI__APIKEY`).

| Key | Default | Description |
|---|---|---|
| `KnowledgeLLM:OpenAI:ApiKey` | *(required)* | OpenAI secret key for embeddings and chat |
| `KnowledgeLLM:OpenAI:EmbeddingModel` | `text-embedding-3-small` | OpenAI embedding model name |
| `KnowledgeLLM:OpenAI:EmbeddingDimensions` | `1536` | Vector dimensions — must match the chosen model |
| `KnowledgeLLM:OpenAI:ChatModel` | `gpt-4o-mini` | OpenAI chat completion model name |
| `KnowledgeLLM:Chunker:ChunkSize` | `500` | Maximum characters per chunk |
| `KnowledgeLLM:Chunker:Overlap` | `100` | Overlapping characters between consecutive chunks |
| `KnowledgeLLM:PgVector:Enabled` | `false` | `true` → PostgreSQL/pgvector store; `false` → in-memory |
| `KnowledgeLLM:PgVector:ConnectionString` | *(empty)* | Npgsql connection string (required when enabled) |

> **PDF and Word support** are opt-in: call `services.AddPdfDocumentLoader()` after `AddKnowledgeLLM(...)` in `Program.cs` to enable `.pdf` and `.docx` loading alongside `.txt`.

---

## Architecture

```
INDEX FLOW
──────────
  source path
    │
    ▼
  IDocumentLoader.LoadAsync()        reads .txt / .pdf / .docx files from disk
    │
    ▼
  ITextChunker.ChunkAsync()          sliding-window split → TextChunk[]
    │
    ▼
  IEmbeddingModel.EmbedBatchAsync()  OpenAI embeddings → float[][]
    │
    ▼
  IVectorStore.UpsertAsync()         persists (chunk, vector) pairs
    │
    ▼
  int  ← chunks indexed


QUERY FLOW
──────────
  question (string)
    │
    ▼
  IEmbeddingModel.EmbedAsync()       embed the question → float[]
    │
    ▼
  IVectorStore.SearchAsync()         cosine similarity, top-K results
    │
    ▼
  PromptBuilder.BuildRagPrompt()     format grounded prompt (static helper)
    │
    ▼
  IChatModel.ChatAsync()             OpenAI chat completion
    │
    ▼
  RagAnswer { Answer, Sources[] }
```

`RagPipeline` orchestrates both flows. Any stage failure short-circuits immediately — subsequent stages do not run.

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
│       ├── Configuration/               # KnowledgeLLMOptions, PgVectorOptions
│       ├── Documents/                   # PlainTextDocumentLoader, PdfDocumentLoader,
│       │                                #   WordDocumentLoader, CompositeDocumentLoader
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
| 3 | ✅ Complete | `IChatModel` via `WeaveLLM.Providers` (`OpenAIChatModel`), streaming SSE endpoint |
| 4 | ✅ Complete | PostgreSQL/pgvector store, PDF document loader, composite loader DI extension |
| 5 | ⏳ Pending | Sample document pack, API authentication example, deployment guide |

---

## Contributing

1. Fork and create a feature branch
2. Run `dotnet test` — all tests must pass
3. Follow the conventions in [`CLAUDE.md`](CLAUDE.md): no exceptions, `CancellationToken` on every async method, XML doc on all public members
4. Open a pull request against `main`

---

## License

MIT © Harshil Shah — see [LICENSE](LICENSE) for full text.
