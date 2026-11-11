# Assumptions

- Each grade and shift combination is a separate class record, with its own name and seat capacity (for example, `Class 6 – Morning Shift`). This matches the API, which models shifts as classes.
- The student registration number is exactly 17 ASCII digits. Login is out of scope; the UI stores the number locally and sends it as `X-Student-Id`.
- A student name is collected for display and sent as the optional `X-Student-Name` header. The registration number remains the application identity.
- New seat holds last `HOLD_DURATION_SECONDS` (600 seconds by default). PostgreSQL time determines expiry; reads and capacity checks free elapsed holds immediately, and a worker persists the `EXPIRED` status.
- The brief says the “Pay admission fee” action should assume payment succeeded. Confirmation therefore simulates successful payment and does not call an external gateway.
- Confirmed applications permanently consume a seat. Cancellation applies only to a live hold; expired and cancelled applications remain in the student's history.
- A student can have at most one active or confirmed application per class. PostgreSQL class-row locks serialize allocation, confirmation and cancellation across API processes sharing the database.
- The browser stores the student's registration number and optional name in local storage so the student can return to their applications after refresh. This is an identifier, not authentication.
- Admission fee is an optional extension stored in minor currency units; it does not replace the required class name, seat count or API response fields.
