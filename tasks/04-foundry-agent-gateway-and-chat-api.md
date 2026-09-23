# Task 04: Foundry Agent Gateway and Chat API

**Status:** complete  
**Depends on:** Tasks 02 and 03  
**Commit subject:** `feat: add Foundry chat gateway`

## Goal

Send user requests to the registered production Azure AI Foundry Agent while preserving a provider-independent application boundary.

## Scope

- Define the chat request, response, citation, tool-call, usage, and correlation contracts.
- Implement an Azure AI Foundry Agent Service adapter using managed identity.
- Resolve the active production version through the registry.
- Implement `POST /api/chat` with validation, cancellation, correlation IDs, and explicit error mapping.
- Record request metadata needed for later evaluation without storing secrets.
- Add fake-gateway unit tests and API integration tests.

## Acceptance Criteria

- Only the production response is returned to the caller.
- No API key or connection string is required in source control.
- Invalid input, missing production deployment, provider failure, and cancellation produce documented errors.
- Correlation data is propagated for later shadow and telemetry processing.

## Validation

```powershell
dotnet test GenAIOps.slnx --filter "Chat|FoundryGateway"
```

## Execution Research

- Tasks 01-03 establish a .NET 10 Minimal API with Domain → Application → Infrastructure
  dependency direction, in-memory/Cosmos repositories, and an agent registry whose production
  assignment contains the provider agent name and prompt version. Task 04 preserves that boundary:
  provider-independent chat DTOs and `IChatGateway` live in Application, while Azure SDK types
  remain in Infrastructure.
- The current GA Foundry (new) .NET pattern uses `Azure.AI.Projects` 2.0.1,
  `Azure.AI.Projects.Agents` 2.0.0, `Azure.AI.Extensions.OpenAI` 2.0.0, and `Azure.Identity`
  1.21.0. It creates an `AIProjectClient` with `DefaultAzureCredential`, creates a project
  conversation, binds a Responses client to an `AgentReference(name, version)`, and calls
  `CreateResponseAsync`. This replaces the classic persistent thread/run API and supports
  cancellation on every network operation.
- Production gateway selection resolves the active assignment from `IAgentRegistryService`; no
  production assignment is an explicit application error. Development and test configuration use
  a deterministic local gateway so endpoint probes and tests need no Azure credentials. The Azure
  adapter requires only a Foundry project endpoint plus optional user-assigned managed-identity
  client ID; API keys and connection strings are neither accepted nor persisted.
- The response contract carries display text, correlation/provider response IDs, citations,
  observable tool calls, and token usage. Provider response models are mapped inside Infrastructure
  so Azure/OpenAI SDK types do not leak into API or Application.
- Safe request metadata is a new persisted record containing correlation ID, registry ID,
  production agent/prompt version, timestamp, input character count, completion outcome, and
  provider response ID. User content, credentials, provider payloads, tool arguments/results, and
  hidden reasoning are deliberately excluded. A dedicated requests container keeps this telemetry
  separate from the registry.
- Affected units are Domain, Application, Infrastructure, API, UnitTests, and IntegrationTests.
  Validation covers deterministic success, input validation, missing production, mapped provider
  failure, cancellation, metadata safety, the filtered task tests, full solution build/tests,
  formatting, and a real local HTTP probe.
- Decomposition verdict: atomic. This is one bounded chat vertical slice whose contracts, registry
  resolution, provider adapter, API mapping, safe metadata, and tests must agree. No scenario skill
  root, Execution-stage file, or Breakdown Hints files were provided; the Execution extension lookup
  returned no applicable guidance.
- Repository restore policy uses only Microsoft's package proxies: root `NuGet.config` clears
  inherited sources and selects `https://packagefeedproxy.microsoft.io/nuget/v3/index.json`, while
  `src/Web/.npmrc` selects `https://packagefeedproxy.microsoft.io/npm/` for the frontend project.
  Validation uses fresh isolated package directories so default NuGet and npm registries cannot
  satisfy requests from local caches.

## Evidence

- `dotnet test GenAIOps.slnx --no-build --filter "Chat|FoundryGateway"`: passed 15 tests
  (9 unit and 6 integration).
- `dotnet build GenAIOps.slnx --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test GenAIOps.slnx --no-build`: passed 44 tests (34 unit and 10 integration).
- `dotnet format GenAIOps.slnx --no-restore --verify-no-changes`: passed.
- `npm --prefix src\Web run build`: passed TypeScript compilation and the Vite production build.
- Runtime probe: development fake `POST /api/chat` returned HTTP 200 with correlation ID
  `runtime-probe-final`, citation, tool call, token usage, and persisted safe metadata.
- Exact-proxy validation: repository source inspection listed only
  `https://packagefeedproxy.microsoft.io/nuget/v3/index.json` and
  `https://packagefeedproxy.microsoft.io/npm/`. Empty-cache NuGet restore and npm clean install
  succeeded; the lockfile contains 238 package URLs all on `packagefeedproxy.microsoft.io`.
  The subsequent .NET build passed with 0 warnings/errors, filtered tests passed 15/15, full tests
  passed 44/44, format verification passed, and frontend build/test/lint all passed.
