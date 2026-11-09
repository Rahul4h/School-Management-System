# School Admission System

A school admission portal with first-come-first-served seat holds, confirmation, cancellation, expiry and a responsive React UI. Backend: .NET 8 minimal API + PostgreSQL. Frontend: React 18 + TypeScript + Vite.

## Run with Docker

Docker Engine/Desktop with Compose is the only required host dependency.

```sh
docker compose up --build
```

Open the UI at **http://localhost:5173**. API: **http://localhost:8080** (`/health` for health status). PostgreSQL data persists in the `postgres_data` volume; `docker compose down` keeps it. To intentionally reset data, use `docker compose down -v`.

Optional configuration:

- `HOLD_DURATION_SECONDS=600` sets the hold period.
- `VITE_API_BASE_URL=http://localhost:8080` sets the API origin embedded in the frontend build. Rebuild after changing it.
- `DATABASE_URL` can override the API PostgreSQL connection string.

The UI identifies a student by their 17 digit registration number in `X-Student-Id`. The ID is stored in browser local storage for return visits.

## Tests

The backend test suite contains API integration tests, including concurrent requests against PostgreSQL. It requires Docker because it provisions an isolated PostgreSQL test container.

```sh
docker compose --profile test build backend-tests
docker compose --profile test run --rm backend-tests
```

For a local frontend build, with Node.js installed: `cd frontend && npm install && npm run build`.

## Design notes

Seat allocation is serialized using a PostgreSQL row lock on the target class. Capacity checks and hold insertion run in the same transaction, so separate API processes sharing the database cannot allocate beyond capacity. An expiry worker transitions elapsed holds to `EXPIRED`; read and capacity queries also exclude elapsed holds directly, so seat availability does not depend on the worker's polling interval.

Input validation failures return 4xx. Database availability errors return 503 with a readable message. All timestamps use UTC.

