# Sample document pack

This folder contains small, safe-to-commit text documents for trying KnowledgeLLM without preparing your own knowledge base.

## Use the pack

Start the API, then index this folder from the repository root:

```bash
curl -s -X POST http://localhost:5000/api/knowledge/index \
  -H "Content-Type: application/json" \
  -d '{"source":"samples/documents"}' | jq
```

Try questions that are answered by the sample files:

- `Who owns escalations?`
- `What storage mode is used by default for local development?`
- `Which endpoint streams answer tokens?`

The files are intentionally plain `.txt` documents so they work in the default loader path and remain easy to inspect in code review.
