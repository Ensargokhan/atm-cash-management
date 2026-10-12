"""Generates the Postman collection and environment. Run: python postman/generate_collection.py"""
import json
import os
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))


def req(name, method, path, body=None, headers=None, tests=None, prerequest=None, description=None):
    raw_path, _, query = path.partition("?")
    item = {
        "name": name,
        "request": {
            "method": method,
            "header": [{"key": "Content-Type", "value": "application/json"}] if body is not None else [],
            "url": {
                "raw": "{{baseUrl}}" + path,
                "host": ["{{baseUrl}}"],
                "path": raw_path.strip("/").split("/"),
            },
        },
        "event": [],
    }
    if query:
        item["request"]["url"]["query"] = [
            {"key": kv.split("=")[0], "value": kv.split("=")[1]} for kv in query.split("&")
        ]
    if description:
        item["request"]["description"] = description
    for key, value in (headers or {}).items():
        item["request"]["header"].append({"key": key, "value": value})
    if body is not None:
        raw = body if isinstance(body, str) else json.dumps(body, indent=2)
        item["request"]["body"] = {"mode": "raw", "raw": raw, "options": {"raw": {"language": "json"}}}
    if prerequest:
        item["event"].append({"listen": "prerequest", "script": {"type": "text/javascript", "exec": prerequest.strip().split("\n")}})
    if tests:
        item["event"].append({"listen": "test", "script": {"type": "text/javascript", "exec": tests.strip().split("\n")}})
    return item


def status(code):
    return f'pm.test("Status is {code}", () => pm.response.to.have.status({code}));\n'


ITEMS = [
    req("00 Health check", "GET", "/health", tests=status(200)),

    req("01 Create ATM", "POST", "/api/atms",
        body='{\n  "code": "{{atmCode}}",\n  "name": "Kadikoy Branch",\n  "location": "Istanbul"\n}',
        prerequest="""
pm.collectionVariables.set("atmCode", "ATM-" + Date.now());
// A fresh card per run keeps the daily limit from blocking repeated runs.
let card = "4"; for (let i = 0; i < 15; i++) card += Math.floor(Math.random() * 10);
pm.collectionVariables.set("cardNumber", card);
""",
        tests=status(201) + """
const atm = pm.response.json();
pm.test("ATM starts UNCONFIGURED", () => pm.expect(atm.status).to.eql("Unconfigured"));
pm.collectionVariables.set("atmId", atm.id);
"""),

    req("02 Configure cassettes", "PUT", "/api/atms/{{atmId}}/cassettes",
        headers={"X-User": "ops.supervisor"},
        body={"cassettes": [
            {"slot": 1, "type": "Recycling", "currency": "TRY", "denomination": 100},
            {"slot": 2, "type": "Recycling", "currency": "TRY", "denomination": 50},
            {"slot": 3, "type": "Recycling", "currency": "TRY", "denomination": 20},
            {"slot": 4, "type": "Recycling", "currency": "USD", "denomination": 100},
            {"slot": 5, "type": "Deposit"}]},
        tests=status(200) + """
pm.test("ATM is CONFIGURED", () => pm.expect(pm.response.json().status).to.eql("Configured"));
"""),

    req("03 Load cash", "POST", "/api/atms/{{atmId}}/cash-loads",
        headers={"X-User": "ops.loader"},
        body={"items": [{"slot": 1, "count": 50}, {"slot": 2, "count": 40}, {"slot": 3, "count": 100}, {"slot": 4, "count": 20}]},
        tests=status(200) + """
const atm = pm.response.json();
pm.test("ATM is LOADED", () => pm.expect(atm.status).to.eql("Loaded"));
pm.test("TRY balance is 9,000", () => pm.expect(atm.balances.find(b => b.currency === "TRY").totalAmount).to.eql(9000));
"""),

    req("04 Put in service", "POST", "/api/atms/{{atmId}}/put-in-service",
        headers={"X-User": "ops.supervisor"},
        tests=status(200) + """
pm.test("ATM is IN_SERVICE", () => pm.expect(pm.response.json().status).to.eql("InService"));
"""),

    req("05 Request withdrawal quote", "POST", "/api/atms/{{atmId}}/withdrawal-quotes",
        body='{\n  "cardNumber": "{{cardNumber}}",\n  "currency": "TRY",\n  "amount": 1160\n}',
        tests=status(201) + """
const quote = pm.response.json();
pm.test("Exact match with the fewest notes", () => {
    pm.expect(quote.isExactMatch).to.be.true;
    pm.expect(quote.options[0].notes).to.eql([{ denomination: 100, count: 11 }, { denomination: 20, count: 3 }]);
});
pm.collectionVariables.set("quoteId", quote.quoteId);
pm.collectionVariables.set("idempotencyKey", pm.variables.replaceIn("{{$guid}}"));
"""),

    req("06 Confirm quote", "POST", "/api/withdrawal-quotes/{{quoteId}}/confirm",
        headers={"Idempotency-Key": "{{idempotencyKey}}"},
        body={"option": "Exact"},
        tests=status(200) + """
const result = pm.response.json();
pm.test("1,160 TRY dispensed", () => pm.expect(result.dispensedAmount).to.eql(1160));
pm.collectionVariables.set("confirmedAt", result.confirmedAt);
"""),

    req("07 Confirm again with the same Idempotency-Key", "POST", "/api/withdrawal-quotes/{{quoteId}}/confirm",
        headers={"Idempotency-Key": "{{idempotencyKey}}"},
        body={"option": "Exact"},
        description="Retry of the previous request: returns the stored result, cash is not debited twice.",
        tests=status(200) + """
pm.test("Same result is replayed", () => pm.expect(pm.response.json().confirmedAt).to.eql(pm.collectionVariables.get("confirmedAt")));
"""),

    req("08 Deposit", "POST", "/api/atms/{{atmId}}/deposits",
        body={"notes": [
            {"currency": "TRY", "denomination": 100, "count": 5},
            {"currency": "TRY", "denomination": 500, "count": 2},
            {"currency": "EUR", "denomination": 50, "count": 3}],
            "unrecognizedCount": 1},
        tests=status(200) + """
const deposit = pm.response.json();
pm.test("TRY 100 goes to its recycling cassette", () =>
    pm.expect(deposit.accepted.find(a => a.denomination === 100).slot).to.eql(1));
pm.test("Notes without a recycling cassette go to the deposit cassette", () =>
    pm.expect(deposit.accepted.filter(a => a.cassetteType === "Deposit").length).to.eql(2));
pm.test("Unrecognised note is returned", () =>
    pm.expect(deposit.returned).to.deep.include({ currency: null, denomination: null, count: 1, reason: "Unrecognized" }));
"""),

    req("09 Switch to maintenance", "POST", "/api/atms/{{atmId}}/maintenance",
        headers={"X-User": "ops.supervisor"},
        tests=status(200) + """
const atm = pm.response.json();
pm.test("ATM is in MAINTENANCE", () => pm.expect(atm.status).to.eql("Maintenance"));
// Prepare the manual count: everything matches except one TRY 100 note (100 TRY, under the approval threshold).
const counts = atm.cassettes.flatMap(c => c.type === "Recycling"
    ? [{ slot: c.slot, count: c.slot === 1 ? c.count - 1 : c.count }]
    : c.contents.map(n => ({ slot: c.slot, currency: n.currency, denomination: n.denomination, count: n.count })));
pm.collectionVariables.set("reconcileCounts", JSON.stringify({ counts }, null, 2));
// Second count for the four-eyes demo: 20 more TRY 100 notes missing (2,000 TRY).
const bigCounts = counts.map(c => c.slot === 1 ? Object.assign({}, c, { count: c.count - 20 }) : c);
pm.collectionVariables.set("reconcileCountsLarge", JSON.stringify({ counts: bigCounts }, null, 2));
"""),

    req("10 Reconcile (small difference, applied)", "POST", "/api/atms/{{atmId}}/reconciliations",
        headers={"X-User": "alice"},
        body="{{reconcileCounts}}",
        tests=status(201) + """
const r = pm.response.json();
pm.test("Applied immediately (100 TRY <= 1,000 TRY)", () => {
    pm.expect(r.status).to.eql("Applied");
    pm.expect(r.totalDifferenceTry).to.eql(100);
});
"""),

    req("11 Reconcile (large difference, needs approval)", "POST", "/api/atms/{{atmId}}/reconciliations",
        headers={"X-User": "alice"},
        body="{{reconcileCountsLarge}}",
        description="The first count was applied, so this count is 20 TRY 100 notes short of the new system figures (2,000 TRY).",
        tests=status(201) + """
const r = pm.response.json();
pm.test("Awaiting four-eyes approval", () => pm.expect(r.status).to.eql("AwaitingApproval"));
pm.collectionVariables.set("reconciliationId", r.id);
"""),

    req("12 Put in service is blocked while awaiting approval", "POST", "/api/atms/{{atmId}}/put-in-service",
        headers={"X-User": "ops.supervisor"},
        tests=status(409) + """
pm.test("Error code", () => pm.expect(pm.response.json().errorCode).to.eql("RECONCILIATION_AWAITING_APPROVAL"));
"""),

    req("13 Approve by the same user is rejected", "POST", "/api/reconciliations/{{reconciliationId}}/approve",
        headers={"X-User": "alice"},
        tests=status(422) + """
pm.test("Error code", () => pm.expect(pm.response.json().errorCode).to.eql("FOUR_EYES_VIOLATION"));
"""),

    req("14 Approve by a different user", "POST", "/api/reconciliations/{{reconciliationId}}/approve",
        headers={"X-User": "bob"},
        tests=status(200) + """
pm.test("Approved", () => pm.expect(pm.response.json().status).to.eql("Approved"));
"""),

    req("15 Put back in service", "POST", "/api/atms/{{atmId}}/put-in-service",
        headers={"X-User": "ops.supervisor"}, tests=status(200)),

    req("16 Operations report", "GET", "/api/reports/operations?atmId={{atmId}}",
        tests=status(200) + """
const atm = pm.response.json().atms[0];
pm.test("Five cassettes with note / reserved / available counts", () => {
    pm.expect(atm.cassettes).to.have.length(5);
    atm.cassettes.forEach(c => pm.expect(c.availableCount).to.eql(c.noteCount - c.reservedCount));
});
pm.test("Totals per currency and overall TRY total", () => {
    pm.expect(atm.totals.map(t => t.currency)).to.include.members(["TRY", "USD", "EUR"]);
    pm.expect(atm.totalTry).to.be.above(0);
});
"""),

    req("17 Ledger consistency check", "GET", "/api/atms/{{atmId}}/ledger/consistency",
        tests=status(200) + """
pm.test("Stored balances match the movement ledger", () => pm.expect(pm.response.json().isConsistent).to.be.true);
"""),

    req("18 Movement ledger", "GET", "/api/atms/{{atmId}}/movements?take=50", tests=status(200)),

    req("19 ATM status history", "GET", "/api/atms/{{atmId}}/status-history",
        description="Every status change with the X-User who made it, newest first. The blocked attempt (12) is not recorded.",
        tests=status(200) + """
const history = pm.response.json();
pm.test("Five transitions recorded, newest first", () => {
    pm.expect(history.map(h => h.fromStatus + "->" + h.toStatus)).to.eql([
        "Maintenance->InService", "InService->Maintenance", "Loaded->InService", "Configured->Loaded", "Unconfigured->Configured"]);
});
pm.test("Each transition names the user who made it", () =>
    pm.expect(history.find(h => h.toStatus === "Loaded").performedBy).to.eql("ops.loader"));
"""),
]

VARIABLES = ["atmCode", "cardNumber", "atmId", "quoteId", "idempotencyKey", "confirmedAt",
             "reconcileCounts", "reconcileCountsLarge", "reconciliationId"]

collection = {
    "info": {
        "_postman_id": str(uuid.uuid5(uuid.NAMESPACE_URL, "atmcash-collection")),
        "name": "ATM Cash Management - End to end",
        "description": "Run in order (Collection Runner or newman): create ATM -> configure cassettes -> load cash -> "
                       "put in service -> request quote -> confirm -> deposit -> switch to maintenance -> reconcile -> report. "
                       "Extra steps show the idempotent replay and the four-eyes approval.",
        "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json",
    },
    "item": ITEMS,
    "variable": [{"key": key, "value": ""} for key in VARIABLES],
}

environment = {
    "id": str(uuid.uuid5(uuid.NAMESPACE_URL, "atmcash-env")),
    "name": "ATM Cash - local (docker compose)",
    "values": [{"key": "baseUrl", "value": "http://localhost:8080", "type": "default", "enabled": True}],
    "_postman_variable_scope": "environment",
}

with open(os.path.join(HERE, "AtmCash.postman_collection.json"), "w", encoding="utf-8") as f:
    json.dump(collection, f, indent=2)
with open(os.path.join(HERE, "AtmCash.local.postman_environment.json"), "w", encoding="utf-8") as f:
    json.dump(environment, f, indent=2)

print(f"Wrote {len(ITEMS)} requests")
