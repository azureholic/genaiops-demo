# Task 07: Promotion and Rollback Workflows

**Status:** complete
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

## Execution Research

- Task 03 provides an optimistic-concurrency agent registry with candidate promotion and assignment
  history; Task 06 provides immutable, replay-safe metric snapshots with quality, latency, failure,
  sample, version, and explicit-window evidence. Workflow services can therefore make decisions from
  persisted evidence and update routing through ETags without depending on provider SDK types.
- Configurable quality gates will require minimum samples plus task-adherence, groundedness, and
  tool-accuracy minima, maximum failure rate, and optional maximum latency. The service selects the
  latest/most complete immutable snapshot for the candidate version and records every observed
  value, threshold, snapshot ID, sample count, and pass/fail reason as immutable release evidence.
- Promotion requires the current candidate to match the requested version and an `If-Match` registry
  ETag. It evaluates gates before routing, then calls the registry's existing ETag-protected promote
  operation. Missing evidence, insufficient samples, regressions, stale ETags, and version mismatch
  are explicit typed errors.
- Rollback also requires an ETag and restores the most recent prior production assignment retained
  in registry history. The registry gains one provider-independent, ETag-protected production
  assignment operation so rollback does not bypass its single-production/history invariants.
- `Idempotency-Key` is mandatory. Immutable release records use a stable registry/key identity and
  retain operation, actor, timestamp, before/after assignments, rollback target, gate decision, and
  evidence. Exact command replay returns the prior result without another registry mutation or
  release; reuse for a different command is rejected.
- `POST /api/promote/{version}` and `POST /api/rollback` accept actor metadata and require
  `Idempotency-Key`/`If-Match` headers. Responses include the new registry ETag and immutable release
  record; typed 400/404/409/412/422 errors distinguish malformed commands, missing state, conflicts,
  stale concurrency, and rejected gates.
- Development seeds a configurable local candidate and deterministic metrics as needed by runtime
  probes. Unit/integration tests create isolated registry/metric evidence to prove v2 acceptance, v3
  regression rejection, rollback from bad v3 to v2, stale writes, and repeat safety.
- PromotionEngine and RollbackEngine hosts compose the same application workflow services against
  configured in-memory/Cosmos persistence; their hosted command workers are configuration-driven
  and disabled unless an operation is explicitly supplied, preventing accidental routing changes.
- Affected units are Domain, Application, Infrastructure composition, API, PromotionEngine,
  RollbackEngine, UnitTests, and IntegrationTests. Package restore remains constrained to the
  committed Microsoft packagefeedproxy sources; the web project is not functionally changed.
- Decomposition verdict: atomic. Gate evaluation, registry concurrency, idempotency identity,
  immutable history, rollback-target selection, API errors, and worker composition form one routing
  transaction contract. No scenario skill root, Execution-stage file, or Breakdown Hints files were
  provided; the Execution extension lookup returned no applicable guidance.

## Completion Evidence

- Configurable gates require minimum samples and enforce task adherence, groundedness, tool accuracy,
  failure rate, and optional latency thresholds from immutable metric snapshots.
- Durable command reservations make routing changes recoverable across persistence failure or
  duplicate delivery; exact replays return the original routing result and immutable release record.
- Filtered tests passed 18/18; the full suite passed 92/92 with a clean 0-warning build and format.
- Runtime probes promoted v2, rejected regressed v3 with HTTP 422 evidence, rolled back to v1, and
  verified promotion/rollback replays reused their original release IDs.
- A fresh restore succeeded from repository `NuGet.config`; web build, test, and lint all passed.
