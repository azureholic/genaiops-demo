# Task 15: End-to-End Demo Validation and Documentation

**Status:** complete
**Depends on:** Tasks 11, 12, and 14  
**Commit subject:** `docs: finalize GenAIOps demonstration`

## Goal

Prove the complete seven-phase demonstration and document a repeatable run that finishes in less than fifteen minutes.

## Scope

- Add an automated smoke test for chat, shadowing, evaluation, promotion, A/B routing, regression detection, and rollback.
- Seed or generate deterministic demo data for v1, v2, and v3 without falsifying live operational state.
- Document prerequisites, local development, Azure deployment, GitHub configuration, troubleshooting, cleanup, and cost controls.
- Add a timed demonstration runbook covering all seven phases from the specification.
- Verify accessibility, security-sensitive configuration, telemetry correlation, and failure recovery.
- Record final expected outputs and screenshots only where they improve reproducibility.

## Acceptance Criteria

- The full demonstration completes in less than fifteen minutes under documented prerequisites.
- The run shows v1 baseline, v2 shadowing and promotion, 90/10 A/B routing, v3 regression, and rollback to v2.
- All automated tests and builds pass from a clean checkout.
- Documentation contains no credentials, tenant-specific secrets, or undocumented manual fixes.

## Validation

```powershell
dotnet restore GenAIOps.slnx
dotnet build GenAIOps.slnx --no-restore
dotnet test GenAIOps.slnx --no-build
npm --prefix src\Web ci
npm --prefix src\Web test -- --run
npm --prefix src\Web run build
az bicep build --file Infrastructure\main.bicep
```

## Completion Evidence

- `scripts\Invoke-EndToEndDemo.ps1 -NoBuild` completed all seven phases in
  13.327 seconds wall-clock (354 milliseconds inside the scenario) and restored v2.
- The smoke path uses labelled deterministic in-memory fixtures. The controlled v3
  incident is isolated from live state and runs only after the unchanged production
  quality gate rejects v3.
- Fresh NuGet and npm restores used only the checked-in Microsoft package proxies.
- Release build passed with zero warnings; 117 .NET tests passed.
- Web lint/typecheck passed; 11 tests and the production build passed.
- Prompt artifacts and three fixtures passed validation; deterministic v2 evaluation
  passed at 94% task adherence, 97% groundedness, and 95% tool accuracy.
- Bicep build, lint, and parameter compilation passed; four GitHub workflows passed
  syntax, immutable-action, OIDC, permission, and concurrency checks.
- `dotnet format` and verify-no-changes passed. NuGet transitive and npm high-severity
  vulnerability checks found none. `git diff --check` passed.
- Azure deployment was not run.
