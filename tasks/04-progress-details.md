# Task 04 Progress Details

## 2026-09-23

- Added provider-independent Application chat contracts, typed errors, gateway/service interfaces,
  registry-based production resolution, cancellation flow, and correlation propagation.
- Added the GA Foundry (new) adapter using `Azure.AI.Projects` 2.0.1,
  `Azure.AI.Projects.Agents` 2.0.0, `Azure.AI.Extensions.OpenAI` 2.0.0, and `Azure.Identity`
  1.21.0. The adapter uses `AIProjectClient`, project conversations, agent-bound Responses, and
  `DefaultAzureCredential` with optional user-assigned managed identity.
- Added a deterministic development/test gateway and development-only local production assignment.
  No API keys, connection strings, request bodies, tool arguments/results, provider payloads,
  credentials, secrets, or hidden reasoning are persisted.
- Added safe request metadata persistence in the requests container: opaque record ID, correlation
  ID, registry ID, production agent/prompt version, timestamp, input character count, outcome, and
  provider response ID.
- Added `POST /api/chat` with 8,000-character input validation, generated or caller-provided
  correlation IDs, citations, tool calls, token usage, request cancellation, and RFC 7807 errors for
  invalid input, missing production, provider failure, cancellation, and metadata persistence.
- Added unit and integration coverage for deterministic success, invalid/malformed input, missing
  production, provider failure, cancellation during registry/provider work, metadata safety/failure,
  persistence mapping, and endpoint response shape.
- Review issues resolved: registry cancellation is typed, metadata failures cannot be misclassified
  as provider failures, and malformed JSON receives a correlated 400 response.
- Validation: filtered Task 04 tests passed 15/15; full .NET tests passed 44/44; solution build
  passed with 0 warnings and 0 errors; .NET format verification passed; frontend production build
  passed.
- Runtime probe: local fake `POST http://127.0.0.1:5099/api/chat` returned HTTP 200,
  `X-Correlation-ID: runtime-probe-final`, the expected local response, one citation, one tool call,
  and token usage.
- Deviations: none. No live Foundry call was attempted because no Azure project or credentials are
  required for deterministic local validation.

## 2026-09-23 Microsoft Feed Follow-up

- Added root `NuGet.config` with `<clear />` and only Microsoft's public `dotnet-public` feed:
  `https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/index.json`.
- Verified the repository config lists exactly one enabled source and does not inherit NuGet.org or
  machine-level sources.
- Deleted the isolated validation package cache, then completed `dotnet restore --force --no-cache`
  into that empty cache with the repository config, proving all solution dependencies are available
  from the Microsoft feed.
- Using only those restored packages, the solution build passed with 0 warnings/errors, Task 04
  tests passed 15/15, full tests passed 44/44, and format verification passed.

## 2026-09-23 Package Proxy Correction

- Replaced the earlier NuGet source with the required Microsoft proxy:
  `https://packagefeedproxy.microsoft.io/nuget/v3/index.json`; `NuGet.config` still clears all
  inherited/default NuGet sources.
- Added `src/Web/.npmrc` with
  `registry=https://packagefeedproxy.microsoft.io/npm/` and
  `replace-registry-host=always`, then normalized every package-lock tarball URL to that proxy so
  `npm ci` cannot bypass it through previously resolved feed URLs.
- Verified the active NuGet source and npm registry exactly match the required proxies. Scans found
  no NuGet.org, dnceng, npmjs.org, or direct Azure DevOps feed URL in the active package config or
  lockfile; all 238 resolved npm package URLs use `packagefeedproxy.microsoft.io`.
- With empty NuGet and npm caches, forced solution restore and `npm ci` both passed. The subsequent
  solution build passed with 0 warnings/errors; Task 04 tests passed 15/15; full .NET tests passed
  44/44; format verification passed; and frontend build, test (1/1), and lint passed.
