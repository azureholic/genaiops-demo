# GenAIOps demo

GenAIOps is a reference implementation for versioned Azure AI Foundry agents with
shadow evaluation, quality-gated promotion, A/B routing, rollback, OpenTelemetry, and a
React operations UI.

## Prerequisites

- .NET SDK from `global.json`
- Node.js 24 and npm
- PowerShell 7
- Azure CLI with Bicep for infrastructure validation
- Docker or an OCI-compatible engine only when building containers

All package restores must use the checked-in Microsoft package proxies:

```powershell
dotnet restore GenAIOps.slnx --configfile NuGet.config
npm --prefix src\Web ci --userconfig src\Web\.npmrc
```

Do not override those sources. The lock file is intentionally proxy-only.

## Local development

The Development environment uses in-memory persistence and deterministic fake chat and
evaluation providers; it does not require Azure credentials:

```powershell
dotnet run --project src\Api\GenAIOps.Api.csproj --launch-profile http
npm --prefix src\Web run dev
```

Open `http://localhost:5173`. Vite proxies API and SignalR traffic to
`http://localhost:5046`. Run the isolated seven-phase smoke demo with:

```powershell
.\scripts\Invoke-EndToEndDemo.ps1
```

The demo labels its in-memory fixture and never reads or writes live operational state.
See [the timed runbook](docs/demo-runbook.md) for expected output and operator checks.

## Validation and deployment

- [Architecture](docs/architecture.md)
- [Azure preparation and Bicep validation](Infrastructure/README.md)
- [GitHub OIDC and protected environments](.github/workflows/README.md)
- [Offline deployment evidence](.azure/deployment-plan.md)

Infrastructure generation and validation do not deploy. Use managed identity and
environment-scoped OIDC, never client secrets, checked-in credentials, or
tenant-specific values.

## Support

The [demo runbook](docs/demo-runbook.md) covers troubleshooting, cleanup, cost
controls, security, accessibility, and telemetry checks.
