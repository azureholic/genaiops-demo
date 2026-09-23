# Task 07 Progress Details

## 2026-09-23

- Added provider-independent promotion and rollback workflows, configurable quality gates, typed
  errors, optimistic registry concurrency, durable idempotency reservations, immutable release
  evidence, and configuration-driven PromotionEngine/RollbackEngine hosts.
- Added `POST /api/promote/{version}` and `POST /api/rollback` with mandatory idempotency and ETag
  headers. Release records retain actor, timestamps, before/after assignments, rollback target,
  exact command identity, metric evidence, and the original routing result.
- Added unit and integration coverage for v2 success, v3 regression, absent/insufficient evidence,
  stale ETags, exact and conflicting replays, concurrent commands, rollback, and missing history.
- Validation: fresh proxy-only restore passed; build passed with 0 errors and 0 warnings; Task 07
  filtered tests passed 18/18; full tests passed 92/92; `dotnet format` completed cleanly; web build,
  test (1/1), and lint passed.
- Runtime probes: v2 promotion returned success, an exact replay reused the release ID, rollback
  restored v1 and replayed safely; a separately configured v3 candidate returned HTTP 422
  `quality_gate_rejected` with the expected regressed metric evidence.
- Decomposition was assessed as atomic before source edits. No scenario skill root, Execution-stage
  file, or Breakdown Hints were supplied; the Execution extension lookup had no applicable guidance.
