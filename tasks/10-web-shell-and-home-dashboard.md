# Task 10: Web Application Shell and Home Dashboard

**Status:** complete
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

## Evidence

- Added an accessible responsive Fluent UI shell with all five routes, keyboard/touch navigation,
  skip link, error boundary, TanStack Query defaults, typed REST contracts, and typed SignalR
  invalidation for metrics, evaluations, versions, experiments, and releases.
- Added Home assignment status, current quality metrics, cached/loading/empty/error states, active
  experiment and recent release updates, and responsive Recharts quality trends.
- Applied the Clawpilot-compatible light/dark `--cp-*` token palette with early `scoutTheme`
  detection and Segoe UI typography.
- `npm ci`, lint, 5 component/user tests, and the production build passed using packagefeedproxy.
- Relevant .NET contract coverage passed 43 tests (28 unit and 15 integration).
- Browser runtime validation passed at 390x844 with no horizontal overflow, visible mobile
  navigation, live SignalR status, production/candidate data, metrics, and chart content.
