# Task 11: Prompt Registry and Release History Dashboards

**Status:** complete
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

## Completion Evidence

- Added responsive, accessible Fluent UI Prompt Registry and Release History pages with v1/v2/v3
  metadata, assignment/experiment/history states, expected-versus-observed metrics, immutable release
  evidence, actors, timestamps, and failed quality-gate details.
- Added guarded promotion and rollback dialogs, exact target identification, optimistic-concurrency
  conflict guidance, idempotency keys, and mutation handling that keeps failures visibly failed.
- Added `GET /api/releases` with typed web contracts and integration coverage; local development
  loads prompt metadata from the committed prompt artifacts without duplicating registry services.
- SignalR release events invalidate versions, metrics, and release history. Component/user-flow tests
  cover all registry states, failed gates, rejected promotion, stale rollback, and exact targets.
- `npm ci`, lint, 8 web tests, production build, `dotnet format`, and all 114 .NET tests passed.
  Browser checks passed in desktop light and mobile dark themes with keyboard focus, live data,
  no horizontal overflow, and no console errors.
