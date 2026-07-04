# API authentication example

KnowledgeLLM supports optional API-key authentication for API routes that mutate or query the RAG index. The expected key is configured with `KnowledgeLLM:Api:ApiKey` or `KNOWLEDGELLM__API__APIKEY`, and callers send the same value in the `X-Api-Key` request header.

When no API key is configured, authentication is disabled for zero-config local development. Configure a key before exposing the API to shared, demo, or production environments.

## Enable API-key authentication locally

Use user secrets when running the API directly from the repository:

```bash
dotnet user-secrets --project src/KnowledgeLLM.Api \
  set "KnowledgeLLM:Api:ApiKey" "dev-local-key"

dotnet run --project src/KnowledgeLLM.Api
```

Or use an environment variable:

```bash
export KNOWLEDGELLM__API__APIKEY="dev-local-key"
dotnet run --project src/KnowledgeLLM.Api
```

## Call protected endpoints

Include the configured key on indexing, question answering, and streaming requests:

```bash
curl -s -X POST http://localhost:5000/api/knowledge/index \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: dev-local-key" \
  -d '{"source": "samples/documents"}' | jq
```

```bash
curl -s -X POST http://localhost:5000/api/knowledge/ask \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: dev-local-key" \
  -d '{"question": "What does the sample document say?", "topK": 3}' | jq
```

```bash
curl -N -X POST http://localhost:5000/api/knowledge/ask/stream \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: dev-local-key" \
  -d '{"question": "Summarise the onboarding guide", "topK": 3}'
```

## Expected failure responses

If authentication is enabled and the header is missing, the API returns `401 UNAUTHORIZED`:

```json
{"code":"UNAUTHORIZED","message":"X-Api-Key header is required."}
```

If the header is present but does not match the configured key, the API returns `403 FORBIDDEN`:

```json
{"code":"FORBIDDEN","message":"Invalid API key."}
```

`GET` requests to health checks and Swagger are intentionally exempt so deployment probes and local API exploration continue to work. Protect or disable Swagger separately when running in shared environments.
