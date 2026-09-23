# Task 05 Progress Details

## 2026-09-23

- Added sanitized shadow work contracts and post-success publication. Work contains only the
  correlation/registry and version identities, evaluation input, production output, and timestamp;
  it excludes credentials, headers, provider payloads, tool arguments/results, and hidden reasoning.
- Added local in-memory and shared persistence-backed queues. The persistent queue stores work in a
  dedicated Cosmos container, atomically claims deliveries with ETags, tracks attempts, and supports
  completion, retry, and dead-letter transitions. API production composition publishes to the
  durable queue while the dedicated worker consumes it; local development runs the same processor
  in-process for deterministic probing.
- Added atomic evaluation claims keyed by correlation ID. Concurrent duplicates cannot invoke the
  candidate twice; sequential duplicate delivery is a no-op. Retry claims advance only with a
  higher attempt number and terminal results prevent reprocessing.
- Added independent candidate invocation with linked operation timeout, bounded retries, poison
  handling, and bounded delivery-level recovery for unexpected failures.
- Added provider-independent evaluation contracts, deterministic local scores, and a Foundry
  evaluation adapter using `DefaultAzureCredential`/managed identity. The adapter requests
  task-adherence, groundedness, and tool-call-accuracy evaluators and maps normalized named scores.
- Persisted production/candidate identities and outputs, all three scores, candidate latency,
  lifecycle, attempts, timestamps, errors, and correlation data. `GET /api/evaluations` returns a
  safe summary without conversation input or output text.
- Added unit/integration coverage for publication isolation, candidate latency/failure isolation,
  success, Foundry adapter authentication/mapping, timeout, retry, exhausted poison handling,
  evaluator failure, queue transitions, concurrent/sequential duplicates, persistence, pagination
  validation, hidden candidate execution, and endpoint querying.
- Review fixes: production API and worker use a shared durable queue with Cosmos, idempotency is
  claimed before provider calls, unexpected retries are bounded, and evaluation query DTOs omit
  conversation content.
- Runtime probe completed from production chat through background candidate evaluation. The visible
  response contained only `local-support-agent/v1`; `GET /api/evaluations` later reported candidate
  `local-support-candidate/v2`, Completed lifecycle, and scores 0.8889/1.0/1.0.
- Final validation: proxy-only fresh restore passed; build passed with 0 warnings/errors; filtered
  tests passed 16/16; full tests passed 60/60; format verification passed; frontend build, test
  (1/1), and lint passed.
