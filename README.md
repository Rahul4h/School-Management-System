# School Admission System

First-come-first-served school admission portal built with React 18, .NET 8 Minimal API, PostgreSQL, and Nginx. Each class/shift has its own seat capacity. PostgreSQL row locks protect the capacity invariant across multiple API processes.

## Run with Docker

Docker Engine/Desktop with Compose is the only host requirement. From the repository root:

```sh
docker compose up
```

Open the UI at **http://localhost:5173**. The API is available at **http://localhost:8080** (`/health`). Nginx serves the frontend and can proxy `/api/*` to the backend. PostgreSQL data persists in the `postgres_data` volume. `docker compose down` keeps the data; `docker compose down -v` deletes it.

Compose builds the images automatically on first start; use `docker compose up --build` after changing source files. The default database password is for local assignment review only. Optional `.env` settings:

- `DATABASE_URL`: backend PostgreSQL connection string.
- `POSTGRES_PASSWORD`: Compose database password, used when `DATABASE_URL` is not overridden.
- `HOLD_DURATION_SECONDS`: hold period, default `600`.
- `FRONTEND_ORIGIN`: allowed browser origin, default `http://localhost:5173`.
- `VITE_API_BASE_URL`: API URL embedded in the frontend build, default `http://localhost:8080`. Rebuild after changing it.

## Admission flow

1. Enter a 17-digit student registration number and name, then apply to a class.
2. The API atomically reserves one seat for `HOLD_DURATION_SECONDS`. Other applicants cannot take that seat during the hold.
3. Click **Pay admission fee** before the countdown expires. As specified in the assignment, this action simulates successful payment and confirms the admission; no real payment gateway is used.
4. If the hold expires first, the seat is released automatically and the UI shows the expired state without a page reload.

The student's name is stored with the hold for display. The registration number remains the API identity header `X-Student-Id`; login is out of scope per the assignment.

## Tests

Backend integration tests include a concurrent seat-capacity test and require Docker for PostgreSQL:

```sh
docker compose --profile test build backend-tests
docker compose --profile test run --rm backend-tests
```

For a local frontend production build: `cd frontend && npm ci && npm run build`.

## Design notes

Seat allocation, confirmation, and cancellation lock the class row first, so separate API instances sharing PostgreSQL follow one lock order. Capacity and read queries exclude expired holds immediately; a background worker also persists the `EXPIRED` state. Schema initialization upgrades the earlier sample database without dropping its data.
