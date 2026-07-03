# Testing

KnowledgeLLM uses automated tests and the GitHub Actions CI workflow to keep the RAG pipeline, document loaders, health checks, and retrieval behavior reliable as the application evolves.

## Unit testing approach

Unit tests live under `tests/KnowledgeLLM.Core.Tests` and mirror the core project structure where possible. They focus on deterministic behavior that does not require a live OpenAI account or PostgreSQL instance.

Current unit coverage includes:

- chunking behavior for sliding-window text splitting;
- prompt formatting for grounded RAG answers;
- pipeline orchestration, including failure short-circuiting and source propagation;
- streaming pipeline behavior;
- OpenAI embedding and health-check HTTP handling through fake message handlers;
- document loader validation for plain-text, PDF, and Word inputs;
- in-memory vector search behavior.

External dependencies are replaced with test doubles such as fake HTTP handlers, substitute chat and embedding models, and the in-memory vector store. This keeps the default `dotnet test` path fast, repeatable, and safe to run without secrets.

Run the full test suite from the repository root:

```bash
dotnet test KnowledgeLLM.sln
```

Run a focused test class or method with a filter:

```bash
dotnet test KnowledgeLLM.sln --filter "FullyQualifiedName~PromptBuilderTests"
```

## Integration testing approach

Integration-style tests are kept inside the same xUnit test project when they can run locally without managed services. They exercise real application components together, such as loading the sample PDF fixture from `docs/test-data`, chunking it, indexing it, retrieving sources, and producing a grounded answer through substituted LLM dependencies.

Use this boundary for new tests:

- prefer unit tests for business logic, validation, prompt construction, and error mapping;
- add integration tests when multiple KnowledgeLLM components must be verified together;
- avoid requiring real OpenAI credentials in the default suite;
- avoid requiring PostgreSQL in the default suite unless the test is explicitly isolated and documented;
- keep reusable fakes and HTTP helpers under `tests/KnowledgeLLM.Core.Tests/Helpers`.

PostgreSQL/pgvector behavior can be validated manually with Docker Compose when persistence needs to be exercised end to end:

```bash
cp .env.example .env
# edit .env and set KNOWLEDGELLM__OPENAI__APIKEY plus a non-default POSTGRES_PASSWORD
docker compose up --build
```

Then call `/api/knowledge/index`, `/api/knowledge/ask`, and `/api/knowledge/ask/stream` as shown in `docs/demo.md`.

## CI workflow explanation

The CI workflow is defined in `.github/workflows/ci.yml` and runs on pushes and pull requests targeting `main`.

The workflow performs these steps:

1. checks out the repository;
2. installs the .NET 8 SDK;
3. caches NuGet packages using the project file hash;
4. restores `KnowledgeLLM.sln`;
5. builds the solution in Release configuration without restoring again;
6. runs the Release test suite without rebuilding, emits TRX results, and collects XPlat code coverage;
7. uploads coverage XML files as a workflow artifact.

To reproduce the CI checks locally, run:

```bash
dotnet restore KnowledgeLLM.sln
dotnet build KnowledgeLLM.sln --configuration Release --no-restore
dotnet test KnowledgeLLM.sln --configuration Release --no-build --logger trx --collect:"XPlat Code Coverage"
```

Documentation-only changes should still pass the same solution-level checks before a pull request is opened.
