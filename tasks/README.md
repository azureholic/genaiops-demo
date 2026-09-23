# GenAIOps Demo Implementation Tasks

These files break the specification into commit-sized implementation tasks. Complete tasks in numeric order unless a task explicitly allows parallel work. Do not combine tasks into one commit.

## Working Agreement

1. Read the task file and all dependency task files before making changes.
2. Implement only the scope listed in the current task.
3. Add or update the tests required by the task.
4. Run every validation command listed in the task.
5. Update the task status and evidence.
6. Commit with the exact commit subject from the task.
7. Stop after the commit unless the user explicitly authorizes the next task.

## Status Values

- `complete`: implementation and validation are committed.
- `ready`: all dependencies are complete.
- `blocked`: at least one dependency is incomplete or an external decision is required.

## Task Order

| Task | Title | Depends on | Status |
|------|-------|------------|--------|
| 00 | Initial repository setup | None | complete |
| 01 | Solution architecture and project scaffold | 00 | complete |
| 02 | Prompt versions and evaluation fixtures | 01 | complete |
| 03 | Cosmos DB persistence and agent registry | 01 | complete |
| 04 | Foundry agent gateway and chat API | 02, 03 | complete |
| 05 | Shadow testing and evaluation worker | 04 | complete |
| 06 | Metrics aggregation and continuous evaluation | 05 | complete |
| 07 | Promotion and rollback workflows | 03, 06 | complete |
| 08 | A/B routing and experiment API | 04, 07 | complete |
| 09A | OpenTelemetry observability | 04, 05, 06, 07, 08 | complete |
| 09B | SignalR real-time updates | 09A | complete |
| 09 | Observability and real-time updates integration gate | 09A, 09B | complete |
| 10 | Web application shell and home dashboard | 01, 06, 09 | complete |
| 11 | Prompt registry and release history dashboards | 07, 10 | complete |
| 12 | Shadow testing and evaluation dashboards | 05, 06, 10 | complete |
| 13 | Azure infrastructure and container deployment | 04, 05, 06, 07, 08, 09 | complete |
| 14 | GitHub Actions CI/CD workflows | 02, 13 | ready |
| 15 | End-to-end demo validation and documentation | 11, 12, 14 | blocked |

## Specification Coverage

| Specification area | Tasks |
|--------------------|-------|
| Prompt versioning | 02, 03, 11 |
| Agent registry | 03, 11 |
| Chat API | 04 |
| Shadow testing | 05, 12 |
| Continuous evaluation | 05, 06, 12 |
| Promotion workflow | 07, 11 |
| Rollback workflow | 07, 11 |
| A/B testing | 08 |
| OpenTelemetry observability | 09A, 09 |
| SignalR real-time updates | 09B, 09 |
| React dashboards | 10, 11, 12 |
| Azure infrastructure | 13 |
| GitHub Actions | 14 |
| Fifteen-minute demonstration | 15 |
