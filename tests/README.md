# Automated API checks

Start the stack first:

```bash
docker compose up --build -d
```

Then run from the repository root:

```bash
python tests/test_api.py
```

Optional API URL:

```bash
API_URL=http://localhost:8080 python tests/test_api.py
```

The script uses Python standard-library modules only. It covers:
1. Class creation and seat-count listing.
2. Idempotent confirmation (a repeated confirmation must not consume another seat).
3. A concurrency test: 30 parallel requests compete for 5 seats; exactly 5 must succeed and 25 must receive HTTP 409.

The concurrency test creates its own uniquely named class each run.
