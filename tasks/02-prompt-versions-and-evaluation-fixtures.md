# Task 02: Prompt Versions and Evaluation Fixtures

**Status:** blocked  
**Depends on:** Task 01  
**Commit subject:** `feat: add versioned prompts and evaluation fixtures`

## Goal

Treat prompt versions and evaluation inputs as validated, testable source artifacts.

## Scope

- Add `Prompts/v1`, `Prompts/v2`, and `Prompts/v3` with immutable metadata and prompt content.
- Encode the expected task-adherence, groundedness, and tool-accuracy values from the specification.
- Make v3 intentionally poor using the behaviors defined by the specification.
- Add representative support scenarios under `EvaluationData`.
- Define JSON schemas for prompt metadata and evaluation fixtures.
- Add a deterministic prompt-validation command and unit tests.

## Acceptance Criteria

- All three prompt versions validate against the schema.
- Version identifiers and expected metric values match the specification.
- Tests fail for missing prompt content, duplicate versions, invalid metric ranges, and malformed fixtures.
- Validation runs without Azure credentials.

## Validation

```powershell
dotnet test GenAIOps.slnx --filter Prompt
dotnet run --project src\Api -- validate-prompts
```

