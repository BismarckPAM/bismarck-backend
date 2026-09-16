# Authorization Service Integration Contract

The Authorization Service is the decision point. A client sends one authorization check to the gateway; Authorization then reads live user data from Identity, live resource data from Resource, and evaluates the matching policy in its own database.

## Service addresses

| Service | Internal address | Public gateway route |
| --- | --- | --- |
| Identity | `http://identity-svc:8080` | `/api/identity` |
| Resource | `http://resource-svc:8080` | `/api/resources` |
| Authorization | `http://authorization-svc:8080` | `/api/authorization` |

All service calls use JSON over HTTP. The caller forwards the incoming `Authorization: Bearer <jwt>` header to Identity and Resource. All three services validate the same JWT issuer, audience, signing key, and lifetime.

## Authorization check

`POST /api/authorization/check`

Request:

```json
{
  "userId": "11111111-1111-1111-1111-111111111111",
  "resourceId": "22222222-2222-2222-2222-222222222222",
  "action": "READ_STATUS",
  "sessionDurationMinutes": 120
}
```

`sessionDurationMinutes` is optional and must be between 1 and 1440. The response is always HTTP 200 for a completed decision. The decision is in the body:

```json
{
  "decision": "ALLOW",
  "reason": "AUTHORIZED",
  "details": "Authorization granted.",
  "approvalRequirement": "NONE",
  "expiresAt": "2026-09-12T12:00:00Z"
}
```

A denial has the same shape with `decision: "DENY"`, a stable `reason`, and no `expiresAt`:

```json
{
  "decision": "DENY",
  "reason": "SYSTEM_ERROR_FAIL_CLOSED",
  "details": "Authorization denied because a required dependency failed.",
  "approvalRequirement": "NONE",
  "expiresAt": null
}
```

Known denial reasons include `USER_NOT_FOUND`, `USER_DEACTIVATED`, `USER_ROLE_NOT_ASSIGNED`, `RESOURCE_NOT_FOUND`, `UNKNOWN_ACTION`, `INSUFFICIENT_ROLE_PERMISSIONS`, and `SYSTEM_ERROR_FAIL_CLOSED`.

## Downstream contracts

### Identity lookup

`GET /api/identity/users/{userId}`

Successful response (`200 OK`):

```json
{
  "id": "11111111-1111-1111-1111-111111111111",
  "roleId": "33333333-3333-3333-3333-333333333333",
  "roleName": "Developer",
  "isActive": true
}
```

Identity may include additional fields such as `fullName`, `email`, or `departmentName`; Authorization consumes only the four fields above. `404` maps to `USER_NOT_FOUND`. Any other non-success status, timeout, connection failure, cancellation caused by the client, or invalid JSON maps to `SYSTEM_ERROR_FAIL_CLOSED`. Authorization never grants access when Identity cannot be verified.

### Resource lookup

`GET /api/resources/{resourceId}`

Successful response (`200 OK`):

```json
{
  "id": "22222222-2222-2222-2222-222222222222",
  "type": "ComputeInstance",
  "owner": "PlatformTeam",
  "environment": "Production",
  "criticality": "HIGH",
  "isActive": true,
  "createdAt": "2026-09-12T10:00:00Z"
}
```

Authorization consumes `type`, `environment`, `criticality`, and `isActive`. `404` or an inactive resource maps to `RESOURCE_NOT_FOUND`. Any other non-success status, timeout, connection failure, or invalid/missing required fields maps to `SYSTEM_ERROR_FAIL_CLOSED`.

### Policy data

Policies are managed by Authorization at `/authz/policies`. A policy is matched by normalized `role`, `resourceType`, `environment`, and `criticality`, and supplies `maxAccessLevel` from 0 through 5. No matching active policy means access is denied because the required action level is greater than zero.

## Failure and consistency rules

1. Identity and Resource are requested concurrently for every check.
2. The check uses the current response from both services; it does not cache user or resource state.
3. Dependency failures, malformed responses, database failures, unknown actions, inactive users, and inactive/missing resources cannot produce `ALLOW`.
4. A caller cancellation is propagated so the server does not continue work after the request is gone; an upstream timeout is converted to a completed fail-closed denial.

## End-to-end verification scenarios

The integration suite should execute the following against the running services and assert the response body, not only the HTTP status:

| # | Setup and request | Expected result |
| --- | --- | --- |
| 1 | Create an active user, active resource, and matching policy; check an action within `maxAccessLevel`. | `ALLOW` with the requested `expiresAt` |
| 2 | Use the same real user/resource with an action above the policy level. | `DENY` with `INSUFFICIENT_ROLE_PERMISSIONS` |
| 3 | Deactivate the user, then repeat the check. | `DENY` with `USER_DEACTIVATED` |
| 4 | Use an unknown resource ID or soft-delete the resource, then check. | `DENY` with `RESOURCE_NOT_FOUND` |
| 5 | Stop or delay Identity or Resource beyond the 3-second downstream timeout. | `DENY` with `SYSTEM_ERROR_FAIL_CLOSED` |

The first scenario is AC-1: it proves a real Identity user and real Resource resource are resolved before policy evaluation. Scenarios 2-5 cover policy, live state, not-found, and fail-closed behavior required by AC-2 and AC-4.
