# Task 05: Shadow Testing and Evaluation Worker

**Status:** complete
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

## Execution Research

- Task 04 returns only the production gateway response and already resolves a registry snapshot
  containing both production and candidate assignments. Shadow publication will happen only after a
  successful production call and metadata write. The work item contains the minimum evaluation
  inputs (correlation/registry and agent versions, user input, production output) and excludes
  credentials, headers, provider payloads, tool arguments/results, and hidden reasoning.
- Provider-independent `IShadowWorkPublisher`/`IShadowWorkQueue`, `IResponseEvaluator`, and
  `IShadowEvaluationProcessor` contracts belong in Application. The chat path performs only a
  non-throwing queue publish; candidate invocation and evaluation run exclusively in the background
  processor, so candidate latency, timeout, or failure cannot alter the visible response.
- A deterministic in-memory queue supports local/test publication, delivery attempts,
  acknowledgement, retry, and dead-letter behavior. The processor uses correlation ID as its stable
  idempotency key and checks persisted results before invoking the candidate, making duplicate
  deliveries no-ops. It uses a linked timeout token for candidate/evaluator calls, returns retryable
  outcomes below the configured attempt limit, and persists an explicit failed/poison result when
  attempts are exhausted.
- A new shadow evaluation record persists production and candidate agent/prompt identity, both
  outputs, three named scores (task adherence, groundedness, tool accuracy), candidate latency,
  lifecycle/status/error, attempt count, timestamps, and correlation data. This intentionally
  persists comparison text required by the task, but never secrets or hidden reasoning.
- As of 2026-09-23 there is no dedicated GA Foundry cloud-evaluation .NET SDK. The supported GA
  in-process stack is `Microsoft.Extensions.AI.Evaluation`/`.Quality` 10.10.0 using relevance/task
  adherence, groundedness, and tool-call accuracy evaluators with an Azure-hosted judge. The
  application boundary therefore exposes the three required normalized scores; Infrastructure
  provides a Foundry adapter boundary for production composition and a deterministic fake for local
  and tests, preventing evaluation SDK types from leaking into Application/API contracts.
- `GET /api/evaluations` will provide partition-scoped pagination and explicit lifecycle/error data.
  Development hosts the same processor loop in-process with fake queue/gateway/evaluator so a real
  HTTP chat-to-evaluation runtime probe is deterministic and credential-free; the dedicated
  ShadowEvaluator worker uses the same processor contracts and loop.
- Affected units are Domain, Application, Infrastructure, API, ShadowEvaluator, UnitTests, and
  IntegrationTests. Package restore remains constrained to the committed Microsoft
  `packagefeedproxy` NuGet source; the web app is not functionally changed.
- Decomposition verdict: atomic. Although this is a vertical slice across several projects, its
  publication contract, idempotency key, retry state machine, persistence schema, worker loop, and
  query API must agree to avoid lossy or duplicate evaluation behavior. No scenario skill root,
  Execution-stage file, or Breakdown Hints files were provided; the Execution extension lookup
  returned no applicable guidance.

## Evidence

- `dotnet restore GenAIOps.slnx --packages <empty-cache> --force --no-cache`: passed using only the
  repository Microsoft NuGet package proxy.
- `dotnet build GenAIOps.slnx --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test GenAIOps.slnx --no-build --filter "Shadow|Evaluation"`: passed 16 tests
  (12 unit and 4 integration).
- `dotnet test GenAIOps.slnx --no-build`: passed 60 tests (46 unit and 14 integration).
- `dotnet format GenAIOps.slnx --no-restore --verify-no-changes`: passed.
- Frontend build, test, and lint passed using the repository Microsoft npm package proxy.
- Runtime probe: `POST /api/chat` returned only production `local-support-agent/v1`; polling
  `GET /api/evaluations` returned the hidden `local-support-candidate/v2` comparison with Completed
  lifecycle and task adherence 0.8889, groundedness 1.0, and tool accuracy 1.0.
