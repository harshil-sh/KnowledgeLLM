# KnowledgeLLM — Internal Configuration Reference

> Generated from a repository scan on 27 June 2026. This file documents current configuration shape and runtime behavior.

## Configuration Root

All application-specific settings are bound under the `KnowledgeLLM` section into `KnowledgeLLMOptions`.

Environment variables use double underscores. Example:

```bash
export KNOWLEDGELLM__OPENAI__APIKEY="sk-..."
```

## OpenAI Settings

| Key | Default | Required | Purpose |
|---|---:|---|---|
| `KnowledgeLLM:OpenAI:ApiKey` | empty | Yes for embeddings/chat | Bearer token for OpenAI provider calls and health checks. |
| `KnowledgeLLM:OpenAI:EmbeddingModel` | `text-embedding-3-small` | Yes | Model used by `OpenAIEmbeddingModel`. |
| `KnowledgeLLM:OpenAI:ChatModel` | `gpt-4o-mini` | Yes | Model passed to WeaveLLM `.AddOpenAI(...)`. |
| `KnowledgeLLM:OpenAI:EmbeddingDimensions` | `1536` | Yes | Expected vector size; must match the embedding model and pgvector schema. |

## Chunker Settings

| Key | Default | Purpose |
|---|---:|---|
| `KnowledgeLLM:Chunker:ChunkSize` | `500` | Maximum characters per text chunk. |
| `KnowledgeLLM:Chunker:Overlap` | `100` | Character overlap shared by consecutive chunks. |

## API Authentication Settings

| Key | Default | Purpose |
|---|---:|---|
| `KnowledgeLLM:Api:ApiKey` | empty | Expected `X-Api-Key` header value. Empty disables API-key enforcement for zero-config local development. |

When configured, all non-exempt routes require `X-Api-Key`. GET requests to `/health`, `/health/ready`, `/health/live`, and `/swagger/*` are exempt.

## Rate-Limiting Settings

| Key | Default | Purpose |
|---|---:|---|
| `KnowledgeLLM:RateLimit:IndexPermitLimit` | `5` | Max index requests per fixed window. |
| `KnowledgeLLM:RateLimit:AskPermitLimit` | `30` | Max ask/stream requests per fixed window. |
| `KnowledgeLLM:RateLimit:WindowSeconds` | `60` | Fixed-window length. |
| `KnowledgeLLM:RateLimit:QueueLimit` | `2` | Number of queued requests after permits are exhausted. |

Partitioning prefers `X-Api-Key`; if absent, the remote IP address is used.

## PgVector Settings

| Key | Default | Purpose |
|---|---:|---|
| `KnowledgeLLM:PgVector:Enabled` | `false` | Registers `PgVectorStore` instead of `InMemoryVectorStore` when true. |
| `KnowledgeLLM:PgVector:ConnectionString` | empty | Npgsql connection string for PostgreSQL with pgvector. |

`PgVectorStore` initializes its schema lazily on first use and builds a pgvector-aware data source with `UseVector()`.

## Local Development Example

```bash
# Required for embeddings/chat
export KNOWLEDGELLM__OPENAI__APIKEY="sk-..."

# Optional API protection
export KNOWLEDGELLM__API__APIKEY="local-dev-key"

# Optional persistent vector store
export KNOWLEDGELLM__PGVECTOR__ENABLED="true"
export KNOWLEDGELLM__PGVECTOR__CONNECTIONSTRING="Host=localhost;Port=5432;Database=knowledgellm;Username=postgres;Password=postgres"

dotnet run --project src/KnowledgeLLM.Api
```

## Configuration Safety Notes

- Never commit real OpenAI API keys or production API keys.
- Keep `appsettings.json` values empty for secrets.
- Prefer `dotnet user-secrets` locally and environment variables in hosted environments.
- Ensure `EmbeddingDimensions` matches existing pgvector table dimensions before changing embedding models.
