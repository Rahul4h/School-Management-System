# School Admission System

A first-come-first-served school admission portal. Each class/shift has its own seat capacity. The project uses React, .NET 8, PostgreSQL, and Docker Compose.

## 1. Start the app

Install and start Docker Desktop (Docker Engine with Compose also works). In a terminal, move to the repository folder and run:

```sh
docker compose up --build
```

The first run downloads packages and builds the images, so it can take a few minutes. When the services are ready, open:

- **Student portal and admin panel:** http://localhost:5173
- **Backend health check:** http://localhost:8080/health

The health check should return `{"status":"healthy"}`. PostgreSQL data is saved in a Docker volume and survives `docker compose down` followed by `docker compose up`.

To stop the services, press `Ctrl+C` in the terminal, or run `docker compose down` from another terminal in the repository folder. This keeps saved data. **`docker compose down -v` permanently deletes the local database and all admissions.**

## 2. Use the student portal

There is no student login. The 17-digit registration number identifies the student.

1. Open http://localhost:5173. At the top of the Admissions page, enter the student's full name and 17-digit registration number, then select **Save**.
2. If there are no classes yet, create one from the admin panel (see below), then return to **Admissions**.
3. Choose a class/shift and select **Apply**. The system holds one seat for the configured duration (10 minutes by default).
4. Under **My applications**, select **Pay admission fee** before the timer expires. This simulates a successful payment; no real payment gateway is connected.
5. The application status and seat counts update automatically. The student's name and registration number are saved in that browser, so they remain available after a refresh or reopening the browser.

For a quick local example, use the name `Test Student` and registration number `20152691234567890` (17 digits).

## 3. Register and use the admin panel

1. Select **School admin** in the top navigation.
2. Select **Register administrator** and enter a username, a password of at least 12 characters, and the registration key.
3. With the default Docker setup, the key is **`local-school-admin-key`**. The same default is visible in `docker-compose.yml`, so use a private key before sharing a deployed instance.
4. After registration, the panel signs you in. Later, select **Sign in** and use the username and password you created. Admin sessions expire after 12 hours.
5. Use **Create a class and shift** to add a class, for example `Class 6 - Morning Shift`, and set its seat capacity. Each shift is a separate class.
6. In **Classes and admissions**, change a class's capacity or select **View admitted students** to see the names and registration numbers of confirmed students. Capacity cannot be reduced below seats already held or confirmed.

### Set a private admin registration key (optional)

Create a `.env` file in the repository root before starting Docker. Compose reads it automatically. For example:

```env
ADMIN_REGISTRATION_KEY=put-a-private-random-key-here
```

Then start or rebuild the app with `docker compose up --build`. Give this key directly to the person who should register an admin; Docker does not send or generate it for them. `.env` is ignored by Git. Do not commit your private key.

## 4. Run the automated tests

Run this from the repository root:

```sh
docker compose --profile test run --build --rm backend-tests
```

Docker starts PostgreSQL and the test runner. The command prints each test result and a final passed/failed count. The suite contains 13 backend integration tests, including a concurrency test that sends 30 applications together for 5 seats and checks that no more than 5 seats are held.

Test results are saved to:

- **Quick summary:** [TEST_REPORT.md](TEST_REPORT.md)
- **Detailed test report:** `artifacts/test-results/admission-tests.trx` (open it in Visual Studio or another TRX viewer)
- **Test source:** `backend/tests/Admission.Tests/ApiTests.cs`

The integration tests use the Compose PostgreSQL database and create test records. To keep the student class list tidy for a demo, run the tests before creating your demo classes and applications. `docker compose down -v` removes both test and app data, so only use it when you are comfortable deleting all local admissions.

To inspect records created by the tests in the app, open **School admin**, register or sign in, and look through **Classes and admissions**. Open **View admitted students** on a class to see its confirmed test students and registration numbers.

## Configuration

Compose works with its defaults. To override settings, add them to the root `.env` file:

| Variable | Default | Purpose |
| --- | --- | --- |
| `ADMIN_REGISTRATION_KEY` | `local-school-admin-key` | Key required to register an administrator |
| `HOLD_DURATION_SECONDS` | `600` | How long a seat remains held |
| `POSTGRES_PASSWORD` | `admission_dev_only` | Local PostgreSQL password |
| `DATABASE_URL` | Compose PostgreSQL connection | Backend database connection string |
| `FRONTEND_ORIGIN` | `http://localhost:5173` | Allowed frontend origin for API requests |
| `VITE_API_BASE_URL` | `http://localhost:8080` | Backend URL compiled into the frontend; rebuild after changing |
| `ADMIN_COOKIE_SECURE` | `false` | Set `true` when serving over HTTPS |

## Project layout

```text
backend/       .NET API, PostgreSQL access, and integration tests
frontend/      React student portal and school admin panel
docker-compose.yml
ASSUMPTIONS.md
AI_USAGE.md
TEST_REPORT.md
```
