# Security, Hosting, and Tokenization Plan

## Current State

- The server already enforces **read-only query validation**, row limits, timeouts, and structured logging
- The current host is **stdio-only** (`WithStdioServerTransport()`), which is a good fit for local tools like Claude Desktop
- Connection strings are currently supplied through environment variables or `appsettings.json`

## Security Features Worth Adding Next

1. **Secret management**
   - Move production connection strings out of local config files and into a secret store such as Azure Key Vault, AWS Secrets Manager, GCP Secret Manager, or Docker/Kubernetes secrets
   - Support secret references so deployments do not need raw passwords in `claude_desktop_config.json` or environment exports

2. **Stronger identity and authorization**
   - Prefer **Azure AD / Entra ID**, managed identity, or another short-lived identity flow over long-lived SQL passwords where possible
   - Add an allowlist for approved databases, schemas, and optionally tables/views
   - Add a denylist or masking policy for sensitive columns such as passwords, tokens, SSNs, card numbers, and personal contact data

3. **Hosted MCP authentication**
   - If the server is exposed remotely, require **OAuth 2.1 / OIDC**, JWT bearer tokens, API keys behind a gateway, or mTLS
   - Bind every request to a caller identity and include that identity in audit logs
   - Add rate limiting, concurrency limits, and per-client quotas to reduce abuse

4. **Query governance**
   - Add configurable maximums for returned rows, execution time, and concurrent requests per client
   - Add optional approval or allowlist mode for free-form SQL in high-risk environments
   - Add query fingerprint logging so repeated risky access patterns are detectable without storing full sensitive payloads

5. **Data protection**
   - Add result redaction for known sensitive fields before they are returned to the MCP client
   - Prefer exposing tokenized or masked views instead of raw production tables
   - Consider separate read-only replicas or sanitized reporting databases for AI access

6. **Operational security**
   - Add audit fields for caller, server name, environment, database, operation, query hash, and elapsed time
   - Add alerting for blocked queries, repeated auth failures, and unusually expensive reads
   - Keep logs out of the MCP transport channel and continue using file or centralized structured logs only

## Can This MCP Be Hosted?

**Yes, but not in its current transport shape.**

Right now this project is set up for **local stdio hosting**, which works well for desktop MCP clients. To make it a hosted MCP service, the project would need:

- an **HTTP-capable MCP transport** (or an MCP-compatible gateway in front of the process)
- caller authentication and authorization
- TLS termination and network controls
- secret management for database credentials
- request throttling, audit logging, and deployment packaging

## Recommended Hosted Deployment Pattern

1. Keep the SQL Server login itself **read-only and least-privileged**
2. Containerize the server
3. Put it behind an API gateway / ingress with:
   - TLS
   - JWT or OAuth validation
   - rate limits
   - IP allowlists or private networking
4. Run it in a private network close to the database
5. Send logs and metrics to centralized monitoring

## Is Tokenization Possible?

**Yes, in two different senses:**

1. **Authentication tokens**
   - Very possible for a hosted version
   - Recommended approach: use short-lived access tokens (OIDC/JWT) issued by an identity provider, then validate them at the MCP edge or gateway

2. **Data tokenization**
   - Also possible, and highly recommended for sensitive datasets
   - Best approach for this project is to expose **tokenized views** or **masked reporting tables** instead of raw tables
   - For highly regulated fields, integrate with an external tokenization service and only let the MCP server read the already-tokenized values

## Similar Database MCP Patterns to Learn From

Verified against public documentation in **March 2026**:

- **[Azure Data API Builder SQL MCP Server overview](https://learn.microsoft.com/en-us/azure/data-api-builder/mcp/overview)** - good reference for remote hosting, identity integration, and granular data exposure
- **[Google Cloud managed MCP servers for databases](https://cloud.google.com/blog/products/databases/managed-mcp-servers-for-google-cloud-databases)** - good reference for hosted MCP, IAM-backed access, and managed observability
- **[Oracle MCP Server for Oracle Database](https://blogs.oracle.com/database/introducing-mcp-server-for-oracle-database)** - good reference for auditability and least-privilege database accounts
- **[AWS MCP servers for databases](https://aws.amazon.com/blogs/database/supercharging-aws-database-development-with-aws-mcp-servers/)** - good reference for token-based cloud access, IAM, and centralized audit trails

Common patterns across those systems:

- hosted remote access is normal
- short-lived identity tokens are preferred over static passwords
- least-privilege database roles are mandatory
- audit logging and policy enforcement happen outside the LLM client as well as inside the database layer
- masking, tokenization, or sanitized replicas are strongly preferred for production data

## Suggested Implementation Plan

### Phase 1 - Harden the current local/stdio server

- Keep stdio transport for local use
- Move connection strings to secret storage for non-local environments
- Add database/schema allowlists
- Add sensitive-column redaction rules
- Document a recommended read-only SQL role for deployments

### Phase 2 - Prepare for hosted deployment

- Add or adopt an HTTP MCP transport
- Containerize the app
- Add health checks, readiness checks, and centralized telemetry
- Introduce gateway-based authentication, TLS, and rate limiting

### Phase 3 - Add identity-aware access control

- Require JWT/OIDC tokens for remote callers
- Map caller identity to allowed environments/databases
- Add per-client quotas and better audit trails
- Add an option to disable free-form SQL and allow only curated inspection tools in high-security environments

### Phase 4 - Protect sensitive production data

- Default to tokenized views, masked columns, or sanitized replicas
- Add field-level policy checks for common secrets and PII
- Review logs to ensure no sensitive result sets are stored

## Recommended Order of Work for This Repository

1. **First**: secret management + least-privilege SQL login
2. **Second**: database/schema allowlists + sensitive data redaction
3. **Third**: containerization + hosted transport proof of concept
4. **Fourth**: OIDC/JWT authentication and rate limiting
5. **Fifth**: tokenized views / masked datasets for production usage

This order keeps the current local workflow intact while making the project safer before it is exposed as a hosted MCP service.
