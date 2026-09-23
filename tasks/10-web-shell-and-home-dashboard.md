# Task 10: Web Application Shell and Home Dashboard

**Status:** blocked  
**Depends on:** Tasks 01, 06, and 09  
**Commit subject:** `feat: add dashboard shell and home page`

## Goal

Create the accessible Fluent UI application shell and a home dashboard that summarizes the GenAIOps system.

## Scope

- Add navigation for Home, Prompt Registry, Shadow Testing, Evaluation Dashboard, and Release History.
- Configure TanStack Query, typed API clients, routing, error boundaries, loading states, and SignalR connectivity.
- Implement the Home dashboard with production/candidate status, current metrics, active experiment, and recent releases.
- Use Recharts for quality trends.
- Add responsive and accessible UI tests.

## Acceptance Criteria

- Navigation is keyboard accessible and responsive.
- Loading, empty, stale, and error states are visible and actionable.
- Home data refreshes through query invalidation after SignalR events.
- No secrets or infrastructure credentials are shipped to the browser.

## Validation

```powershell
npm --prefix src\Web test -- --run
npm --prefix src\Web run build
```

