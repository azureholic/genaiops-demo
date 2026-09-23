# Task 01: Solution Architecture and Project Scaffold

**Status:** ready  
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

