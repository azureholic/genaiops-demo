# Task 13: Azure Infrastructure and Container Deployment

**Status:** blocked  
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

