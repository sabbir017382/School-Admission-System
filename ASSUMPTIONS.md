# Assumptions and design decisions

1. **Student identity:** `X-Student-Id` is the student's 17-digit birth registration number, based on the assignment example. The backend validates exactly 17 digits.
2. **One active application:** A student may have at most one active (`HELD`) application for a given class/shift. A cancelled or expired application does not block a new attempt.
3. **Hold duration:** `HOLD_DURATION_SECONDS` controls the reservation window and defaults to 600 seconds (10 minutes), as specified.
4. **Payment:** The assignment explicitly says no real payment gateway is required. Clicking “Pay admission fee” simulates successful payment and confirms the hold.
5. **FIFO:** The database transaction commits the successful hold allocation. Requests are serialized by locking the class row. The first transaction to acquire the lock and successfully allocate a seat wins; this is practical first-come-first-served ordering at the server/database boundary.
6. **Expired holds:** Expiry is checked when class counts, a student's applications, or a hold are read and before a new hold is created. Expired holds are persisted as `EXPIRED`.
7. **Admin authentication:** No login/authentication mechanism is included because the brief does not specify one. The admin UI is therefore a demonstration interface, not production-secure.
8. **Class names:** Class/shift names are unique to avoid accidental duplicate entries.
9. **Persistence:** PostgreSQL data is stored in the named Docker volume `postgres_data`.
10. **ORM setup:** `EnsureCreated` is used for easy evaluation startup. For a production deployment, use versioned EF Core migrations instead.
11. **CORS:** The frontend origin is configurable through `Frontend__Origin`.
12. **API base URL:** The React build uses `VITE_API_URL`; change it at build time for another frontend deployment.
