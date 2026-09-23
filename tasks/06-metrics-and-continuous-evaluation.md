# Task 06: Metrics Aggregation and Continuous Evaluation

**Status:** blocked  
**Depends on:** Task 05  
**Commit subject:** `feat: add evaluation metrics aggregation`

## Goal

Aggregate evaluation results into reliable version-level quality and operational metrics.

## Scope

- Implement `src/Workers/MetricsAggregator`.
- Aggregate task adherence, groundedness, tool accuracy, latency, failure rate, sample count, and time windows.
- Make aggregation replay-safe and idempotent.
- Persist immutable metric snapshots.
- Expose `GET /api/metrics` with version and time-range filters.
- Add scheduled continuous evaluation for the checked-in evaluation dataset.
- Add tests for empty windows, late events, retries, and weighted aggregation.

## Acceptance Criteria

- Metrics reproduce the baseline values for deterministic fixtures.
- API results identify sample count and aggregation window.
- Reprocessing an evaluation does not double count it.
- Continuous evaluation can run locally with provider fakes.

## Validation

```powershell
dotnet test GenAIOps.slnx --filter "Metrics|Aggregation|ContinuousEvaluation"
```

