# Task 09A: OpenTelemetry Observability

**Status:** complete
**Depends on:** Tasks 04, 05, 06, 07, and 08  
**Commit subject:** `feat: add OpenTelemetry observability`

## Goal

Provide end-to-end traces, low-cardinality business metrics, and structured logs across the API and workers.

## Scope

- Configure OpenTelemetry for ASP.NET Core, HTTP clients, workers, and Azure SDK activity.
- Configure optional Azure Monitor/Application Insights export with explicit startup validation.
- Trace production chat, shadow work, evaluation, aggregation, experiment routing, promotion, and rollback.
- Define bounded business metrics for success, failure, rejection, latency, and sample counts.
- Propagate W3C trace context through durable shadow work.
- Exclude prompts, credentials, provider payloads, actor IDs, idempotency keys, assignment keys, and PII from telemetry.
- Add listener-based unit and integration tests.

## Acceptance Criteria

- A chat trace links production invocation, shadow execution, evaluation, and aggregation.
- Business failures are recorded as failures and never emitted as successful outcomes.
- Azure Monitor export is disabled by default and fails startup clearly when enabled without required configuration.
- Tests prove the telemetry tag allowlist and sensitive-value exclusion.

## Validation

```powershell
dotnet test GenAIOps.slnx --filter "Telemetry|Observability"
dotnet test GenAIOps.slnx
```

## Evidence

- Configured OpenTelemetry hosting for the API and all worker processes with ASP.NET Core,
  `HttpClient`, application, and Azure SDK activity sources.
- Added optional Azure Monitor trace, metric, and log export that is disabled by default and
  rejects enabled configurations without a connection string during startup.
- Added bounded tracing and metrics for chat, shadow evaluation, aggregation, routing, quality
  gates, promotion, and rollback with W3C context persisted through durable shadow work.
- Added listener-based unit and integration coverage for W3C correlation, the application tag
  allowlist, sensitive-value exclusion, and success, failure, and rejection outcomes.
- Validated with fresh restore, build, filtered and full tests, format verification, frontend
  checks, and `git diff --check`.
