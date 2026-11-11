# Assumptions

- Each grade and shift combination is a separate class record, with its own name and seat capacity (for example, `Class 6 – Morning Shift`). This matches the API, which models shifts as classes.
- The brief asks school admins to create classes but does not define an admin identity flow. Admin registration is therefore gated by the `ADMIN_REGISTRATION_KEY` invite configured in Docker, while username/password login creates a 12-hour HttpOnly session. This keeps the registration screen usable without allowing unrestricted visitor sign-up.
- Admin passwords are salted and hashed with PBKDF2-SHA256; the database stores only hashes and hashed session tokens. The local default invite key is for assignment review only and should be replaced in `.env` before sharing the running app.
- The student registration number is exactly 17 ASCII digits. Login is out of scope; the UI stores the number locally and sends it as `X-Student-Id`.
- A student name is collected for display and sent as the optional `X-Student-Name` header. The registration number remains the application identity.
- New seat holds last `HOLD_DURATION_SECONDS` (600 seconds by default). PostgreSQL time determines expiry; reads and capacity checks free elapsed holds immediately, and a worker persists the `EXPIRED` status.
- The brief says the “Pay admission fee” action should assume payment succeeded. Confirmation therefore simulates successful payment and does not call an external gateway.
- Confirmed applications permanently consume a seat. Cancellation applies only to a live hold; expired and cancelled applications remain in the student's history.
- A student can have at most one active or confirmed application per class. PostgreSQL class-row locks serialize allocation, confirmation and cancellation across API processes sharing the database.
- “First come, first served” means requests are admitted in the order they acquire the class row lock. The row lock protects the seat limit when multiple API processes use the same PostgreSQL database; the automated concurrency test exercises concurrent requests against one API process.
- The browser stores the student's registration number and optional name in local storage so the student can return to their applications after refresh. This is an identifier, not authentication.
- Admission fee is an optional extension stored in minor currency units; it does not replace the required class name, seat count or API response fields.
