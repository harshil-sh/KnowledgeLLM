# KnowledgeLLM — Internal Security and Testing Notes

> Generated from a repository scan on 27 June 2026. This file summarizes current security controls, testing coverage, and gaps to consider before production deployment.

## Current Security Controls

| Area | Current behavior |
|---|---|
| API authentication | `ApiKeyMiddleware` enforces `X-Api-Key` when `KnowledgeLLM:Api:ApiKey` is configured. Empty config intentionally disables auth for local development. |
| Exempt routes | GET `/health`, `/health/ready`, `/health/live`, and `/swagger/*` bypass API-key checks. |
| Request validation | FluentValidation validates request bodies before controller actions reach the RAG pipeline. |
| Rate limiting | Fixed-window limits apply separately to index and ask endpoints. Partition key is API key when present, otherwise remote IP. |
| Secret storage | Committed appsettings keep secret values empty. README recommends user-secrets for local OpenAI keys. |
| Prompt grounding | `PromptBuilder` tells the model to answer only from retrieved context. |
| Provider error handling | OpenAI embedding failures map to structured `WeaveLLMError` values instead of uncaught exceptions. |
| Health checks | OpenAI connectivity check uses a lightweight `/v1/models` probe and reports healthy/degraded/unhealthy outcomes. |

## Request Validation Limits

| Request | Field | Limits |
|---|---|---|
| `AskRequest` | `Question` | Not null, not empty, minimum 3 characters, maximum 1000 characters. |
| `AskRequest` | `TopK` | Inclusive range 1 through 20. |
| `IndexRequest` | `Source` | Not null, not empty, maximum 500 characters. |

## Security Gaps / Follow-up Items

- Add explicit path allow-listing or sandbox root validation for indexing paths before production use.
- Consider disabling Swagger in shared non-development environments unless protected by auth.
- Consider source-document size limits and per-file ingestion limits.
- Consider malware/content scanning for uploaded or externally synchronized documents if upload support is added.
- Add retry/backoff around transient OpenAI failures without retrying non-idempotent operations blindly.
- Add authorization scopes if multiple tenants or datasets are introduced.
- Review SSE error-token behavior for information disclosure in production responses.

## Test Inventory

The test project mirrors production areas:

| Test area | Representative files |
|---|---|
| Chunking | `Chunking/SlidingWindowChunkerTests.cs` |
| Documents | `Documents/PlainTextDocumentLoaderTests.cs`, `Documents/PdfDocumentLoaderTests.cs`, `Documents/CompositeDocumentLoaderTests.cs` |
| Embeddings | `Embeddings/OpenAIEmbeddingModelTests.cs` |
| Retrieval | `Retrieval/InMemoryVectorStoreTests.cs`, `Retrieval/PgVectorStoreTests.cs` |
| Pipeline | `Pipeline/RagPipelineTests.cs`, `Pipeline/RagPipelineIntegrationTests.cs`, `Pipeline/RagPipelineStreamingTests.cs`, `Pipeline/PromptBuilderTests.cs` |
| API boundary | `Controllers/KnowledgeControllerStreamingTests.cs`, `Validation/ValidatorTests.cs` |
| Middleware/health | `Middleware/ApiKeyMiddlewareTests.cs`, `HealthChecks/OpenAiConnectivityCheckTests.cs` |

## Recommended Checks Before Merging Code Changes

```bash
dotnet test
```

For changes that affect public HTTP behavior, also run the API locally and exercise:

```bash
dotnet run --project src/KnowledgeLLM.Api
curl -i http://localhost:5000/health/live
curl -i -X POST http://localhost:5000/api/knowledge/ask \
  -H "Content-Type: application/json" \
  -d '{"question":"What does this project do?","topK":5}'
```

## CI Notes

The repository contains a GitHub Actions workflow at `.github/workflows/ci.yml`. Keep internal documentation aligned with the checks enforced there whenever the workflow changes.
