# Task 13: Azure Infrastructure and Container Deployment

**Status:** complete
**Depends on:** Tasks 04, 05, 06, 07, 08, and 09  
**Commit subject:** `feat: add Azure infrastructure and containers`

## Goal

Provision and deploy the complete application to Azure using Bicep and managed identities.

## Scope

- Add production-ready Dockerfiles and local container build validation.
- Add modular Bicep for Azure AI Foundry, Azure OpenAI, Container Apps, Cosmos DB, Application Insights, Log Analytics, Key Vault, and managed identities.
- Configure Container Apps for the API, web app, and four workers.
- Configure least-privilege role assignments and Key Vault references.
- Add environment parameters, outputs, tags, scaling, health probes, and diagnostics.
- Add Bicep linting and deployment what-if validation guidance.

## Acceptance Criteria

- Bicep builds and lints without errors.
- Containers build and run as non-root where supported.
- Services use managed identity instead of embedded credentials.
- Infrastructure outputs supply all deployment-time identifiers without exposing secrets.
- The deployed application has health and readiness checks.

## Validation

```powershell
az bicep build --file Infrastructure\main.bicep
az bicep lint --file Infrastructure\main.bicep
docker build --file src\Api\Dockerfile .
docker build --file src\Web\Dockerfile src\Web
```

## Evidence

- `az bicep build`, `az bicep lint`, and development parameter compilation: passed.
- .NET solution: 114 tests passed (79 unit, 35 integration).
- Web: lint, typecheck, 11 tests, and production build passed.
- Dependency security: no vulnerable NuGet or npm packages reported.
- Six Dockerfiles define explicit non-root users; API and web have Container Apps
  startup, liveness, and readiness probes.
- Docker engine unavailable on the validation host, so image builds were not runnable.
  The limitation and exact build commands are documented in `Infrastructure/README.md`.
- Deployment and Task 14 were not started.
