# KnowledgeLLM Architecture

KnowledgeLLM is a .NET 8 Retrieval-Augmented Generation (RAG) API. The API layer exposes document indexing and question-answering endpoints, while the core layer owns document loading, chunking, embedding, vector storage, prompt construction, and LLM calls.

## Runtime Components

| Component | Responsibility |
|---|---|
| `KnowledgeController` | HTTP boundary for `/api/knowledge/index`, `/api/knowledge/ask`, and `/api/knowledge/ask/stream`. It maps pipeline results to API responses and error payloads. |
| `RagPipeline` | Orchestrates indexing and query execution. Each stage returns a `ChainResult<T>` and the pipeline short-circuits on the first failure. |
| `IDocumentLoader` | Loads supported source documents from a file or directory. `PlainTextDocumentLoader` handles `.txt`; `CompositeDocumentLoader` can combine text and PDF loading. |
| `ITextChunker` | Splits each loaded document into overlapping `TextChunk` instances using `SlidingWindowChunker`. |
| `IEmbeddingModel` | Calls OpenAI embeddings for document chunks and user questions. |
| `IVectorStore` | Stores and searches vectors. The application can use `InMemoryVectorStore` for local development or `PgVectorStore` for PostgreSQL/pgvector persistence. |
| `PromptBuilder` | Formats retrieved chunks and the user question into a grounded RAG prompt. |
| `IChatModel` | Uses WeaveLLM's OpenAI chat provider for regular and streaming answer generation. |

## Component Interaction Diagram

```mermaid
flowchart LR
    Client[Client]
    API[KnowledgeLLM.Api\nKnowledgeController]
    Pipeline[KnowledgeLLM.Core\nRagPipeline]
    Loader[IDocumentLoader\n.txt / .pdf]
    Chunker[ITextChunker\nSlidingWindowChunker]
    Embeddings[IEmbeddingModel\nOpenAI embeddings]
    Store[IVectorStore\nInMemory or PostgreSQL/pgvector]
    Prompt[PromptBuilder]
    Chat[IChatModel\nOpenAI chat]

    Client -->|POST /api/knowledge/index| API
    Client -->|POST /api/knowledge/ask| API
    Client -->|POST /api/knowledge/ask/stream| API
    API --> Pipeline

    Pipeline -->|index: load| Loader
    Pipeline -->|index: chunk| Chunker
    Pipeline -->|index/query: embed| Embeddings
    Pipeline -->|index: upsert\nquery: search| Store
    Pipeline -->|query: build prompt| Prompt
    Pipeline -->|query: complete/stream| Chat
    Chat --> Pipeline --> API --> Client
```

## Indexing Flow

The indexing flow ingests a local source path and persists searchable chunk embeddings.

1. **Validate input**: `RagPipeline.IndexAsync` rejects a null, empty, or whitespace `source` value.
2. **Load documents**: The configured `IDocumentLoader` reads the source path.
   - A single `.txt` file is loaded by `PlainTextDocumentLoader`.
   - When PDF support is registered, `CompositeDocumentLoader` dispatches `.txt` and `.pdf` files to the appropriate loader.
   - Directories are loaded recursively for supported file types.
3. **Chunk documents**: Each `Document` is split by `SlidingWindowChunker` according to configured chunk size and overlap.
4. **Embed chunks**: `OpenAIEmbeddingModel.EmbedBatchAsync` converts chunk text into embedding vectors.
5. **Persist vectors**: `IVectorStore.UpsertAsync` stores each chunk and embedding pair.
   - `InMemoryVectorStore` keeps vectors in process memory.
   - `PgVectorStore` initializes its schema on first use and persists rows to PostgreSQL with pgvector.
6. **Return count**: The API returns the total number of chunks indexed for the request.

```mermaid
sequenceDiagram
    participant Client
    participant Controller as KnowledgeController
    participant Pipeline as RagPipeline
    participant Loader as IDocumentLoader
    participant Chunker as ITextChunker
    participant Embeddings as IEmbeddingModel
    participant Store as IVectorStore

    Client->>Controller: POST /api/knowledge/index { source }
    Controller->>Pipeline: IndexAsync(source)
    Pipeline->>Loader: LoadAsync(source)
    Loader-->>Pipeline: Document[]
    loop each document
        Pipeline->>Chunker: ChunkAsync(document)
        Chunker-->>Pipeline: TextChunk[]
        Pipeline->>Embeddings: EmbedBatchAsync(chunk text)
        Embeddings-->>Pipeline: float[][]
        Pipeline->>Store: UpsertAsync(chunks, embeddings)
        Store-->>Pipeline: inserted count
    end
    Pipeline-->>Controller: total chunks indexed
    Controller-->>Client: 200 OK { chunksIndexed, source }
```

## Query Flow

The query flow retrieves relevant chunks, builds a grounded prompt, and asks the chat model to answer from the retrieved context.

1. **Validate input**: `RagPipeline.AskAsync` rejects blank questions and requires `topK >= 1`.
2. **Embed question**: `IEmbeddingModel.EmbedAsync` converts the user question into a query vector.
3. **Search vector store**: `IVectorStore.SearchAsync` performs similarity search and returns the top matching chunks.
4. **Handle empty retrieval**: If no sources are found, the pipeline returns a `NOT_FOUND` error instead of asking the model to answer without context.
5. **Build grounded prompt**: `PromptBuilder.BuildRagPrompt` combines the user question with retrieved source chunks.
6. **Generate answer**:
   - `/api/knowledge/ask` calls `IChatModel.ChatAsync` and returns one response payload with `answer` and `sources`.
   - `/api/knowledge/ask/stream` calls the streaming chat path and emits Server-Sent Events until `[DONE]` or an error token.

```mermaid
sequenceDiagram
    participant Client
    participant Controller as KnowledgeController
    participant Pipeline as RagPipeline
    participant Embeddings as IEmbeddingModel
    participant Store as IVectorStore
    participant Prompt as PromptBuilder
    participant Chat as IChatModel

    Client->>Controller: POST /api/knowledge/ask { question, topK }
    Controller->>Pipeline: AskAsync(question, topK)
    Pipeline->>Embeddings: EmbedAsync(question)
    Embeddings-->>Pipeline: float[]
    Pipeline->>Store: SearchAsync(queryEmbedding, topK)
    Store-->>Pipeline: RetrievalResult[]
    Pipeline->>Prompt: BuildRagPrompt(question, sources)
    Prompt-->>Pipeline: grounded prompt
    Pipeline->>Chat: ChatAsync(messages, options)
    Chat-->>Pipeline: answer content
    Pipeline-->>Controller: RagAnswer(answer, sources)
    Controller-->>Client: 200 OK { answer, sources }
```

## Configuration and Storage Selection

`AddKnowledgeLLM` registers the core services and binds settings from the `KnowledgeLLM` configuration section. Storage is selected at startup:

- `KnowledgeLLM:PgVector:Enabled = false` uses `InMemoryVectorStore`.
- `KnowledgeLLM:PgVector:Enabled = true` uses `PgVectorStore` with `KnowledgeLLM:PgVector:ConnectionString` and the configured embedding dimensions.

This allows the same pipeline to run in lightweight local mode or in persistent PostgreSQL-backed mode without changing controller code.

## Observability and Reliability Boundaries

- The API applies request validation, optional API-key middleware, fixed-window rate limiting, health checks, and Serilog request logging.
- `RagPipeline` creates OpenTelemetry activities for index, load, chunk, embed, upsert, ask, search, and completion stages.
- The pipeline records latency histograms for indexing (`knowledgellm.indexing.duration`), retrieval (`knowledgellm.retrieval.duration`), and LLM response generation (`knowledgellm.llm_response.duration`) so operators can compare ingestion, vector-search, and model-call performance.
- Pipeline stages fail fast with structured `ChainResult<T>` errors; later stages are not invoked after an upstream failure.
