# Task 14: GitHub Actions CI/CD Workflows

**Status:** complete
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

## Evidence

- Four workflow files pass YAML parsing and repository security-policy checks for immutable
  action SHAs, least permissions, OIDC, protected environments, and deployment concurrency.
- .NET: 115 tests passed (79 unit and 36 integration); the new candidate registration
  endpoint is covered by an integration test.
- Web: 11 tests, lint, production build, deterministic prompt validation, and v2 quality
  gates passed using the committed packagefeedproxy configuration.
- NuGet and npm vulnerability checks reported no vulnerable packages.
- Bicep build and lint passed without warnings, including conditional infrastructure-first
  provisioning and immutable image digest references.
- Promotion and rollback require explicit environment/version inputs, protected GitHub
  environments, and verify the resulting production version.
- `git diff --check` passed. No Azure deployment was run locally.
