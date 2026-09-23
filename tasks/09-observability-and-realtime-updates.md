# Task 09: Observability and Real-Time Updates

**Status:** blocked
**Depends on:** Tasks 09A and 09B
**Integration commit subject:** `feat: integrate observability and realtime updates`

## Goal

Verify end-to-end traces, metrics, logs, and live dashboard notifications across API and workers after the two atomic subtasks complete.

## Scope

- Complete Task 09A for OpenTelemetry instrumentation and Azure Monitor export.
- Complete Task 09B for typed SignalR events and workflow publication.
- Run the combined telemetry and SignalR integration suite.
- Verify package-source restrictions and all repository validation.

## Acceptance Criteria

- Task 09A and Task 09B are complete.
- A chat can be traced through production invocation, shadow execution, evaluation, and aggregation.
- Sensitive prompt or credential values are excluded from telemetry.
- Dashboard clients receive typed update events.
- Exporter failures do not falsely report successful business operations.

## Validation

```powershell
dotnet test GenAIOps.slnx --filter "Telemetry|SignalR|Observability"
```

## Execution Research

- Tasks 04-08 expose provider-independent application seams for chat, shadow evaluation, metric
  aggregation, release workflows, and experiment routing. Instrumentation can live at those seams
  using `System.Diagnostics.ActivitySource` and `Meter`, avoiding provider SDK types and preventing
  telemetry/export failures from changing persisted business outcomes.
- Repository package versions are centrally managed. The required Microsoft package proxy offers
  OpenTelemetry hosting and ASP.NET Core/HTTP instrumentation 1.18.0, Azure Monitor OpenTelemetry
  Exporter 1.9.0, and SignalR Client 10.0.12. Server-side SignalR is in the ASP.NET Core shared
  framework. No NuGet.org or npmjs source is needed.
- A shared Infrastructure registration will configure resource/service identity, ASP.NET Core and
  `HttpClient` instrumentation, the application activity source/meter, worker activities, and Azure
  SDK activity sources. Azure Monitor export is off by default and enabled only by configuration;
  enabling it without a connection string fails startup explicitly rather than silently dropping
  telemetry.
- Application telemetry will use bounded dimensions only: operation, outcome, prompt version,
  lifecycle/disposition, route type, and gate result. Prompt/user content, correlation header
  values, provider payloads, credentials, actor/idempotency/session/assignment keys, registry IDs,
  agent IDs, and other PII/high-cardinality values will never be tags or log message values.
- W3C trace context is captured when shadow work is published and restored by the evaluator, linking
  API chat, provider invocation, shadow work, evaluation, and subsequent aggregation activities
  without using user-controlled correlation values as telemetry attributes.
- Business instruments cover chat requests/latency, routing assignments, shadow dispositions and
  latency, evaluation outcomes, metric snapshots/samples, quality-gate results, and
  promotion/rollback outcomes. Counters are emitted only after the corresponding outcome is known;
  failures and rejections have explicit non-success values.
- Application-level realtime event contracts will define typed metrics, evaluation, experiment, and
  release updates. The API implements them through a typed SignalR hub and publisher; workflows and
  processors publish only after successful persistence/state transition. Worker hosts use the same
  contracts with a no-op publisher when no hub is hosted.
- Tests will use `ActivityListener`/`MeterListener` to prove correlation, bounded tag allowlists,
  sensitive-value exclusion, and accurate failure outcomes. SignalR integration tests will connect
  through the real hub and prove typed delivery for representative workflow events.
- Runtime probes will negotiate/connect to the local SignalR hub, trigger representative events,
  and inspect local telemetry listeners without configuring an external Azure resource.
- Affected units span Application instrumentation and event contracts, Infrastructure OpenTelemetry
  registration and worker composition, API SignalR hosting, workflow/processor hooks, package
  manifests, and unit/integration tests. Frontend source is unchanged but its checks remain required.
- Decomposition assessment trigger: this task crosses independent observability and realtime
  concerns across the API and four worker hosts, so it must be presented to TaskBreaker before the
  first source edit. No scenario skill root, Execution-stage file, or Breakdown Hints were supplied.
  Skill discovery found no matching skill; the Execution extension lookup returned no guidance.

## Decomposition

- [Task 09A](09a-opentelemetry-observability.md) owns tracing, metrics, logging, and Azure Monitor export.
- [Task 09B](09b-signalr-realtime-updates.md) owns typed SignalR contracts, publication, and delivery tests.
