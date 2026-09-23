# Task 02 Progress Details

## 2026-09-23

- Added v1, v2, and intentionally poor v3 prompt content and immutable metadata with the exact
  expected metrics from the specification.
- Added Draft 2020-12 JSON schemas and three representative support evaluation fixtures.
- Added a deterministic, credential-free validator in `GenAIOps.Domain` and exposed it through
  `dotnet run --project src\Api -- validate-prompts`.
- Added prompt-focused tests for the valid repository, exact metrics, required v3 behaviors,
  missing content, duplicate versions, out-of-range metrics, malformed fixtures, and invalid JSON.
- Validation results are recorded in the Task 02 Evidence section. No deviations or blockers.
