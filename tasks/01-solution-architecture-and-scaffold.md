# Task 01: Solution Architecture and Project Scaffold

**Status:** complete
**Depends on:** Task 00  
**Commit subject:** `feat: scaffold GenAIOps solution`

## Goal

Create a buildable .NET 10 and React/TypeScript solution that establishes the repository structure and shared engineering conventions.

## Scope

- Create `GenAIOps.slnx`.
- Create `src/Api`, `src/Workers/ShadowEvaluator`, `src/Workers/MetricsAggregator`, `src/Workers/PromotionEngine`, and `src/Workers/RollbackEngine`.
- Create shared projects for domain models, application contracts, and infrastructure adapters.
- Create `src/Web` with React, TypeScript, Fluent UI v10, TanStack Query, Recharts, and SignalR dependencies.
- Create unit and integration test projects.
- Add root build, formatting, and test configuration.
- Add architecture documentation describing project boundaries and dependency direction.

## Out of Scope

- Business workflows, Azure resources, and production integrations.

## Acceptance Criteria

- The .NET solution restores and builds on .NET 10.
- The web application installs, type-checks, and builds.
- Test projects run successfully with an initial smoke test.
- Project references enforce domain/application independence from infrastructure and presentation.

## Validation

```powershell
dotnet restore GenAIOps.slnx
dotnet build GenAIOps.slnx --no-restore
dotnet test GenAIOps.slnx --no-build
npm --prefix src\Web ci
npm --prefix src\Web run build
```


## Execution Research

- The repository initially contained only the specification and task documents; there were no
  existing source projects or conventions to preserve.
- The installed toolchain provides .NET SDK 10.0.401, Node.js 24.16.0, and npm 11.13.0.
- The solution contains independent `GenAIOps.Domain` and `GenAIOps.Application` class libraries,
  with Application referencing only Domain. `GenAIOps.Infrastructure` references Application and
  Domain, while the API and worker hosts reference Application and Infrastructure.
- Host projects map directly to `src/Api` and the four required folders below `src/Workers`. Unit
  and integration test projects live below `tests`.
- The web scaffold uses Vite with React and TypeScript and declares
  `@fluentui/react-components`, `@tanstack/react-query`, `recharts`, and `@microsoft/signalr`.
  The current published Fluent UI React Components package is 9.74.8; no v10 package major is
  published, so the current Microsoft Fluent React package is used rather than an unavailable one.
- Root conventions are established through `global.json`, `Directory.Build.props`,
  `Directory.Packages.props`, `.editorconfig`, and an architecture test guarding dependency direction.
- Decomposition verdict: atomic. This is one foundational scaffold whose project graph, tests,
  documentation, and lock files must be generated and validated together; it does not implement
  application workflows from later tasks. No scenario skill root or breakdown-hint files were provided.

## Evidence

- `dotnet restore GenAIOps.slnx`: passed.
- `dotnet build GenAIOps.slnx --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test GenAIOps.slnx --no-build`: passed 3 tests (2 unit/architecture and 1 integration).
- `npm --prefix src\Web ci`: passed with 0 vulnerabilities.
- `npm --prefix src\Web run build`: passed TypeScript compilation and the Vite production build.
- Additional checks: frontend smoke test, frontend lint, and .NET formatting all passed.
