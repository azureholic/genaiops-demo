# Task 11: Prompt Registry and Release History Dashboards

**Status:** blocked  
**Depends on:** Tasks 07 and 10  
**Commit subject:** `feat: add registry and release dashboards`

## Goal

Visualize version metadata and release state, and expose guarded promotion and rollback controls.

## Scope

- Implement the Prompt Registry page with v1, v2, and v3 metadata and status.
- Show expected versus observed quality metrics.
- Implement the Release History page with promotion, rollback, actor, timestamp, and gate evidence.
- Add confirmation and conflict handling for promotion and rollback actions.
- Refresh both pages on real-time release events.
- Add component and user-flow tests.

## Acceptance Criteria

- Production, candidate, historical, and experiment states are distinguishable.
- Blocked promotions show the failed quality gates.
- Rollback identifies the exact target version before confirmation.
- Failed mutations remain visibly failed and are not shown as successful.

## Validation

```powershell
npm --prefix src\Web test -- --run
npm --prefix src\Web run build
```

