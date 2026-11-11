# Backend test report

**Result:** Passed — 4 tests, 0 failures

**Executed:** 2026-11-10 (UTC)

**Command:** `docker compose --profile test run --build --rm backend-tests`

**Evidence:** `artifacts/test-results/admission-tests.trx`

| Test | Result |
| --- | --- |
| `NewClassStartsWithAllSeatsAvailable` | Passed |
| `OneStudentCannotHoldTwoSeatsInOneClass` | Passed |
| `NonAsciiRegistrationDigitsAreRejected` | Passed |
| `ConcurrentApplicationsNeverHoldMoreThanTheSeatCapacity` | Passed |

The concurrency test sent 30 application requests together for a class with 5 seats. Exactly 5 created holds; the other 25 received `409 Conflict`; the resulting class had no available seats and no more holds than its capacity.

Run the same test suite in Docker from the repository root:

```sh
docker compose --profile test run --build --rm backend-tests
```

The command writes a TRX report to `artifacts/test-results/admission-tests.trx`. GitHub Actions runs this command on pushes and pull requests and uploads the report as the `backend-test-report` artifact. Test source: `backend/tests/Admission.Tests/ApiTests.cs`.
