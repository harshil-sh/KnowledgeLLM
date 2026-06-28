# Configuration

KnowledgeLLM uses the standard ASP.NET Core configuration stack. Runtime settings are bound from the `KnowledgeLLM` section and can be supplied through `appsettings.json`, user secrets, environment variables, or Docker Compose.

Environment variables use double underscores for nested keys. For example, `KnowledgeLLM:OpenAI:ApiKey` becomes `KNOWLEDGELLM__OPENAI__APIKEY`.

## Local development

Use local development mode when you want to run the API directly from the repository with the in-memory vector store.

1. Install the .NET 8 SDK.
2. Store your OpenAI API key in user secrets so it is not committed:

   ```bash
   dotnet user-secrets --project src/KnowledgeLLM.Api \
     set "KnowledgeLLM:OpenAI:ApiKey" "sk-..."
   ```

3. Start the API:

   ```bash
   dotnet run --project src/KnowledgeLLM.Api
   ```

4. Open Swagger at `http://localhost:5000/swagger`, or call the API endpoints directly.

For ad-hoc local runs, you can also provide settings as environment variables:

```bash
export KNOWLEDGELLM__OPENAI__APIKEY="sk-..."
export KNOWLEDGELLM__OPENAI__CHATMODEL="gpt-4o-mini"
dotnet run --project src/KnowledgeLLM.Api
```

API-key authentication is optional for zero-config development. When `KnowledgeLLM:Api:ApiKey` is empty, requests do not need an `X-Api-Key` header. Set this value locally when you want to exercise the authentication middleware.

## In-memory mode

In-memory mode is the default vector-store configuration. It is useful for development, demos, and tests because it does not require PostgreSQL.

```json
{
  "KnowledgeLLM": {
    "PgVector": {
      "Enabled": false,
      "ConnectionString": ""
    }
  }
}
```

In this mode:

- indexed chunks and embeddings live only in process memory;
- data is lost when the API process restarts;
- no database connection string is required;
- `KnowledgeLLM:OpenAI:EmbeddingDimensions` must still match the selected embedding model because vectors are generated before storage.

## PostgreSQL mode

PostgreSQL mode enables persistent retrieval storage through PostgreSQL and pgvector. Use it for local integration testing, Docker-based demos, or environments where indexed documents should survive API restarts.

```json
{
  "KnowledgeLLM": {
    "PgVector": {
      "Enabled": true,
      "ConnectionString": "Host=localhost;Port=5432;Database=knowledgellm;Username=knowledgellm;Password=change-me"
    }
  }
}
```

The Docker Compose stack enables PostgreSQL mode automatically for the API container and points it at the `postgres` service:

```bash
cp .env.example .env
# edit .env and set KNOWLEDGELLM__OPENAI__APIKEY plus a non-default POSTGRES_PASSWORD
docker compose up --build
```

When running without Docker Compose, make sure that:

- PostgreSQL is reachable from the API process;
- the pgvector extension is installed and enabled in the target database;
- the connection string user can create and write the vector-store tables;
- `KnowledgeLLM:OpenAI:EmbeddingDimensions` matches the embedding model and the configured database vector dimension.

## Environment variable reference

| Environment variable | Configuration key | Default | Required | Description |
|---|---|---:|:---:|---|
| `KNOWLEDGELLM__OPENAI__APIKEY` | `KnowledgeLLM:OpenAI:ApiKey` | empty | Yes | OpenAI API key used for embeddings and chat completions. Prefer user secrets or a deployment secret store. |
| `KNOWLEDGELLM__OPENAI__EMBEDDINGMODEL` | `KnowledgeLLM:OpenAI:EmbeddingModel` | `text-embedding-3-small` | No | Embedding model used to generate vectors for documents and questions. |
| `KNOWLEDGELLM__OPENAI__CHATMODEL` | `KnowledgeLLM:OpenAI:ChatModel` | `gpt-4o-mini` | No | Chat model used to generate grounded answers. |
| `KNOWLEDGELLM__OPENAI__EMBEDDINGDIMENSIONS` | `KnowledgeLLM:OpenAI:EmbeddingDimensions` | `1536` | No | Embedding vector length. Must match the selected embedding model and pgvector column dimension. |
| `KNOWLEDGELLM__CHUNKER__CHUNKSIZE` | `KnowledgeLLM:Chunker:ChunkSize` | `500` | No | Maximum number of characters per indexed text chunk. |
| `KNOWLEDGELLM__CHUNKER__OVERLAP` | `KnowledgeLLM:Chunker:Overlap` | `100` | No | Number of overlapping characters between consecutive chunks. |
| `KNOWLEDGELLM__API__APIKEY` | `KnowledgeLLM:Api:ApiKey` | empty | No | Expected `X-Api-Key` request header. Leave empty to disable API-key auth for local development. |
| `KNOWLEDGELLM__RATELIMIT__INDEXPERMITLIMIT` | `KnowledgeLLM:RateLimit:IndexPermitLimit` | `5` | No | Number of `/index` requests allowed per rate-limit window. |
| `KNOWLEDGELLM__RATELIMIT__ASKPERMITLIMIT` | `KnowledgeLLM:RateLimit:AskPermitLimit` | `30` | No | Number of `/ask` and `/ask/stream` requests allowed per rate-limit window. |
| `KNOWLEDGELLM__RATELIMIT__WINDOWSECONDS` | `KnowledgeLLM:RateLimit:WindowSeconds` | `60` | No | Rate-limit window duration in seconds. |
| `KNOWLEDGELLM__RATELIMIT__QUEUELIMIT` | `KnowledgeLLM:RateLimit:QueueLimit` | `2` | No | Requests allowed to wait when the active rate-limit window is exhausted. |
| `KNOWLEDGELLM__PGVECTOR__ENABLED` | `KnowledgeLLM:PgVector:Enabled` | `false` | No | Set to `true` to use PostgreSQL/pgvector instead of the in-memory store. |
| `KNOWLEDGELLM__PGVECTOR__CONNECTIONSTRING` | `KnowledgeLLM:PgVector:ConnectionString` | empty | Required when pgvector is enabled | Npgsql connection string for the pgvector-enabled database. |
| `KNOWLEDGELLM_API_PORT` | Docker Compose host port | `5000` | No | Host port mapped to the API container. Used by Docker Compose, not by ASP.NET Core configuration binding. |
| `POSTGRES_DB` | Docker Compose PostgreSQL database | `knowledgellm` | No | Database name created by the Compose PostgreSQL service. |
| `POSTGRES_USER` | Docker Compose PostgreSQL user | `knowledgellm` | No | Database user created by the Compose PostgreSQL service. |
| `POSTGRES_PASSWORD` | Docker Compose PostgreSQL password | `knowledgellm_dev_password` | Recommended | Database password for the Compose PostgreSQL service. Override in `.env`. |
| `POSTGRES_PORT` | Docker Compose PostgreSQL host port | `5432` | No | Host port mapped to the PostgreSQL container. |

## Configuration precedence

For local runs, later providers override earlier ones according to ASP.NET Core defaults. In practice, environment variables override `appsettings.json`, and user secrets are convenient for local secrets. For Docker Compose, values in `.env` are interpolated into container environment variables before the API starts.

Never commit real API keys, database passwords, or production connection strings. Keep `.env` local and use managed secret storage in deployed environments.
