# Deployment guide

KnowledgeLLM can run as a standard ASP.NET Core service or as a containerized API. Production deployments should use PostgreSQL with pgvector for persistent retrieval storage and should keep OpenAI, API, and database credentials in the platform secret store rather than in source-controlled files.

## Deployment checklist

Before exposing the API outside a local workstation:

- publish or deploy the API with `ASPNETCORE_ENVIRONMENT=Production`;
- set `KNOWLEDGELLM__OPENAI__APIKEY` from a secret store;
- set `KNOWLEDGELLM__API__APIKEY` so indexing and question-answering routes require the `X-Api-Key` header;
- enable PostgreSQL persistence with `KNOWLEDGELLM__PGVECTOR__ENABLED=true`;
- point `KNOWLEDGELLM__PGVECTOR__CONNECTIONSTRING` at a pgvector-enabled PostgreSQL database;
- verify `/health/live` for liveness and `/health/ready` for OpenAI and PostgreSQL readiness.

## Azure App Service

Azure App Service works well for the API when paired with Azure Database for PostgreSQL Flexible Server.

1. Create a PostgreSQL Flexible Server instance and database for KnowledgeLLM.
2. Enable the `vector` extension in the target database:

   ```sql
   CREATE EXTENSION IF NOT EXISTS vector;
   ```

3. Create an App Service or Web App for Containers using the published KnowledgeLLM API image.
4. Configure application settings in App Service:

   ```text
   ASPNETCORE_ENVIRONMENT=Production
   ASPNETCORE_URLS=http://+:8080
   KNOWLEDGELLM__OPENAI__APIKEY=<from Key Vault or App Service secret setting>
   KNOWLEDGELLM__API__APIKEY=<shared client key>
   KNOWLEDGELLM__PGVECTOR__ENABLED=true
   KNOWLEDGELLM__PGVECTOR__CONNECTIONSTRING=Host=<server>.postgres.database.azure.com;Port=5432;Database=knowledgellm;Username=<user>;Password=<password>;Ssl Mode=Require;Trust Server Certificate=true
   ```

5. Set the App Service health check path to `/health/ready` so unhealthy instances are removed from rotation.
6. Restrict inbound access with App Service authentication, a private endpoint, API Management, or network rules when the API is not intended to be public.

## Containers

The repository includes a multi-stage `Dockerfile` and a local `docker-compose.yml` stack that runs the API with PostgreSQL and pgvector.

Build and run the API image locally:

```bash
docker build -t knowledgellm-api:local .
docker run --rm -p 5000:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e KNOWLEDGELLM__OPENAI__APIKEY="sk-..." \
  -e KNOWLEDGELLM__API__APIKEY="change-me" \
  -e KNOWLEDGELLM__PGVECTOR__ENABLED="true" \
  -e KNOWLEDGELLM__PGVECTOR__CONNECTIONSTRING="Host=<postgres-host>;Port=5432;Database=knowledgellm;Username=knowledgellm;Password=<password>" \
  knowledgellm-api:local
```

For local end-to-end validation with PostgreSQL:

```bash
cp .env.example .env
# edit .env and set KNOWLEDGELLM__OPENAI__APIKEY plus a strong POSTGRES_PASSWORD
docker compose up --build
```

Mount document folders read-only when indexing host files from a container, following the existing Compose pattern of mapping `./data` to `/data:ro`.

## PostgreSQL hosting

KnowledgeLLM expects a PostgreSQL database with the pgvector extension available. Use a managed PostgreSQL service for production rather than the development Compose database.

Recommended production settings:

- use PostgreSQL 16 or another version supported by the pgvector extension;
- create a dedicated database and least-privilege application user;
- require TLS for remote database connections;
- store the connection string in the deployment platform secret store;
- back up the database because indexed chunks and embeddings are application state;
- keep `KnowledgeLLM:OpenAI:EmbeddingDimensions` aligned with the embedding model and the pgvector column dimension before indexing production documents.

After deployment, call `/health/ready` to confirm both OpenAI connectivity and PostgreSQL readiness before sending indexing traffic.
