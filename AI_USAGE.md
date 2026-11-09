# AI usage

OpenAI Codex was used to scaffold the .NET API, PostgreSQL transaction flow, React interface, Docker setup, test cases, and project documentation. AI-generated code is treated as a draft: the API contract and invariants were checked against the assignment, especially transaction locking, seat counts, expiry, ownership checks, and frontend polling. The environment did not provide a local .NET SDK, so the project is intended to build and run through the supplied Docker images; container builds/tests should be run before submission.
