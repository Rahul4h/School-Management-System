# Assumptions

- Each shift is represented as its own named class, as required by the brief.
- The student registration ID is exactly 17 ASCII digits. It is an identity label only; authentication is out of scope because the assignment explicitly says login is not required.
- New holds last `HOLD_DURATION_SECONDS` (default 600). Expiry is based on PostgreSQL UTC time; a background worker persists `EXPIRED`, while capacity/read queries treat expired rows as free immediately.
- Confirmed applications permanently consume a seat. Cancellation is only available for an unexpired `HELD` application; a confirmed admission is not reversible through this API.
- A student may have at most one `HELD` or `CONFIRMED` application per class. Expired/cancelled application history remains visible.
- “Payment” is simulated by the confirm endpoint. A repeated confirmation by the owner returns the same confirmed outcome.
- The browser stores the current registration number in local storage to restore the student's view after refresh/reopen. This is not an authentication mechanism.
- PostgreSQL row-level locking on a class row serializes competing holds across API processes. This is the consistency mechanism for the optional multi-process case.
- UI polls every five seconds for class/application data and updates countdowns locally every second. Expiry status and seat release are also enforced by the backend.
- Admin and parent actions share the same unauthenticated UI because no role/login workflow or admin authorization was specified.
