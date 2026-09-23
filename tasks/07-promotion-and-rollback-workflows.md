# Task 07: Promotion and Rollback Workflows

**Status:** blocked  
**Depends on:** Tasks 03 and 06  
**Commit subject:** `feat: add promotion and rollback workflows`

## Goal

Promote candidates based on explicit quality gates and restore the previous production version safely.

## Scope

- Implement quality-gate policies using metric thresholds and minimum sample counts.
- Implement `src/Workers/PromotionEngine` and `src/Workers/RollbackEngine`.
- Implement `POST /api/promote/{version}` and `POST /api/rollback`.
- Use optimistic concurrency and idempotency keys for routing changes.
- Persist release history, gate evidence, actor, timestamp, and rollback target.
- Prevent promotion when required evidence is absent or regressed.
- Add workflow and API tests.

## Acceptance Criteria

- v2 can be promoted after passing configured gates.
- v3 is rejected when its specified regression is observed.
- Rollback restores v2 after a bad v3 release.
- Repeated commands do not create duplicate releases or corrupt routing state.

## Validation

```powershell
dotnet test GenAIOps.slnx --filter "Promotion|Rollback|QualityGate"
```

