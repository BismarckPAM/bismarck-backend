-- Seeds a realistic BIS-402 event set. Column values mirror exactly what
-- AnalyticsEventProcessor.Create writes when the Kafka consumer handles the
-- corresponding SecurityEvent envelopes.
TRUNCATE TABLE "AnalyticsEvents";

INSERT INTO "AnalyticsEvents"
  ("Id","EventId","SourceTopic","EventType","OccurredAt","OccurredAtDate","Actor",
   "Resource","ResourceName","Action","Outcome","DenialReason","Metadata","ConsumedAt")
VALUES
-- 2 REQUESTS: ApprovalRequested (the only event type that counts as a request)
(gen_random_uuid(), gen_random_uuid(), 'approval-requested', 'ApprovalRequested',
 '2026-10-05T09:00:00Z','2026-10-05','user-1','res-prod','Production Database',
 'ELEVATED_ACCESS','REQUESTED', NULL, '{"ResourceName":"Production Database"}', now()),

(gen_random_uuid(), gen_random_uuid(), 'approval-requested', 'ApprovalRequested',
 '2026-10-05T11:30:00Z','2026-10-05','user-2','res-billing','Billing API',
 'ELEVATED_ACCESS','REQUESTED', NULL, '{"ResourceName":"Billing API"}', now()),

-- 1 APPROVAL: ApprovalGranted
(gen_random_uuid(), gen_random_uuid(), 'approval-granted', 'ApprovalGranted',
 '2026-10-06T10:00:00Z','2026-10-06','admin-1','res-prod','Production Database',
 'ELEVATED_ACCESS','APPROVED', NULL, '{"ResourceName":"Production Database"}', now()),

-- 2 DENIALS: one AccessDenied (Reason) + one ApprovalRejected (RejectionReason)
(gen_random_uuid(), gen_random_uuid(), 'access-denied', 'AccessDenied',
 '2026-10-07T08:15:00Z','2026-10-07','user-3','res-prod', NULL,
 'READ','DENIED','INSUFFICIENT_ROLE_PERMISSIONS','{"Reason":"INSUFFICIENT_ROLE_PERMISSIONS"}', now()),

(gen_random_uuid(), gen_random_uuid(), 'approval-rejected', 'ApprovalRejected',
 '2026-10-07T09:45:00Z','2026-10-07','admin-1','res-billing', NULL,
 'ELEVATED_ACCESS','REJECTED','Business justification missing',
 '{"RejectionReason":"Business justification missing"}', now()),

-- 2 REVOCATIONS: automatic expiry (permission-revoked) + admin JIT manual revoke (jit-revoked)
(gen_random_uuid(), gen_random_uuid(), 'permission-revoked', 'PermissionRevoked',
 '2026-10-08T12:00:00Z','2026-10-08','system:expiration-worker','res-prod', NULL,
 'EXPIRE_PERMISSION','SUCCESS', NULL,
 '{"Reason":"Automatic expiration by background worker past TTL."}', now()),

(gen_random_uuid(), gen_random_uuid(), 'jit-revoked', 'PermissionRevoked',
 '2026-10-09T13:00:00Z','2026-10-09','system:admin','res-prod','Production Database',
 'MANUAL_REVOKE_JIT_SESSION','SUCCESS', NULL,
 '{"Reason":"Manual revocation by administrator"}', now()),

-- MUST NOT be counted anywhere: authorization-engine decisions
(gen_random_uuid(), gen_random_uuid(), 'access-requested', 'AccessRequested',
 '2026-10-05T08:00:00Z','2026-10-05','user-4','res-prod', NULL,
 'READ','APPROVAL_REQUIRED', NULL,
 '{"Reason":"ELEVATED_ACCESS_REQUIRES_APPROVAL"}', now()),

(gen_random_uuid(), gen_random_uuid(), 'access-granted', 'AccessGranted',
 '2026-10-05T08:30:00Z','2026-10-05','user-4','res-prod', NULL,
 'READ','ALLOWED', NULL, '{}', now()),

-- Outside the queried range, to prove date filtering works
(gen_random_uuid(), gen_random_uuid(), 'approval-requested', 'ApprovalRequested',
 '2026-11-15T09:00:00Z','2026-11-15','user-9','res-prod','Production Database',
 'ELEVATED_ACCESS','REQUESTED', NULL, '{"ResourceName":"Production Database"}', now());
