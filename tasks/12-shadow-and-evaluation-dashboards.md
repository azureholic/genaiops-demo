# Task 12: Shadow Testing and Evaluation Dashboards

**Status:** blocked  
**Depends on:** Tasks 05, 06, and 10  
**Commit subject:** `feat: add shadow and evaluation dashboards`

## Goal

Make production-versus-candidate quality, regressions, and evaluation evidence understandable during the demo.

## Scope

- Implement the Shadow Testing page with candidate status, throughput, failures, latency, and recent comparisons.
- Implement the Evaluation Dashboard with metric cards, trends, filters, sample counts, and quality-gate indicators.
- Highlight the specified v2 improvement and v3 regression.
- Add drill-down views that avoid exposing sensitive prompt content by default.
- Refresh views from SignalR notifications.
- Add component and user-flow tests.

## Acceptance Criteria

- Users can compare production and candidate metrics over the same window.
- v3 regressions are visually obvious and tied to gate failures.
- Empty, delayed, partial, and failed evaluations are represented accurately.
- Charts remain accessible through text summaries or tables.

## Validation

```powershell
npm --prefix src\Web test -- --run
npm --prefix src\Web run build
```

