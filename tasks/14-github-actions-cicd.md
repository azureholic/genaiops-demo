# Task 14: GitHub Actions CI/CD Workflows

**Status:** blocked  
**Depends on:** Tasks 02 and 13  
**Commit subject:** `ci: add GenAIOps deployment workflows`

## Goal

Automate validation, candidate deployment, promotion, and rollback through GitHub Actions.

## Scope

- Add PR validation for build, unit tests, web tests, prompt validation, evaluation runs, Bicep validation, and quality gates.
- Add candidate deployment for infrastructure, containers, agents, and candidate registration.
- Add manually approved promotion and rollback workflows.
- Use GitHub OIDC federation and environment protections.
- Add concurrency controls, immutable image references, deployment summaries, and artifact retention.
- Pin third-party actions to immutable commit SHAs.

## Acceptance Criteria

- Pull requests cannot pass when prompt validation or quality gates fail.
- Deployment uses no long-lived Azure credentials.
- Promotion and rollback identify the version and environment explicitly.
- Concurrent deployment workflows cannot race the same environment.
- Workflow syntax and local validation pass.

## Validation

```powershell
dotnet test GenAIOps.slnx
npm --prefix src\Web test -- --run
npm --prefix src\Web run build
az bicep build --file Infrastructure\main.bicep
```

