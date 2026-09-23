# Task 02: Prompt Versions and Evaluation Fixtures

**Status:** complete
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

## Execution Research

- The specification defines three immutable prompt versions and exact expected percentages:
  v1 = 82/89/84, v2 = 94/97/95, and v3 = 72/75/58 for task adherence,
  groundedness, and tool accuracy respectively.
- The required poor v3 behaviors are: use internal reasoning first, avoid tools whenever
  possible, do not ask clarifying questions, and provide a best guess when uncertain.
- Prompt artifacts live in the repository-root `Prompts/v1`, `Prompts/v2`, and
  `Prompts/v3` directories. Each version contains prompt content and metadata; JSON schemas
  and representative support fixtures live under root-level `Schemas` and `EvaluationData`.
- Validation belongs in the dependency-free Domain project so both the API command and unit tests
  use the same deterministic implementation without credentials or network access. The API
  already references the solution layers, and the unit test project already references Domain.
- Validation covers directory/version consistency, unique versions, non-empty prompt content,
  expected metric ranges and exact values, metadata shape, fixture structure, and references from
  fixtures to known prompt versions. The command discovers the repository root from the current
  directory and prints stable, sorted diagnostics.
- Decomposition verdict: atomic. The artifacts, schemas, validator command, and tests form one
  coherent contract and must be validated together. No scenario skill root or breakdown-hint files
  were provided; the Execution extension lookup returned no applicable guidance.

## Evidence

- TDD red phase: the initial filtered test failed because the prompt validation types did not exist.
- `dotnet test GenAIOps.slnx --filter Prompt`: passed 11 prompt tests.
- `dotnet run --project src\Api -- validate-prompts`: passed for 3 prompt versions and 3 fixtures.
- `dotnet build GenAIOps.slnx --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test GenAIOps.slnx --no-build`: passed 14 tests (13 unit and 1 integration).
- `dotnet format GenAIOps.slnx --no-restore --verify-no-changes`: passed.
