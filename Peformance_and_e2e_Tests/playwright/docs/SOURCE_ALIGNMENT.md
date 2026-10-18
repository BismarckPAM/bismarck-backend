# Source alignment and practical limits

Reviewed on 16 October 2026 against BismarckPAM/bismarck-frontend and
BismarckPAM/bismarck-backend, branch deploy/production. Recheck selectors/contracts
if your testing branch contains newer changes.

| Contract / selector | Reviewed source |
| --- | --- |
| Login labels, button and original-route destination | frontend/src/pages/Login.tsx |
| Token, user, expiry storage keys and 401 interceptor | frontend/src/api/client.ts |
| Session initialization and expiration | frontend/src/context/AuthProvider.tsx |
| Developer versus Admin route gates | frontend/src/App.tsx; src/auth/roles.ts |
| data-testid=forbidden | frontend/src/components/auth/RequireRole.tsx |
| Search/filter/error/empty inventory states | frontend/src/pages/Resources.tsx |
| Directory search states | frontend/src/pages/Users.tsx |
| Request field IDs, 20-character minimum, 1440-minute maximum | frontend/src/pages/RequestAccess.tsx |
| Timeout recovery, two reconciliation attempts, no repeat POST | frontend/src/pages/RequestAccess.tsx |
| Exact approval/rejection modal controls | frontend/src/pages/ApprovalQueue.tsx |
| Full request ID in td title; history search and details | frontend/src/pages/MyRequests.tsx |
| JIT active table, revoke modal and developer restrictions | frontend/src/pages/JitAccess.tsx |
| Request API endpoints, DTO status normalization | frontend/src/api/approval.ts; src/types/pam.ts |
| Approval state transition conflict and ownership risk | backend/Approval/Controllers/ApprovalController.cs; Services/ApprovalService.cs |
| JWT scoping and Admin revoke authority | backend/AuthorizationService/Controllers/JitSessionsController.cs |
| Per-user notification endpoint and non-owner 403 | backend/Notification/Controllers/NotificationsController.cs |
| ApprovalGranted notification title/event routing | backend/Notification/Services/KafkaNotificationConsumer.cs |
| Audit event actor and raw metadata storage | backend/Audit/Services/KafkaAuditConsumer.cs |

## Deliberate limits

- These 56 cases are a focused, deep E2E contribution, not exhaustive coverage of
  every PAM feature, browser, device or threat model. Chromium is the configured browser.
- No real SSH connection, Azure role assignment or terminal command execution is asserted.
  The suite verifies application JIT grant records and revocation. A LOCAL_ONLY grant
  must not be described as successful cloud provisioning.
- Grant expiry timestamp/duration is checked; waiting for automatic real-time expiry is
  not implemented. That can be added as a separately timed long-running scenario.
- Read tests use real APIs; resilience tests deliberately inject responses. The timeout
  test uses a real backend write with controlled response latency. Keep those claims distinct.
- No test infers successful service integration from a visible page heading alone.
- Some source comments/UI notices are stale: the frontend notifications page says read
  state is local, while the current backend has persistent read endpoints; the Dashboard
  notice mentions missing endpoints that now exist. No notification read persistence
  assumption is built into this suite. Report such discrepancies separately.
- E2E-52 is a strict expected-security behaviour and may currently fail. This is useful
  regression coverage, not evidence that the implementation already satisfies the requirement.

## How to investigate a failure

1. Distinguish bad prerequisites, test defect and application defect.
2. Inspect the exact failed assertion, captured response status, role trace and IDs.
3. Compare the selector/API contract with the actual branch under test.
4. For async failures, check Kafka consumers and downstream service availability.
5. Preserve evidence, fix the correct layer, rerun the affected project, then the full suite.

Never accept both allowed and forbidden outcomes for a known role; never suppress 500s,
replace a business assertion with a heading check, or count skipped tests as passed.
