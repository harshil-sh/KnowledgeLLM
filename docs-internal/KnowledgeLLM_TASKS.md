# KnowledgeLLM Portfolio Alignment Tasks

## Phase 1 - CV Alignment (Highest Priority)

- [x] Add README section: "Why this project exists"
  - Position KnowledgeLLM as a practical RAG application built on WeaveLLM.Core.
  - Explain document-grounded question answering use case.

- [x] Add README section: "Production-Oriented Capabilities"
  - CI/CD
  - Automated testing
  - Environment-based configuration
  - PostgreSQL/pgvector persistence
  - SSE streaming
  - Source-grounded responses

- [x] Add architecture documentation
  - Create docs/architecture.md
  - Include indexing flow
  - Include query flow
  - Include component interaction diagram

- [x] Add demo documentation
  - docs/demo.md
  - /index examples
  - /ask examples
  - /ask/stream examples
  - Screenshots or GIFs

- [x] Add GitHub topics
  - dotnet
  - csharp
  - rag
  - llm
  - vector-search
  - pgvector
  - openai
  - aspnetcore
  - ai-engineering

---

## Phase 2 - Senior Engineer Signal

- [x] Add docker-compose support
  - KnowledgeLLM API
  - PostgreSQL
  - pgvector
  - .env.example

- [x] Add docs/configuration.md
  - Local development
  - In-memory mode
  - PostgreSQL mode
  - Environment variable reference

- [x] Add docs/security.md
  - API key handling
  - File restrictions
  - Path validation
  - Prompt injection considerations

- [x] Add docs/testing.md
  - Unit testing approach
  - Integration testing approach
  - CI workflow explanation

---

## Phase 3 - AI Engineering Credibility

- [x] Create evaluation dataset
  - eval/questions.json
  - 20+ benchmark questions

- [x] Create RAG evaluation runner
  - Retrieval hit rate
  - Source relevance
  - Grounding validation

- [x] Add latency metrics
  - Indexing latency
  - Retrieval latency
  - LLM response latency

- [x] Add OpenTelemetry tracing
  - Document loading
  - Chunking
  - Embeddings
  - Vector search
  - Chat completion

- [x] Add retry policy
  - Exponential backoff
  - Transient OpenAI failures
  - Automated tests

---

## Phase 4 - Nice To Have

- [x] Add Word document loader (.docx)

- [x] Add sample document pack

- [x] Add API authentication example

- [x] Add deployment guide
  - Azure App Service
  - Containers
  - PostgreSQL hosting

---

## Recommended Execution Order

1. README improvements
2. Architecture documentation
3. Demo documentation
4. Docker Compose
5. Evaluation dataset
6. OpenTelemetry
7. Retry policy
8. Remaining enhancements
