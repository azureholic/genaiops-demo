# Task 09B: SignalR Real-Time Updates

**Status:** complete
**Depends on:** Task 09A
**Commit subject:** `feat: add SignalR realtime updates`

## Goal

Deliver typed real-time metrics, evaluation, experiment, and release notifications to dashboard clients.

## Scope

- Define provider-independent typed event contracts in the application layer.
- Host a typed SignalR hub in the API.
- Publish events only after successful persistence or state transitions.
- Provide a no-op publisher for worker hosts that do not host a hub.
- Add integration tests using a real SignalR client connection.
- Add runtime negotiation, connection, and representative event-delivery probes.

## Acceptance Criteria

- Clients receive typed metrics, evaluation, experiment, and release events.
- Failed or rolled-back operations do not emit success-shaped events.
- Worker hosts do not require an API-hosted hub to start.
- Event payloads contain no secrets, prompt content, assignment keys, or PII.

## Validation

```powershell
dotnet test GenAIOps.slnx --filter SignalR
dotnet test GenAIOps.slnx
```

## Evidence

- Added provider-independent typed metrics, evaluation, experiment, and release update contracts
  in the application layer, with payloads limited to operational state and aggregate values.
- Hosted a strongly typed SignalR hub at `/hubs/realtime` and published workflow updates only
  after successful persistence or completed release/experiment state transitions.
- Registered a no-op publisher in every worker so workers start without an API hub.
- Prevented duplicate, rejected, failed, and retrying operations from emitting success-shaped
  updates; release rollback notifications retain the explicit `rolledBack` lifecycle.
- Added real SignalR client integration coverage for negotiate, WebSocket connection, all four
  typed event deliveries, successful release publication, rejection suppression, and sensitive
  contract-field exclusion.
- Validated with fresh proxy-only restore, build, filtered and full tests, format verification,
  and `git diff --check`.
