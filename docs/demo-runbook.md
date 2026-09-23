# GenAIOps demonstration runbook

## Safety and data sources

`Invoke-EndToEndDemo.ps1` uses deterministic, process-local repositories, fake agent
responses, fixed v2/v3 scores, and synthetic identifiers. It never connects to Azure,
Cosmos DB, or live agents and never presents seeded records as operational telemetry.
The controlled v3 incident fixture is applied only after the real promotion gate rejects
v3; production quality thresholds are unchanged.

## Prepare once

Install the prerequisites listed in the root README. From a clean checkout, restore only
through the checked-in proxies:

```powershell
dotnet restore GenAIOps.slnx --configfile NuGet.config --force --no-cache
npm --prefix src\Web ci --userconfig src\Web\.npmrc --prefer-online
dotnet build GenAIOps.slnx --configuration Release --no-restore
```

For local UI development, run the API launch profile and Vite commands from the README.
No credential is needed because Development selects fake providers and in-memory
persistence. Use `Ctrl+C` in each terminal to stop them.

## Timed seven-phase demonstration

Target: **under 15 minutes** after preparation. The automated path normally completes
in seconds.

| Time | Phase | Narration and expected proof |
|------|-------|------------------------------|
| 0:00-1:00 | 1. v1 baseline | A visible chat is assigned to production v1. |
| 1:00-2:00 | 2. v2 candidate | v2 is registered while v1 remains production. |
| 2:00-4:00 | 3. v2 shadow | Three hidden v2 responses are evaluated; candidate text never reaches the visible response. |
| 4:00-5:30 | 4. Promote v2 | The measured v2 snapshot passes unchanged quality gates and v2 becomes production. |
| 5:30-7:00 | 5. A/B | Start 90% v2 / 10% v1; deterministic assignment keys prove both routes. |
| 7:00-9:30 | 6. Poor v3 | Three intentionally poor v3 samples regress; promotion returns a gate rejection and v2 remains production. |
| 9:30-11:00 | 7. Rollback | An explicitly labelled in-memory incident fixture sets v3, then release history restores v2. |

Run:

```powershell
Measure-Command { .\scripts\Invoke-EndToEndDemo.ps1 -NoBuild }
.\scripts\Invoke-EndToEndDemo.ps1 -NoBuild -Json
```

Success ends with `SMOKE DEMO PASS: 7/7 phases` and `production=v2`. JSON output includes
the data-source label, seven phase results, final version, and elapsed milliseconds.

## Full local verification

```powershell
dotnet test GenAIOps.slnx --configuration Release --no-build
npm --prefix src\Web run lint
npm --prefix src\Web run typecheck
npm --prefix src\Web test -- --run
npm --prefix src\Web run build
dotnet run --project src\Api\GenAIOps.Api.csproj --configuration Release --no-build -- validate-prompts
node src\Web\scripts\validate-content.mjs --candidate v2
az bicep build --file Infrastructure\main.bicep
az bicep lint --file Infrastructure\main.bicep
npm --prefix src\Web run validate:workflows
dotnet format GenAIOps.slnx --verify-no-changes --no-restore
```

## Azure preparation and GitHub configuration

1. Copy `Infrastructure/main.dev.bicepparam` outside source control and replace only
   generic placeholders; keep secrets out of parameter files.
2. Confirm regional model availability, quota, naming, and Azure Policy.
3. Run Bicep build/lint and an authenticated resource-group `what-if` as documented in
   `Infrastructure/README.md`. A what-if is not a deployment.
4. Create `dev`, `staging`, and `production` GitHub environments with required
   reviewers. Configure environment variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
   and `AZURE_SUBSCRIPTION_ID`.
5. Configure one federated credential per environment and least-privilege,
   resource-group-scoped roles. Never configure a client secret.
6. Follow `.github/workflows/README.md` for immutable image digests, concurrency, and
   candidate, promotion, and rollback workflows.

## Operational checks

- **Security:** verify managed identity, Key Vault references, HTTPS, non-root
  containers, protected environments, and absence of credential literals. Run NuGet
  and npm vulnerability checks before release.
- **Accessibility:** `npm test` checks semantic navigation, dialogs, live status,
  responsive navigation, keyboard-focusable tables, and the accessible chart data
  table. Complete keyboard, zoom, contrast, and screen-reader checks in the deployed
  environment.
- **Telemetry:** use one correlation ID to connect chat, provider, shadow, evaluation,
  aggregation, routing, and release spans. Confirm candidate content is absent from the
  user response and sensitive prompt/input text is not attached to telemetry.
- **Recovery:** stale ETags and duplicate idempotency keys must fail safely; a rejected
  v3 must not alter routing. Rollback must record actor, target, and result.

## Troubleshooting

| Symptom | Action |
|---------|--------|
| Restore contacts another registry | Re-run with `NuGet.config` or `src\Web\.npmrc`; do not bypass the proxy. |
| API is unreachable from Vite | Start the `http` launch profile and verify `/health` on port 5046. |
| Demo reports missing assets | Run the proxy-only restore and Release build, then retry without `-NoBuild`. |
| Promotion is rejected | Inspect sample count and gate reasons; do not lower production thresholds to make a demo pass. |
| A/B request fails | Supply a stable assignment key while an experiment is running. |
| Azure what-if fails | Check selected subscription, provider registration, policy, region availability, and quota; no deployment is required for this demo. |
| SignalR is disconnected | Verify the `/hubs` WebSocket proxy/ingress and use polling data while reconnecting. |

## Cleanup and cost controls

Stop local processes with `Ctrl+C`; the deterministic runner leaves no data. Delete
generated `bin`, `obj`, and `src\Web\dist` directories only when a clean rebuild is
needed. For a disposable Azure demo, first retain required audit evidence and then:

```powershell
az group delete --name <demo-resource-group> --yes --no-wait
```

Use a dedicated resource group, owner/cost-center tags, budgets and alerts, short log
retention, minimum model capacity, serverless Cosmos DB, consumption Container Apps,
bounded replicas, and disabled idle environments. Review forecast and actual cost
before increasing capacity. The command above is destructive; verify the generic
placeholder is replaced with the intended disposable resource group.
