# AI usage

OpenAI Codex assisted with the .NET API and PostgreSQL transactions, React admissions and administrator screens, Docker configuration, tests, and documentation.

During review, Codex found that earlier documentation did not match the checked-in test evidence. It corrected the report and added regression tests for invalid requests, hold ownership and idempotency, cancellation, and the administrator workflow. It also implemented invite-key registration and password login for administrators, protected class creation and capacity changes, and a confirmed-student registration list.

Codex initially misdiagnosed a Docker CLI permission restriction as an engine problem. An elevated Docker check showed the engine was running; the app was then built and started successfully. A separate first image build could not reach NuGet from the restricted shell; rerunning with approved network access completed the build. The frontend production build and Docker app health checks succeeded, and the backend integration suite passed all 13 tests (including the concurrent allocation test) on 2026-11-11. The concurrency result and admin flows were checked by automated tests; API and UI source were also reviewed against the assignment requirements.
