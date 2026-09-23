# Task 03: Cosmos DB Persistence and Agent Registry

**Status:** blocked  
**Depends on:** Task 01  
**Commit subject:** `feat: add Cosmos persistence and agent registry`

## Goal

Persist prompt versions, deployments, evaluations, metrics, experiments, and release history in Azure Cosmos DB behind testable interfaces.

## Scope

- Define domain records and lifecycle states for agents, prompt versions, deployments, evaluations, metric snapshots, experiments, and releases.
- Define container names, partition keys, indexing requirements, and optimistic concurrency behavior.
- Implement Cosmos repositories and local/in-memory test doubles.
- Implement an agent registry service with production and candidate assignments.
- Expose `GET /api/versions`.
- Add repository, serialization, concurrency, and endpoint tests.

## Acceptance Criteria

- The registry prevents multiple production assignments.
- Candidate and production history is retained.
- Cosmos-specific types do not leak into domain or API contracts.
- `GET /api/versions` returns current assignments and version metadata.
- Tests cover not-found, conflict, and concurrent update cases.

## Validation

```powershell
dotnet test GenAIOps.slnx --filter "Registry|Persistence|Versions"
```

