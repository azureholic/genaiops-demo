# Task 09: Observability and Real-Time Updates

**Status:** blocked  
**Depends on:** Tasks 04, 05, 06, 07, and 08  
**Commit subject:** `feat: add OpenTelemetry and realtime updates`

## Goal

Provide end-to-end traces, metrics, logs, and live dashboard notifications across API and workers.

## Scope

- Configure OpenTelemetry for ASP.NET Core, HTTP, workers, and Azure SDK activity.
- Export telemetry to Application Insights through Azure Monitor.
- Define low-cardinality business metrics for chat, shadow evaluation, quality gates, routing, promotion, and rollback.
- Correlate API and worker operations without exposing prompt content or personal data.
- Add SignalR hubs and events for metrics, evaluations, experiments, and releases.
- Add telemetry and SignalR integration tests.

## Acceptance Criteria

- A chat can be traced through production invocation, shadow execution, evaluation, and aggregation.
- Sensitive prompt or credential values are excluded from telemetry.
- Dashboard clients receive typed update events.
- Exporter failures do not falsely report successful business operations.

## Validation

```powershell
dotnet test GenAIOps.slnx --filter "Telemetry|SignalR|Observability"
```

