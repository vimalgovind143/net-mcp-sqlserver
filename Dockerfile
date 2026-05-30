# ── Stage 1: Build ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore dependencies (cached layer)
COPY SqlServerMcpServer/*.csproj ./SqlServerMcpServer/
RUN dotnet restore SqlServerMcpServer/SqlServerMcpServer.csproj -r linux-x64

# Copy source and publish
COPY . .
RUN dotnet publish SqlServerMcpServer/SqlServerMcpServer.csproj \
    -c Release \
    -r linux-x64 \
    -o /app/publish \
    --no-restore \
    /p:DebugType=None \
    /p:DebugSymbols=false

# ── Stage 2: Runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

WORKDIR /app
COPY --from=build /app/publish .

# curl is used by the HTTP health check; procps provides pgrep for stdio/tcp mode.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl procps \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user for security
RUN useradd -m -s /bin/bash mcp && chown -R mcp:mcp /app
USER mcp

# Default transport: SSE (HTTP). Override with MCP_TRANSPORT=stdio or MCP_TRANSPORT=tcp
ENV MCP_TRANSPORT=sse
ENV MCP_PORT=8080
ENV SQLSERVER_COMMAND_TIMEOUT=30

EXPOSE 8080

# Health check: use /health endpoint for HTTP transports, else rely on process status
HEALTHCHECK --interval=30s --timeout=5s --retries=3 --start-period=10s \
    CMD if [ "$MCP_TRANSPORT" = "sse" ] || [ "$MCP_TRANSPORT" = "http" ]; then \
            curl -fsS "http://localhost:${MCP_PORT:-8080}/health" > /dev/null; \
        else \
            pgrep SqlServerMcpServer > /dev/null; \
        fi || exit 1

ENTRYPOINT ["./SqlServerMcpServer"]
