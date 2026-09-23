# Task 15: End-to-End Demo Validation and Documentation

**Status:** blocked  
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
