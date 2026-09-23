# Task 08: A/B Routing and Experiment API

**Status:** blocked  
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

