"""Black-box API checks. Requires Python 3.10+; no third-party packages."""
import concurrent.futures
import json
import os
import urllib.error
import urllib.request
import uuid

BASE = os.getenv("API_URL", "http://localhost:8080").rstrip("/")

def call(method, path, body=None, student=None):
    data = None if body is None else json.dumps(body).encode()
    headers = {"Content-Type": "application/json"}
    if student:
        headers["X-Student-Id"] = student
    req = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            raw = response.read()
            return response.status, json.loads(raw) if raw else None
    except urllib.error.HTTPError as e:
        raw = e.read()
        try: payload = json.loads(raw) if raw else None
        except Exception: payload = raw.decode(errors="replace")
        return e.code, payload

def create_class(seats):
    status, result = call("POST", "/classes", {
        "name": f"Automated Test {uuid.uuid4().hex[:8]}",
        "totalSeats": seats
    })
    assert status == 201, (status, result)
    return result["id"]

def test_create_and_list():
    class_id = create_class(3)
    status, rows = call("GET", "/classes")
    assert status == 200
    row = next(x for x in rows if x["id"] == class_id)
    assert row["totalSeats"] == 3 and row["availableSeats"] == 3
    print("PASS: create class and list seat counts")

def test_confirm_is_idempotent():
    class_id = create_class(1)
    student = "20152691234567890"
    status, hold = call("POST", f"/classes/{class_id}/holds", student=student)
    assert status in (200, 201), (status, hold)
    hid = hold["holdId"]
    assert call("POST", f"/holds/{hid}/confirm", student=student)[0] == 200
    assert call("POST", f"/holds/{hid}/confirm", student=student)[0] == 200
    _, detail = call("GET", f"/classes/{class_id}")
    assert detail["confirmedSeats"] == 1
    assert detail["availableSeats"] == 0
    print("PASS: confirming twice consumes exactly one seat")

def test_concurrent_requests_never_overbook():
    class_id = create_class(5)
    def apply(i):
        student = f"{20000000000000000 + i:017d}"
        return call("POST", f"/classes/{class_id}/holds", student=student)[0]
    with concurrent.futures.ThreadPoolExecutor(max_workers=30) as pool:
        statuses = list(pool.map(apply, range(30)))
    successes = sum(s in (200, 201) for s in statuses)
    conflicts = sum(s == 409 for s in statuses)
    assert successes == 5, (successes, statuses)
    assert conflicts == 25, (conflicts, statuses)
    _, detail = call("GET", f"/classes/{class_id}")
    assert detail["heldSeats"] + detail["confirmedSeats"] <= detail["totalSeats"], detail
    print("PASS: 30 simultaneous applications for 5 seats never overbook")

if __name__ == "__main__":
    test_create_and_list()
    test_confirm_is_idempotent()
    test_concurrent_requests_never_overbook()
    print("All automated API checks passed.")
