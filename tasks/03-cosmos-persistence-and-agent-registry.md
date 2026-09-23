# Task 03: Cosmos DB Persistence and Agent Registry

**Status:** complete  
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

## Execution Research

- Task 01 established .NET 10 projects with Domain independent, Application referencing Domain,
  Infrastructure referencing both, and the API composing Application and Infrastructure. Task 02
  added immutable prompt artifacts but no persistence contracts or implementations.
- The specification requires prompt versioning, an agent registry, candidate registration,
  production marking, release history, and `GET /api/versions`; later promotion, rollback, metrics,
  evaluation, and A/B tasks depend on the persistence foundations defined here but their workflows
  remain out of scope.
- Domain records will cover agents, prompt versions, deployments, evaluations, metric snapshots,
  experiments, and releases. Every persisted record has an explicit schema version and type
  discriminator, lifecycle enums serialize as strings, and infrastructure types remain outside
  Domain and API contracts.
- Application contracts use asynchronous repository APIs, opaque continuation tokens, bounded
  page sizes, explicit not-found/conflict exceptions, and ETag values represented as strings.
  Agent registry operations retain assignment history and use optimistic concurrency to prevent
  two production assignments.
- Cosmos implementation uses `Microsoft.Azure.Cosmos` 3.63.1, a singleton `CosmosClient`,
  parameterized `QueryDefinition` queries, continuation-token pagination, query-aligned
  high-cardinality `/partitionKey` values, explicit container/index definitions, request-charge
  diagnostics, and `IfMatchEtag` writes.
- Local validation uses in-memory repositories and `WebApplicationFactory`; no Azure account or
  Cosmos emulator is required. Cosmos mapping and container-definition tests verify the production
  adapter without network access.
- Affected units are Domain, Application, Infrastructure, API, UnitTests, and IntegrationTests.
  Workers and the web application are not modified.
- Decomposition verdict: atomic. This is one persistence-and-registry vertical slice whose contracts,
  adapters, composition, endpoint, and concurrency tests must agree; splitting it would leave
  untestable intermediate contracts. No scenario skill root, Execution-stage file, or Breakdown
  Hints files were provided.

## Evidence

- `dotnet restore GenAIOps.slnx`: passed.
- `dotnet build GenAIOps.slnx --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test GenAIOps.slnx --filter "Registry|Persistence|Versions"`: passed
  16 tests (13 unit and 3 integration).
- `dotnet test GenAIOps.slnx --no-build`: passed 29 tests (25 unit and 4 integration).
- `dotnet format GenAIOps.slnx --no-restore --verify-no-changes`: passed.
