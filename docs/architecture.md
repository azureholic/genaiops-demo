# Solution architecture

## Project map

| Area | Project or folder | Responsibility |
| --- | --- | --- |
| Domain | `src/GenAIOps.Domain` | Domain entities, value objects, and domain rules with no external dependencies. |
| Application | `src/GenAIOps.Application` | Use-case contracts and orchestration abstractions; depends only on Domain. |
| Infrastructure | `src/GenAIOps.Infrastructure` | Adapters for data stores, AI services, telemetry, and other external systems. |
| API | `src/Api` | ASP.NET Core Minimal API composition root and HTTP presentation. |
| Workers | `src/Workers/*` | Independent background-process composition roots for evaluation, aggregation, promotion, and rollback. |
| Web | `src/Web` | React and TypeScript browser application. |
| Tests | `tests/*` | Unit-level architecture checks and API integration tests. |

## Dependency direction

Dependencies point inward:

```text
Web ---> API ---> Application ---> Domain
                   ^                ^
                   |                |
Workers ------> Infrastructure -----+
```

- Domain does not reference Application, Infrastructure, host, or presentation projects.
- Application references Domain only.
- Infrastructure implements Application contracts and can reference Domain.
- API and workers are composition roots. They reference Application and Infrastructure, but
  reusable business rules must not be placed in a host.
- Web communicates with the API over HTTP and SignalR and is not coupled to .NET assemblies.

The unit test suite verifies the core assembly dependency rules. Project references are explicit
in each project file, so an outward dependency from Domain or Application requires a reviewed
source change and causes the architecture test to fail once used.

## Engineering conventions

- `global.json` pins the .NET 10 SDK feature band used by the repository.
- `Directory.Build.props` enables nullable analysis, deterministic output, and warnings as errors.
- `Directory.Packages.props` centrally owns NuGet package versions.
- `.editorconfig` defines cross-language whitespace and C# style defaults.
- `package-lock.json` makes frontend installs reproducible through `npm ci`.
- No production cloud adapter or business workflow is part of this scaffold. Later tasks add
  implementations behind Application contracts.
