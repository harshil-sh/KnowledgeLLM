# Security

KnowledgeLLM is designed for local and portfolio-friendly RAG workflows, with production-oriented controls that can be enabled through configuration. Treat the guidance below as the baseline checklist before running the API in a shared or internet-accessible environment.

## API key handling

The API supports optional API-key authentication with the `X-Api-Key` header. Configure the expected value with `KnowledgeLLM:Api:ApiKey` or the `KNOWLEDGELLM__API__APIKEY` environment variable.

- Keep `KnowledgeLLM:Api:ApiKey` empty only for local development or isolated demos.
- Store API keys, OpenAI keys, PostgreSQL passwords, and production connection strings in user secrets, environment variables, or a managed secret store.
- Never commit real secrets to `appsettings.json`, `.env`, documentation examples, or test fixtures.
- Rotate keys immediately if they are exposed in logs, shell history, screenshots, or pull requests.
- Health and Swagger `GET` routes are intentionally exempt from API-key checks; protect or disable Swagger separately in shared environments.

OpenAI credentials are configured separately with `KnowledgeLLM:OpenAI:ApiKey` or `KNOWLEDGELLM__OPENAI__APIKEY`. The application uses this key only for embeddings, chat completions, and the OpenAI health probe.

## File restrictions

The `/api/knowledge/index` endpoint indexes files from a server-side path supplied in the request body. Current document loaders support:

| Loader | Supported input |
|---|---|
| Plain text | Single `.txt` file or a directory recursively containing `.txt` files |
| PDF | Single `.pdf` file or a directory recursively containing `.pdf` files when PDF loading is registered |
| Composite | `.txt` and `.pdf` files; other file extensions are rejected for single-file indexing |

Recommended production safeguards:

- Index only trusted document locations owned by the application or deployment pipeline.
- Mount document folders read-only where possible.
- Avoid indexing user-writable system locations such as `/tmp` unless they are isolated per tenant or per job.
- Add file size, total directory size, and file count limits before accepting large or externally supplied corpora.
- Scan externally sourced documents for malware before placing them in an indexable folder.
- Keep document content free of secrets unless the API and backing vector store are protected for the same audience allowed to read those secrets.

## Path validation

KnowledgeLLM currently validates that the `source` field is present and that the path exists before loading supported files. It does not enforce a repository-wide or deployment-wide allow-list root by default.

Before production use, add or enforce an application-specific indexing root, then resolve and compare canonical paths before calling the document loaders. A safe path policy should:

1. Configure an allowed root such as `/var/lib/knowledgellm/documents`.
2. Convert the requested `source` to a full path with the platform path APIs.
3. Reject paths that resolve outside the allowed root after normalization.
4. Reject unsupported extensions for single files.
5. Avoid following symlinks to locations outside the allowed root unless explicitly intended.
6. Return generic validation errors that do not reveal sensitive filesystem layout.

This is especially important because indexing runs on the API host. Any caller allowed to index should be treated as having indirect read access to files that the API process can read unless an allow-list is enforced.

## Prompt injection considerations

KnowledgeLLM builds prompts from retrieved document chunks and instructs the model to answer only from the provided context. That grounding helps reduce hallucinations, but it does not make retrieved content inherently trustworthy.

Use these practices when operating the RAG pipeline:

- Treat indexed documents as untrusted input, even when they come from internal sources.
- Expect documents to contain prompt-injection text such as requests to ignore previous instructions or reveal secrets.
- Keep secrets out of indexed documents and out of system, developer, or operational prompts.
- Preserve source metadata in responses so users can inspect the evidence behind an answer.
- Prefer narrow `topK` values that retrieve enough context to answer without flooding the model with unrelated instructions.
- Evaluate answers for grounding and source relevance before relying on them in automated workflows.
- Add policy checks or human review before using model output for high-impact decisions.

Prompt grounding is a defense-in-depth control, not an authorization boundary. Access control should happen before indexing, retrieval, and answer generation.
