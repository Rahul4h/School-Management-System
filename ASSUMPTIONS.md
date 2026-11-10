# Assumptions

- Each class/shift pair is represented by a separate class with its own seat capacity, as described in the brief.
- The student registration number is exactly 17 ASCII digits. Login is out of scope; the ID is supplied in `X-Student-Id`. The optional student name is stored for display only.
- New holds last `HOLD_DURATION_SECONDS` (default 600). Expiry uses PostgreSQL time; the worker persists `EXPIRED`, while reads and capacity checks also treat elapsed holds as free immediately.
- The assignment explicitly says the “Pay admission fee” action should assume payment succeeded. The confirm endpoint therefore simulates payment and does not call an external gateway.
- Confirmed applications permanently consume one seat. Cancellation applies only to a live hold; expired/cancelled application history remains visible.
- A student may have at most one active or confirmed application per class. PostgreSQL row locks on the class serialize holds, confirmation and cancellation across API processes sharing the database.
- The browser stores the student's registration number and optional name in local storage to restore their view after refresh. This is an identifier, not authentication.
- Admission fee is configurable per class as an optional extension; the assignment’s required class fields and API response fields remain present.
