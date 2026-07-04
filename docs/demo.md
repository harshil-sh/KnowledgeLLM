# KnowledgeLLM Demo

This walkthrough demonstrates the core KnowledgeLLM API flow: index a local knowledge source, ask a grounded question, and stream the answer with Server-Sent Events (SSE).

## Prerequisites

- .NET 8 SDK installed.
- An OpenAI API key configured with user secrets or environment variables.
- A folder containing `.txt`, `.pdf`, or `.docx` files to index. This repository includes `samples/documents` for a ready-to-use demo source.
- Optional: `jq` for formatted JSON output.

```bash
export KNOWLEDGELLM__OPENAI__APIKEY="sk-..."
dotnet run --project src/KnowledgeLLM.Api
```

By default, the API is available at `http://localhost:5000` when launched with the repository's development settings.

## Sample knowledge source

Use the committed sample document pack at `samples/documents`, or create your own folder of supported files. The sample pack includes a small support handbook and product FAQ that are safe to index during local demos.

## 1. Index documents with `/index`

`POST /api/knowledge/index` accepts a single file path or a directory path. Directories are loaded recursively for supported document types.

```bash
curl -s -X POST http://localhost:5000/api/knowledge/index \
  -H "Content-Type: application/json" \
  -d '{"source":"samples/documents"}' | jq
```

Example response:

```json
{
  "chunksIndexed": 2,
  "source": "samples/documents"
}
```

### Screenshot: successful indexing

```text
┌─────────────────────────────────────────────────────────────────────────────┐
│ Terminal                                                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│ $ curl -s -X POST http://localhost:5000/api/knowledge/index ... | jq         │
│ {                                                                           │
│   "chunksIndexed": 2,                                                       │
│   "source": "samples/documents"                                             │
│ }                                                                           │
└─────────────────────────────────────────────────────────────────────────────┘
```

## 2. Ask a grounded question with `/ask`

`POST /api/knowledge/ask` embeds the question, retrieves the most relevant chunks, builds a grounded prompt, and returns the answer with source chunks.

```bash
curl -s -X POST http://localhost:5000/api/knowledge/ask \
  -H "Content-Type: application/json" \
  -d '{"question":"Who owns escalations?","topK":3}' | jq
```

Example response:

```json
{
  "answer": "Escalations are owned by the platform engineering team.",
  "sources": [
    {
      "chunkId": "company-handbook.txt:0",
      "documentId": "samples/documents/company-handbook.txt",
      "content": "# Acme Support Handbook\n\nAcme Support uses KnowledgeLLM...",
      "score": 0.86
    }
  ]
}
```

### Screenshot: grounded answer with source metadata

```text
┌─────────────────────────────────────────────────────────────────────────────┐
│ Terminal                                                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│ $ curl -s -X POST http://localhost:5000/api/knowledge/ask ... | jq           │
│ {                                                                           │
│   "answer": "Escalations are owned by the platform engineering team.",      │
│   "sources": [                                                             │
│     { "documentId": "samples/documents/company-handbook.txt",               │
│       "score": 0.86 }                                                        │
│   ]                                                                         │
│ }                                                                           │
└─────────────────────────────────────────────────────────────────────────────┘
```

## 3. Stream an answer with `/ask/stream`

`POST /api/knowledge/ask/stream` uses the same retrieval flow as `/ask`, but streams answer tokens as SSE frames. Use `curl -N` to disable output buffering.

```bash
curl -N -X POST http://localhost:5000/api/knowledge/ask/stream \
  -H "Content-Type: application/json" \
  -d '{"question":"What is the support response target?","topK":3}'
```

Example stream:

```text
data: Support

data: requests

data: should

data: receive

data: an initial response within one business day.

data: [DONE]
```

### Screenshot: SSE stream

```text
┌─────────────────────────────────────────────────────────────────────────────┐
│ Terminal                                                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│ $ curl -N -X POST http://localhost:5000/api/knowledge/ask/stream ...         │
│ data: Support                                                               │
│                                                                             │
│ data: requests should receive an initial response within one business day.   │
│                                                                             │
│ data: [DONE]                                                                │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `INVALID_CONFIGURATION` | OpenAI API key is missing. | Set `KnowledgeLLM:OpenAI:ApiKey` or `KNOWLEDGELLM__OPENAI__APIKEY`. |
| `NOT_FOUND` | No matching chunks were retrieved. | Confirm `/index` succeeded and ask about content present in the indexed files. |
| `401 UNAUTHORIZED` | API key middleware is enabled and `X-Api-Key` is missing. | Send `X-Api-Key` with the configured `KnowledgeLLM:Api:ApiKey` value. |
| `429 RATE_LIMIT_EXCEEDED` | The fixed-window rate limit was exceeded. | Wait for the `Retry-After` interval and retry. |
