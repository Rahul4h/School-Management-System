# Backend test report

**Result:** Passed - 4 tests, 0 failures  
**Executed:** 2026-11-10 (UTC)  
**Command:** `docker compose --profile test run --build --rm backend-tests`

| Test | Result |
| --- | --- |
| `NewClassStartsWithAllSeatsAvailable` | Passed |
| `OneStudentCannotHoldTwoSeatsInOneClass` | Passed |
| `NonAsciiRegistrationDigitsAreRejected` | Passed |
| `ConcurrentApplicationsNeverHoldMoreThanTheSeatCapacity` | Passed |

The concurrency test issued 30 application requests together for a class with 5 seats. Exactly 5 requests created holds, the other 25 received `409 Conflict`, and the final available-seat count was 0.

The machine-readable TRX report is generated at `artifacts/test-results/admission-tests.trx` when the Docker test command runs. GitHub Actions also uploads it as the `backend-test-report` artifact for each workflow run.