# Task 08: A/B Routing and Experiment API

**Status:** complete
**Depends on:** Tasks 04 and 07  
**Commit subject:** `feat: add A/B experiment routing`

## Goal

Route production traffic deterministically between approved versions and track experiment outcomes.

## Scope

- Define experiment configuration, allocation, lifecycle, and assignment records.
- Implement `POST /api/abtest`.
- Add stable traffic assignment using an approved user/session key.
- Support the specified v2 90% and v1 10% allocation.
- Record assigned version on chat, evaluation, and telemetry records.
- Validate total allocation, eligible versions, and experiment state transitions.
- Add distribution, stickiness, validation, and concurrency tests.

## Acceptance Criteria

- The same assignment key remains in the same cohort during an experiment.
- A large deterministic sample stays within an agreed tolerance of 90/10.
- Only registered, deployable versions can receive traffic.
- Ending an experiment restores normal production routing.

## Validation

```powershell
dotnet test GenAIOps.slnx --filter "AbTest|Experiment|Routing"
```

## Execution Research

- Task 03 supplies generic ETag-aware repositories and registry assignment history; Task 04 routes
  chat through the registry and safely persists request metadata; Task 07 establishes v2 as an
  approved production release and retains v1 in production history. A/B routing can therefore stay
  provider-independent and resolve only current or previously approved production assignments.
- One `ExperimentRecord` per registry is the optimistic-concurrency aggregate. It contains the
  experiment identity/name, actor, allocations, lifecycle, and created/started/updated/ended times.
  Start creates it, update requires its current `If-Match` ETag, and end completes it with the same
  precondition. This prevents two active configurations and makes lifecycle transitions explicit.
- Allocations use integer percentages and must contain distinct versions, positive weights, and an
  exact total of 100. Every version must resolve to current production or retained production
  history; candidate-only, unknown, or retired/non-deployable versions are rejected.
- `IExperimentRouter` queries the registry experiment. When it is running, a bounded
  `X-Assignment-Key` session/user key is mandatory. SHA-256 over experiment ID plus that key maps to
  one of 100 deterministic buckets, yielding stable cohorts without persisting the identifier or a
  reusable identifier hash.
  The required demo allocation is v2 90 / v1 10. Completed experiments are ignored and chat returns
  to the registry's normal production assignment.
- Assignment metadata includes experiment ID, assigned agent/version, and timestamp. Chat request
  metadata, shadow work, and shadow evaluation carry the assigned
  experiment/version so later evaluation and Task 09 telemetry can segment outcomes without secrets
  or raw user/session identifiers.
- `POST /api/abtest` accepts explicit `start`, `update`, and `end` actions. Start returns 201 and an
  ETag; update/end require `If-Match`; malformed allocations/transitions map to 400, ineligible
  versions to 422, missing experiments to 404, and stale/concurrent writes to 412.
- Tests will cover exact validation and lifecycle transitions, stale writes, eligibility, same-key
  stickiness, deterministic large-sample 90/10 tolerance, chat metadata propagation, API contracts,
  and restoration of normal production routing after end. Runtime probes use the local fake gateway.
- Affected units are Domain, Application, Infrastructure composition, API, UnitTests, and
  IntegrationTests. No frontend behavior or package dependency is required.
- Decomposition verdict: atomic. The experiment aggregate, deterministic routing, chat selection,
  metadata propagation, endpoint state transitions, and tests are one routing contract. No scenario
  skill root, Execution-stage file, Breakdown Hints, or task-related skill block was supplied, and
  no instruction-discovery tool is available in this dispatch.

## Completion Evidence

- Added explicit start/update/end experiment commands, ETag concurrency, strict 100-percent
  allocations, approved-production eligibility, terminal lifecycle enforcement, and safe restart
  with a new experiment identity.
- Added stable SHA-256 bucket assignment for approved session/user keys. A 20,000-key deterministic
  test remained within the agreed 88-92 / 8-12 tolerance around the required v2 90 / v1 10 split.
- Chat responses and safe metadata carry the assigned version and experiment ID; shadow work and
  evaluation records propagate the same dimensions without persisting raw keys or reusable hashes.
- Filtered tests passed 11/11 and the full suite passed 102/102. Build completed with 0 errors and
  0 warnings; format verification, fresh proxy-only restore, and frontend build/test/lint passed.
- Runtime probe promoted v2, started the 90/10 experiment (HTTP 201), observed deterministic v2 and
  v1 cohorts, ended it, and verified keyless chat returned to normal v2 production routing.
