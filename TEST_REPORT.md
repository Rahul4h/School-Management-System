# Backend test report

**Result:** Passed — 13 tests, 0 failures

**Executed:** 2026-11-11 (UTC)

**Command:** `docker compose --profile test run --build --rm backend-tests`

**Evidence:** `artifacts/test-results/admission-tests.trx`

The Docker integration suite covers class capacity, duplicate applications, registration ID validation, malformed JSON and invalid class input, administrator registration/login/logout and authorization, capacity safeguards, admitted-student roster access, confirmation ownership and idempotency, cancellation, and concurrent seat allocation.

The concurrency test sends 30 application requests together to a class with 5 seats. Exactly 5 holds are created; the remaining 25 receive `409 Conflict`; the class is never oversubscribed.

Run the suite from the repository root:

```sh
docker compose --profile test run --build --rm backend-tests
```

The command writes a TRX report to `artifacts/test-results/admission-tests.trx`. GitHub Actions runs the same command on pushes and pull requests and uploads the report as the `backend-test-report` artifact. Test source: `backend/tests/Admission.Tests/ApiTests.cs`.
