# GenAIOps Azure Deployment Plan

**Status:** Validated
**Authorization:** The user approved sequential implementation of Task 13 from `tasks/13-azure-infrastructure-and-containers.md`. This plan covers preparation and validation only; it does not authorize an Azure deployment.

## 1. Workload Summary

GenAIOps is a .NET 10 and React reference application for versioned Azure AI Foundry agents. It includes:

- An ASP.NET Core Minimal API and SignalR endpoint.
- A React/TypeScript web application.
- Shadow evaluation, metrics aggregation, promotion, and rollback workers.
- Azure Cosmos DB persistence.
- Azure AI Foundry Agent Service, Azure OpenAI, and Foundry Evaluations.
- OpenTelemetry exported to Application Insights.

## 2. Deployment Mode

- **Mode:** Prepare an existing application for Azure.
- **Recipe:** Bicep with Azure Container Apps.
- **Deployment execution:** Out of scope for Task 13.
- **Environment strategy:** Parameterized environment names with development defaults and production-safe overrides.

## 3. Target Architecture

| Component | Azure service | Identity/network approach |
|-----------|---------------|---------------------------|
| Web | Azure Container Apps | External ingress; managed identity |
| API | Azure Container Apps | External ingress for API and SignalR; managed identity |
| Four workers | Azure Container Apps Jobs or internal apps, selected by runtime pattern | Managed identity; no public ingress |
| Containers | Azure Container Registry | Pull through managed identity |
| Operational data | Azure Cosmos DB for NoSQL | RBAC data-plane access; no embedded keys |
| Agents/evaluations | Azure AI Foundry project and Azure OpenAI | RBAC through managed identity |
| Secrets/configuration | Azure Key Vault and Container Apps secrets | Key Vault references; no secret outputs |
| Telemetry | Log Analytics and Application Insights | Connection string injected as a secret reference |

## 4. Infrastructure Modules

- Resource naming and tags.
- Log Analytics and Application Insights.
- Azure Container Registry.
- Cosmos DB account, database, containers, partition keys, and indexes.
- Key Vault with RBAC authorization.
- Azure AI Foundry project and Azure OpenAI connection/deployment inputs.
- Container Apps environment.
- User-assigned managed identities and least-privilege role assignments.
- API, web, and worker Container Apps with scaling, probes, and diagnostics.

## 5. Security Decisions

- Use workload managed identities and Azure RBAC.
- Do not emit credentials, keys, or connection strings in deployment outputs.
- Use HTTPS-only ingress and minimum TLS 1.2 where configurable.
- Run application containers as non-root.
- Mark sensitive parameters and outputs appropriately.
- Keep production network isolation extensible without making local/demo deployment impossible.
- Assign only the roles required by each component.

## 6. Reliability and Operations

- Configure startup, liveness, and readiness probes for HTTP workloads.
- Configure retries and idempotency in application workers.
- Use Container Apps scaling bounds and resource requests/limits.
- Enable Azure Monitor diagnostics and OpenTelemetry export.
- Parameterize zone redundancy and retention where service/region support varies.

## 7. Cost Controls

- Default to low-cost development SKUs and consumption/serverless options.
- Parameterize model deployment capacity, Cosmos throughput mode, retention, and replica counts.
- Apply consistent resource tags for ownership and cleanup.

## 8. Validation Plan

1. Build and test the .NET solution and web application.
2. Build every container image.
3. Verify containers run as non-root and health endpoints respond.
4. Run core Azure CLI validation:
   - [x] Confirm the Azure CLI is installed and authenticated to the intended subscription.
   - [x] Compile `Infrastructure\main.bicep`.
   - [x] Validate the template at resource-group scope.
   - [x] Run a resource-group what-if preview and confirm there are no deletes.
5. Run `az bicep lint` and compile `Infrastructure\main.dev.bicepparam`.
6. Review assigned Azure Policy constraints for the active subscription.
7. Confirm region availability and relevant quota for planned resource types and the model deployment.
8. Run security and static role checks against generated Bicep and Dockerfiles.

## 9. Outputs

- Resource identifiers and public application URLs.
- Managed identity principal/client IDs needed by deployment automation.
- No secrets, keys, or connection strings.

## 10. Task 13 Completion Gate

Task 13 is complete when container and Bicep builds pass, least-privilege identity wiring is represented, probes and diagnostics are configured, task evidence is recorded, and the plan status is updated to `Ready for Validation`.

## 11. Preparation Result

- Modular Bicep is under `Infrastructure`, including current schemas for Foundry,
  Azure OpenAI, ACR, Container Apps, Cosmos DB, monitoring, Key Vault, identities,
  RBAC, diagnostics, scaling, and probes.
- Six non-root image definitions are present: API, web, shadow evaluator, metrics
  aggregator, promotion engine, and rollback engine.
- Workloads authenticate with user-assigned managed identity. The only Container Apps
  secret is a versionless Key Vault reference to the generated Application Insights
  connection string; no keys or credentials are output.
- `Infrastructure/main.dev.bicepparam` contains development placeholders only.
  `Infrastructure/README.md` documents authenticated what-if without running it.
- Bicep build, lint, and parameter compilation pass. Full .NET and web validation and
  dependency vulnerability scans pass.
- Docker Desktop was unavailable, so the installed Podman Linux engine was used as an
  OCI-compatible fallback. All six images built successfully, passed non-root image
  inspection, and the API and web `/health` endpoints returned HTTP 200 when run
  together.

## 12. Validation Proof

Live Azure validation completed at `2026-09-23T14:50:32Z` using the existing
`RBR-NonProd` Azure CLI context and the existing `DefaultResourceGroup-SEC` resource
group in Sweden Central. No resources were deployed or changed.

- [x] Shared Azure validation recipe:
  `validate-deployment.ps1 -Scope group -ResourceGroup DefaultResourceGroup-SEC
  -Template Infrastructure\main.bicep -Parameters Infrastructure\main.dev.bicepparam`.
  Azure CLI authentication, Bicep compilation, ARM resource-group validation, and
  what-if all passed.
- [x] What-if result: 54 creates, 0 modifications, and 0 deletes.
- [x] `az bicep lint --file Infrastructure\main.bicep`.
- [x] `az bicep build-params --file Infrastructure\main.dev.bicepparam`.
- [x] Applicable policy query:
  `az policy assignment list --scope <validation-resource-group-scope>
  --disable-scope-strict-match true`; no policy assignments were returned.
- [x] Provider metadata confirms Sweden Central availability for Container Apps,
  Container Registry, Cosmos DB, Cognitive Services, Key Vault, Application Insights,
  and Log Analytics.
- [x] `az cognitiveservices model list --location swedencentral` confirms
  `gpt-4.1-mini` version `2025-04-14`, `GlobalStandard`, and maximum deployment
  capacity 3; the requested capacity is 1.
- [x] `az cognitiveservices account list-skus --kind OpenAI --location
  swedencentral` confirms the `S0` SKU.
- [x] Azure quota checks report no fixed limits for ACR, Cosmos DB, Key Vault,
  Application Insights, or Log Analytics. The Container Apps usage endpoint returned
  no regional usage entries. The model catalogue and ARM validation accepted the
  requested Azure OpenAI capacity.
- [x] `dotnet build GenAIOps.slnx --configuration Release --no-restore`: succeeded
  with 0 warnings and 0 errors.
- [x] `npm run build` in `src\Web`: succeeded.
- [x] Static RBAC verification reconfirmed resource-scoped ACR Pull, Key Vault Secrets
  User, Monitoring Metrics Publisher, Cognitive Services OpenAI User, Azure AI
  Developer, and Cosmos DB Built-in Data Contributor assignments. No generic Owner or
  Contributor workload role is present.

Previously completed local validation remains valid:

- [x] Azure CLI 2.89.1 and Bicep CLI 0.42.1 available.
- [x] `az bicep build --file Infrastructure\main.bicep`.
- [x] `az bicep lint --file Infrastructure\main.bicep`.
- [x] `az bicep build-params --file Infrastructure\main.dev.bicepparam`.
- [x] `dotnet test GenAIOps.slnx --configuration Release --no-restore`: 114 passed.
- [x] `dotnet format GenAIOps.slnx --verify-no-changes --no-restore`.
- [x] Web lint, typecheck, test (11 passed), and production build.
- [x] NuGet transitive vulnerability scan and `npm audit --audit-level=high`: none found.
- [x] Static Docker checks: all six final stages specify non-root users; no credential
  literals found.
- [x] Podman OCI image builds for API, web, shadow evaluator, metrics aggregator,
  promotion engine, and rollback engine.
- [x] Image configuration inspection confirms non-root users for all six images.
- [x] API and web container health probes returned HTTP 200.
- [x] ARM validation, Azure Policy review, regional availability checks, quota
  preflight, and what-if completed using the current Azure CLI context.

## 13. Role Assignment Verification

- **Status:** Verified statically.
- API and all workers: scoped Cosmos DB Built-in Data Contributor, Azure AI Developer,
  Cognitive Services OpenAI User, Key Vault Secrets User, Monitoring Metrics Publisher,
  and ACR Pull.
- Web: scoped ACR Pull only.
- Foundry account/project identities: scoped Cognitive Services OpenAI User on the
  configured Azure OpenAI account.
- No generic Owner, Contributor, or subscription-scoped workload role is assigned.
- A local developer requires equivalent data-plane roles for authenticated local tests;
  no live role changes were made.

## 14. Task 15 Offline Validation Evidence

Validation on 2026-09-23 was local and did not deploy or mutate Azure resources:

- Fresh NuGet and npm restores used the checked-in Microsoft package proxies only.
- Release build passed with zero warnings; 117 .NET tests passed.
- Web lint/typecheck, 11 tests, and production build passed.
- Prompt artifact validation passed. Deterministic v2 evaluation passed with 94% task
  adherence, 97% groundedness, and 95% tool accuracy.
- Bicep build, lint, and development parameter compilation passed.
- Four GitHub workflows passed syntax, immutable-action, OIDC, permission, and
  concurrency validation.
- The isolated smoke demo passed all seven phases in 13.327 seconds wall-clock
  (354 milliseconds scenario time), ending with v2 restored.
- .NET formatting checks and NuGet/npm vulnerability checks passed; no vulnerable
  packages were reported.
- ARM validation, Azure Policy review, regional availability checks, quota preflight,
  and what-if were completed later under Section 12. Deployment remains unexecuted.
