# Task 04: Foundry Agent Gateway and Chat API

**Status:** blocked  
**Depends on:** Tasks 02 and 03  
**Commit subject:** `feat: add Foundry chat gateway`

## Goal

Send user requests to the registered production Azure AI Foundry Agent while preserving a provider-independent application boundary.

## Scope

- Define the chat request, response, citation, tool-call, usage, and correlation contracts.
- Implement an Azure AI Foundry Agent Service adapter using managed identity.
- Resolve the active production version through the registry.
- Implement `POST /api/chat` with validation, cancellation, correlation IDs, and explicit error mapping.
- Record request metadata needed for later evaluation without storing secrets.
- Add fake-gateway unit tests and API integration tests.

## Acceptance Criteria

- Only the production response is returned to the caller.
- No API key or connection string is required in source control.
- Invalid input, missing production deployment, provider failure, and cancellation produce documented errors.
- Correlation data is propagated for later shadow and telemetry processing.

## Validation

```powershell
dotnet test GenAIOps.slnx --filter "Chat|FoundryGateway"
```

