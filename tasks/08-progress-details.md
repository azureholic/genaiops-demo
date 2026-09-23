# Task 08 Progress Details

## 2026-09-23

- Added provider-independent experiment configuration, percentage allocation, lifecycle, assignment,
  workflow, and routing contracts with explicit start/update/end semantics.
- Added `POST /api/abtest`, ETag-protected updates and completion, exact allocation validation,
  approved-production eligibility, deterministic sticky routing, and restoration of normal routing
  after completion. Completed experiments can be replaced safely by a new uniquely identified run.
- Added assigned experiment/version propagation to chat responses, safe chat metadata, shadow work,
  shadow evaluations, and continuous evaluation records. Assignment keys and reusable key hashes are
  not persisted.
- Added unit coverage for 20,000-key 90/10 distribution tolerance, stickiness, allocation errors,
  candidate/unknown eligibility, key validation, stale concurrency, lifecycle, restart, and normal
  routing. Added API coverage for start/update/end, typed errors, metadata safety, and chat routing.
- Validation: fresh restore from repository `NuGet.config` passed; build passed with 0 errors and
  0 warnings; filtered tests passed 11/11; full tests passed 102/102; format verification passed;
  frontend build, test (1/1), and lint passed.
- Runtime probe: promoted v2, started v2 90 / v1 10 with HTTP 201, observed both v2 and v1 cohorts,
  ended the experiment, and verified keyless chat returned to normal v2 production routing.
- Decomposition was assessed as atomic before source edits. No scenario skill root, Execution-stage
  file, Breakdown Hints, or task-related skills were supplied, and instruction discovery was not
  available in this dispatch.
