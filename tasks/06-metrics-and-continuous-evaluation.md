# Task 06: Metrics Aggregation and Continuous Evaluation

**Status:** complete
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

## Execution Research

- Task 05 persists one idempotent `ShadowEvaluationRecord` per correlation ID with candidate prompt
  version, lifecycle, named quality scores, latency, attempts, and timestamps. Metrics can therefore
  be derived from persisted facts without introducing another delivery queue or incrementing mutable
  counters that would double-count retries/replays.
- Application-level aggregation will read a closed explicit `[windowStart, windowEnd)` interval,
  de-duplicate evaluation IDs, group by candidate prompt version, and compute arithmetic means for
  task adherence, groundedness, tool accuracy, and candidate latency plus failure rate and sample
  count. Failed/poisoned records count toward failure rate/sample count but not quality-score means.
- Metric snapshots are immutable. Their IDs include registry, prompt version, exact UTC window, and
  a stable hash of sorted source evaluation IDs. Replaying the same source set resolves to the same
  record and is a no-op; a genuinely late event produces a new immutable revision for the same
  logical window rather than mutating prior evidence. Snapshot records retain source IDs and
  generated time so revisions remain explainable.
- Weighted aggregation combines snapshot means using each snapshot's applicable sample count rather
  than averaging averages. Empty windows produce no fabricated zero snapshot. Repository conflicts
  are treated as successful idempotent replays after reading the existing snapshot.
- `GET /api/metrics` will validate prompt-version and UTC time-range filters, enforce bounded page
  sizes, filter persisted immutable snapshots, and return explicit window/sample metadata with an
  opaque offset continuation token. API DTOs do not expose persistence implementation details.
- Scheduled continuous evaluation reads checked-in `EvaluationData` fixtures and their declared
  prompt versions behind `IContinuousEvaluationProvider`. Local/test composition uses deterministic
  results matching the checked-in v1/v2/v3 prompt baseline metrics; each fixture/version result has
  a stable ID, so schedule retries and overlapping runs remain idempotent. The provider interface is
  ready for a hosted Foundry implementation without leaking provider types.
- Development hosts the continuous-evaluation scheduler and metrics scheduler in-process for a
  credential-free HTTP runtime probe. The dedicated MetricsAggregator host uses the same services
  and shared Cosmos persistence in production.
- Affected units are Domain, Application, Infrastructure, API, MetricsAggregator, UnitTests, and
  IntegrationTests. Restore remains restricted to the committed Microsoft packagefeedproxy source;
  the web project is not functionally changed.
- Decomposition verdict: atomic. Aggregation identity, late-event revision policy, fixture replay
  identity, immutable persistence, scheduler behavior, and API filtering must share the same window
  and version semantics. No scenario skill root, Execution-stage file, or Breakdown Hints files were
  provided; the Execution extension lookup returned no applicable guidance.

## Evidence

- Fresh no-cache restore succeeded using only the repository Microsoft NuGet package proxy.
- `dotnet build GenAIOps.slnx --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test GenAIOps.slnx --no-build --filter "Metrics|Aggregation|ContinuousEvaluation"`:
  passed 21 tests (14 unit and 7 integration).
- Full solution tests passed 76/76 (55 unit and 21 integration); format verification and frontend
  build/test/lint passed.
- Runtime probe: filtered `GET /api/metrics?version=v2&from=...&to=...` returned HTTP 200 with an
  explicit 24-hour window, sample count 3, failure rate 0, and deterministic v2 task adherence 0.94,
  groundedness 0.97, and tool accuracy 0.95.
