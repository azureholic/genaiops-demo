# Azure infrastructure

This folder prepares GenAIOps for Azure Container Apps without deploying resources. The
template uses user-assigned managed identities for ACR, Cosmos DB, Azure AI, Azure
OpenAI, and Key Vault access. Local authentication is disabled for Cosmos DB, Azure
OpenAI, Foundry, and Application Insights. No credentials are emitted.

## Validate locally

```powershell
az bicep build --file Infrastructure\main.bicep
az bicep lint --file Infrastructure\main.bicep
az bicep build-params --file Infrastructure\main.dev.bicepparam
```

The development parameters contain placeholders only. Confirm that the selected region
supports the requested model/version/SKU and that subscription quota is available.

## Preview with what-if

What-if requires an authenticated Azure context but does not deploy:

```powershell
az login
az account set --subscription <subscription-id>
az deployment group what-if `
  --resource-group <existing-resource-group> `
  --parameters Infrastructure\main.dev.bicepparam
```

Do not put live secrets in parameter files. Supply any bootstrap secrets through a
secure CI secret store or an in-memory parameter value. Application workloads consume
secrets only through versionless Key Vault references.

## Images

Build from the repository root, except for the web image:

```powershell
docker build --file src\Api\Dockerfile --tag genaiops-api:dev .
docker build --file src\Web\Dockerfile --tag genaiops-web:dev src\Web
docker build --file src\Workers\ShadowEvaluator\Dockerfile --tag genaiops-shadow-evaluator:dev .
docker build --file src\Workers\MetricsAggregator\Dockerfile --tag genaiops-metrics-aggregator:dev .
docker build --file src\Workers\PromotionEngine\Dockerfile --tag genaiops-promotion-engine:dev .
docker build --file src\Workers\RollbackEngine\Dockerfile --tag genaiops-rollback-engine:dev .
```

The .NET images run as the platform-provided `$APP_UID`; the web image uses the
unprivileged nginx user. HTTP workloads expose `/health` for startup/liveness and
`/ready` for readiness probes.
