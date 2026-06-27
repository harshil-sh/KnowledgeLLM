# Build and run the KnowledgeLLM API as a containerized .NET 8 service.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY KnowledgeLLM.sln ./
COPY src/KnowledgeLLM.Api/KnowledgeLLM.Api.csproj src/KnowledgeLLM.Api/
COPY src/KnowledgeLLM.Core/KnowledgeLLM.Core.csproj src/KnowledgeLLM.Core/
COPY tests/KnowledgeLLM.Core.Tests/KnowledgeLLM.Core.Tests.csproj tests/KnowledgeLLM.Core.Tests/
RUN dotnet restore src/KnowledgeLLM.Api/KnowledgeLLM.Api.csproj

COPY . .
RUN dotnet publish src/KnowledgeLLM.Api/KnowledgeLLM.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "KnowledgeLLM.Api.dll"]
