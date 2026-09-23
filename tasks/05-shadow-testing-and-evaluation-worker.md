# Task 05: Shadow Testing and Evaluation Worker

**Status:** blocked  
**Depends on:** Task 04  
**Commit subject:** `feat: add shadow testing pipeline`

## Goal

Run candidate prompts against production traffic without exposing candidate responses, then evaluate and persist the comparison.

## Scope

- Publish sanitized shadow work after a successful production chat.
- Implement idempotent background processing in `src/Workers/ShadowEvaluator`.
- Invoke the candidate agent independently from the production request.
- Evaluate task adherence, groundedness, and tool accuracy through Foundry Evaluations.
- Persist production/candidate outputs, scores, latency, evaluator status, and correlation data.
- Add retry, poison-message, timeout, and duplicate-delivery handling.
- Expose `GET /api/evaluations`.

## Acceptance Criteria

- Candidate latency or failure never changes the visible production response.
- The worker safely handles duplicate messages.
- Evaluation failures are explicit and queryable.
- Tests prove hidden candidate execution and persisted score comparison.

## Validation

```powershell
dotnet test GenAIOps.slnx --filter "Shadow|Evaluation"
```

