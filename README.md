# School Admission System

A .NET 8 + PostgreSQL + React 18 take-home assignment implementation.

## Run with Docker

```bash
docker compose up --build
```

- Frontend: http://localhost:3000
- Backend API: http://localhost:8080
- Swagger: http://localhost:8080/swagger
- Health: http://localhost:8080/health

No local .NET, Node.js, or PostgreSQL installation is required when using Docker.

## Configuration

Copy `.env.example` to `.env` and adjust:

```env
HOLD_DURATION_SECONDS=600
VITE_API_URL=http://localhost:8080
```

`VITE_API_URL` is a Vite build-time variable. Rebuild the frontend after changing it.

## Try the system

1. Open the frontend and choose **School admin**.
2. Create `Class 6 - Morning Shift` with 2 seats.
3. Switch to **Parent**.
4. Enter a 17-digit student birth registration number.
5. Apply for the class; the hold lasts 600 seconds by default.
6. Click **Pay admission fee** to simulate successful payment.
7. Use another student ID to verify that the confirmed seat is no longer available.

## API examples

Create class:

```bash
curl -X POST http://localhost:8080/classes \
  -H "Content-Type: application/json" \
  -d '{"name":"Class 6 - Morning Shift","totalSeats":120}'
```

List classes:

```bash
curl http://localhost:8080/classes
```

Create hold (replace `c_ID` with the returned ID):

```bash
curl -X POST http://localhost:8080/classes/c_ID/holds \
  -H "X-Student-Id: 20152691234567890"
```

Confirm hold:

```bash
curl -X POST http://localhost:8080/holds/h_ID/confirm \
  -H "X-Student-Id: 20152691234567890"
```

## Important implementation notes

- The class database row is locked with `SELECT ... FOR UPDATE` inside a transaction during allocation.
- Held and confirmed counts are checked inside the same transaction.
- Hold expiry is configurable and expired holds release seats.
- Confirm is idempotent: confirming an already confirmed hold does not count it twice.
- PostgreSQL uses a named Docker volume so data survives container restarts.

## Before submitting

- Run all manual checks in the assignment.
- Add automated tests, especially a concurrency test that fires more requests than the available seats.
- Inspect the generated SQL/schema and test against the exact PostgreSQL image used by Docker.
- Replace `EnsureCreated` with EF Core migrations if you have time.
- Push the source and commit history to your own public GitHub repository.
- Read `ASSUMPTIONS.md` and `AI_USAGE.md`; update them honestly to reflect your final implementation and AI usage.

## Limitations to address before production

This is an assignment starter, not a production payment or identity system. Admin authentication, real payment integration, rate limiting, audit logging, observability, and full automated test coverage are not included.
