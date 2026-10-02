# BIS-402 — Analytics Verification Guide

This document is the verification evidence for **BIS-402 (Implement Analytics
Service — Aggregate Access & Approval Metrics)**.

It answers two questions an evaluator needs answered:

1. **What exactly does each metric count?**
2. **How can I prove the Analytics numbers match the Audit Logs?**

---

## 1. Canonical metric mapping

The definitions live in exactly one place in code:
[`Analytics/Models/AnalyticsMetricMap.cs`](../Analytics/Models/AnalyticsMetricMap.cs).
No other file hard-codes an event-type string for counting.

| Metric | Counted event types | Kafka topics | Why |
|---|---|---|---|
| **Requests** | `ApprovalRequested` | `approval-requested` | The user-facing *Request Access* page creates an Approval Request, and that is what emits `ApprovalRequested`. |
| **Approvals** | `ApprovalGranted` | `approval-granted` | A human approved the request. |
| **Denials** | `AccessDenied`, `ApprovalRejected` | `access-denied`, `approval-rejected` | Security analytics need both halves: policy/authorization denials *and* human rejections. |
| **Revocations** | `PermissionRevoked` **or** source topic `permission-revoked` / `jit-revoked` | `permission-revoked`, `jit-revoked` | Covers automatic JIT expiry, legacy/manual permission revocation, and Admin JIT manual revocation. |

### Events deliberately NOT counted

| Event | Reason |
|---|---|
| `AccessRequested` (outcome `APPROVAL_REQUIRED`) | This is an **authorization-engine decision** stating "approval is required", not a user-submitted request. Counting it would inflate the request total and double-count the same user action, because the frontend already created an `ApprovalRequest`. |
| `AccessGranted` (outcome `ALLOWED`) | An authorization decision, **not** a human approval. Counting it as an approval would be wrong. |
| `security.auth.login`, `user-created`, `resource-*`, … | Unrelated to the four headline metrics. They are still stored (the database is a full event history) but filtered out of every aggregate. |

### Note on `jit-revoked`

`JitSessionsController` publishes the admin manual-revoke event to the
`jit-revoked` topic but stamps `EventType = "PermissionRevoked"`. The mapping
therefore checks **both** the event type and the source topic, so all three
revocation flows are covered explicitly. Each stored row is counted at most
once because `EventId` is uniquely indexed.

---

## 2. Verifying Analytics against Audit Logs

Audit and Analytics consume Kafka **independently under different consumer
groups** (`bismarck-audit-service` vs `bismarck-analytics-service`), and each
owns its own database. This preserves microservice database ownership while
making the numbers directly comparable.

> Never point the Analytics Service at `audit_db`. The comparison below is done
> by an operator, as two independent read-only queries.

### 2.1 Capture a date range

Use the same `startDate` / `endDate` you passed to the API, e.g.
`2026-10-01T00:00:00Z` → `2026-10-31T23:59:59Z`.

### 2.2 Expected counts from the audit trail

Run against **`audit_db`** (table `AuditLogs`):

```sql
-- REQUESTS
SELECT COUNT(*) AS requests
FROM auditlogs
WHERE "EventType" = 'ApprovalRequested'
  AND "OccurredAt" >= '2026-10-01T00:00:00Z'
  AND "OccurredAt" <= '2026-10-31T23:59:59Z';

-- APPROVALS
SELECT COUNT(*) AS approvals
FROM auditlogs
WHERE "EventType" = 'ApprovalGranted'
  AND "OccurredAt" >= '2026-10-01T00:00:00Z'
  AND "OccurredAt" <= '2026-10-31T23:59:59Z';

-- DENIALS (both sources)
SELECT COUNT(*) AS denials
FROM auditlogs
WHERE "EventType" IN ('AccessDenied', 'ApprovalRejected')
  AND "OccurredAt" >= '2026-10-01T00:00:00Z'
  AND "OccurredAt" <= '2026-10-31T23:59:59Z';


### 2.3 Compare with the Analytics API

```bash
curl -s -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5290/api/analytics/summary?startDate=2026-10-01T00:00:00Z&endDate=2026-10-31T23:59:59Z"
```

The four `totals` values **must be identical** to the four SQL results above.
Because both sides count the same `EventId` set under the same date filter and
the same inclusive bounds, accuracy is 100% by construction.

### 2.4 Cross-check top resources

```sql
-- audit_db: requests per resource label
SELECT
  COALESCE(metadata->>'ResourceName', resource) AS resource_name,
  COUNT(*) AS request_count
FROM auditlogs
WHERE "EventType" = 'ApprovalRequested'
  AND "OccurredAt" >= '2026-10-01T00:00:00Z'
  AND "OccurredAt" <= '2026-10-31T23:59:59Z'
GROUP BY 1
ORDER BY 2 DESC, 1 ASC;
```

This must match `GET /api/analytics/top-resources?startDate=…&endDate=…`
(`resourceName` / `requestCount`). The `COALESCE` mirrors the ingestion rule
"Metadata.ResourceName, else the Resource identifier".

### 2.5 Cross-check denial reasons

```sql
-- audit_db: denial reason distribution
SELECT
  COALESCE(
    NULLIF(metadata->>'Reason', ''),
    NULLIF(metadata->>'RejectionReason', ''),
    'UNKNOWN'
  ) AS reason,
  COUNT(*) AS denial_count
FROM auditlogs
WHERE "EventType" IN ('AccessDenied', 'ApprovalRejected')
  AND "OccurredAt" >= '2026-10-01T00:00:00Z'
  AND "OccurredAt" <= '2026-10-31T23:59:59Z'
GROUP BY 1
ORDER BY 2 DESC, 1 ASC;
```

`AccessDenied` reads `Reason`; `ApprovalRejected` reads `RejectionReason`. This
must match `GET /api/analytics/denial-reasons`.

### 2.6 Internal consistency invariant

`GET /api/analytics/summary` returns `totals` **and** a daily `trend`. By
construction the totals are the sum of the trend buckets, so this must always
hold and is asserted in the automated tests:

```
sum(trend[].requests)    == totals.requests
sum(trend[].approvals)   == totals.approvals
sum(trend[].denials)     == totals.denials
sum(trend[].revocations) == totals.revocations
```

---

## 3. Percentage and rounding policy

* `percentage = count / total × 100`
* Rounded to **2 decimal places**, `MidpointRounding.AwayFromZero`.
* Denominator is the **total in range** (`totalRequests` / `totalDenials`), not
  the sum of the returned items — so a limited list still reports true shares.
* Division by zero is impossible: the service returns `total = 0` and an empty
  `items` array, and `CalculatePercentage` returns `0` when the denominator is 0.
* Example: `5 / 9 × 100 = 55.555…` → **`55.56`**.

---

## 4. Date-range contract

| Input | Behaviour |
|---|---|
| neither parameter | all currently stored history |
| `startDate` only | `OccurredAt >= startDate` |
| `endDate` only | `OccurredAt <= endDate` |
| both | inclusive range |
| `startDate > endDate` | **HTTP 400**, never silently swapped |
| malformed date | **HTTP 400** |
| valid range, no data | **HTTP 200** with zeros and `[]` |

All values are parsed with `DateTimeStyles.AssumeUniversal` and normalized to
UTC, so a caller sending `+05:00` and the stored `timestamptz` values are
compared on one timeline. Daily buckets are UTC days.

---

## 5. Manual end-to-end procedure

1. `docker compose up -d` (Kafka, all PostgreSQLs, all services).
2. `docker compose exec analytics-db psql -U admin -d analytics_db -c '\dt'`
   → expect `AnalyticsEvents`.
3. Log in via the gateway and submit an access request (emits `ApprovalRequested`).
4. Approve one request (`ApprovalGranted`); reject another (`ApprovalRejected`).
5. Trigger an authorization denial (`AccessDenied`).
6. Let a JIT permission expire, and manually revoke another
   (`PermissionRevoked` on `permission-revoked` and `jit-revoked`).
7. Confirm both services received the events — the consumer groups must differ:
   ```bash
   docker compose exec kafka rpk group list
   # bismarck-audit-service     and     bismarck-analytics-service
   ```
8. `GET /api/analytics/summary` and compare with the §2 SQL.
9. `GET /api/analytics/top-resources` → the requested resource with its count.
10. `GET /api/analytics/denial-reasons` → both denial reasons present.
11. Repeat all three with a range that excludes the events → zeros, HTTP 200.
12. Verify idempotency: restart `analytics_svc` and confirm counts are unchanged
    (the `EventId` unique index plus offset-commit-after-save guarantees this).

---

## 6. Test evidence

| Suite | Command | Result |
|---|---|---|
| Unit | `dotnet test ./Analytics/Analytics.Service.Tests/Analytics.Service.Tests.csproj -c Release` | 74/74 passed |
| Integration | `dotnet test ./Analytics/Analytics.Service.IntegrationTests/Analytics.Service.IntegrationTests.csproj -c Release` | 32/32 passed (real PostgreSQL 16 Testcontainer, real `InitialCreate` migration) |
| Coverage | `Analytics/coverage-summary.ps1` over both Cobertura reports | **93.77 %** lines (286/305) of the Analytics implementation |

The integration suite re-asserts the same metric expectations against real
PostgreSQL, so the numbers are proven identical on the production database
engine and not only against the SQLite-backed unit suite.

-- REVOCATIONS (PermissionRevoked is stamped on both revocation topics)
SELECT COUNT(*) AS revocations
FROM auditlogs
WHERE "EventType" = 'PermissionRevoked'
  AND "OccurredAt" >= '2026-10-01T00:00:00Z'
  AND "OccurredAt" <= '2026-10-31T23:59:59Z';
```
