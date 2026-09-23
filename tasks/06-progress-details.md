# Task 06 Progress Details

## 2026-09-23

- Added replay-safe metrics aggregation over terminal shadow evaluation records. Aggregation
  de-duplicates evaluation IDs, groups by candidate prompt version, uses explicit half-open UTC
  windows, and calculates task adherence, groundedness, tool accuracy, candidate latency, failure
  rate, successful/failure counts, and total sample count.
- Added immutable snapshots with stable source-set hashes and source evaluation IDs. Exact replay is
  a no-op; a late event creates a new immutable revision. Query and weighted-combination logic
  selects the most complete/latest revision for a logical registry/version/window to prevent double
  counting.
- Added weighted aggregation based on applicable sample counts rather than averaging snapshot
  averages.
- Added stable keyset pagination bound to registry/version/time filters. `GET /api/metrics` validates
  page size, UTC ranges, ordering, prompt version, and continuation tokens, and returns explicit
  window and sample metadata.
- Added scheduled continuous evaluation over checked-in `EvaluationData`. IDs include the aligned
  schedule period, making retries within a period idempotent while later schedules produce fresh
  samples. The scheduler aggregates the current period and revisits the prior closed window for
  bounded late-arrival revisions.
- Added deterministic local provider baselines matching v1 82/89/84, v2 94/97/95, and v3 72/75/58.
  Production composition uses gateway/evaluator-backed continuous evaluation and fails startup when
  required Foundry configuration is absent rather than writing fake scores.
- Added unit and integration coverage for empty windows, replay, concurrent identity behavior,
  duplicate evaluations, late revisions, weighted aggregation, failed samples, deterministic
  baselines, later schedules, retry resume, filtering, keyset pagination, invalid filters, and empty
  queries.
- Runtime probe returned a filtered v2 snapshot with a 24-hour window, sample count 3, failure rate
  0, task adherence 0.94, groundedness 0.97, tool accuracy 0.95, and deterministic latency.
- Final validation: fresh proxy-only restore passed; build passed with 0 warnings/errors; filtered
  tests passed 21/21; full .NET tests passed 76/76 (55 unit and 21 integration); format verification
  passed; frontend build, test (1/1), and lint passed.
