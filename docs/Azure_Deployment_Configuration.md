# Bismarck Backend - Azure Deployment Configuration Guide

This document explains how the event bus (Kafka) fits into the Bismarck
architecture, how to configure every environment variable for Azure Container
Apps, and where the current implementation has gaps that need code changes.

Everything below is derived from the actual source (Program.cs, appsettings.json,
Kafka publishers/consumers, gateway middleware), not assumptions.

---

## 1. How Kafka works in this system

Kafka is **not a microservice the gateway talks to**. It is an internal
publish/subscribe message bus used only for **service-to-service events**.

```
                       ┌──────────────────────────────────────┐
                       │             Kafka broker             │
                       │  (9 topics — see KafkaTopics.cs)     │
                       └───────▲───────────────▲──────────────┘
        produce                │               │            consume
 ┌──────────────┬──────────────┼───────────┐   │   ┌───────────────┬────────────────┐
 │ Identity     │ Resource     │ Approval  │   └───│ Audit         │ Notification   │
 │ (identity-   │ (resource-   │ (approval-│       │ (group:       │ (group:        │
 │  events)     │  events)     │  *)       │       │ bismarck-     │ bismarck-      │
 │              │              │           │       │ audit-service)│ notification-  │
 └──────────────┴──────────────┴───────────┘       │               │ service)       │
        ▲                                          └───────────────┴────────────────┘
        │                                          ┌────────────────────────────────┐
        └──────────────────────────────────────────│ AuthorizationService           │
             consumes "approval-granted"           │ (group: bismarck-authorization-│
             (group: bismarck-authorization-service)│  service)                      │
                                                   └────────────────────────────────┘
```

### Producers (publish events)

| Service | Topic(s) published | Code |
|---|---|---|
| **Identity** | `identity-events` (hard-coded string) | `identity/Services/KafkaDomainEventPublisher.cs`, `UserService.cs` |
| **Resource** | `resource-events` (hard-coded string) | `resource/Services/KafkaDomainEventPublisher.cs`, `ResourceService.cs` |
| **Approval** | `approval-requested`, `approval-granted`, `approval-rejected` | `Approval/Services/ApprovalService.cs` |
| **AuthorizationService** | `access-granted`, `permission-revoked` | `KafkaAuthorizationEventPublisher.cs`, `TemporaryPermissionExpirationWorker.cs` |

### Consumers (`BackgroundService` loops)

| Service | Subscribes to | Default group ID |
|---|---|---|
| **Audit** | all 9 topics in `KafkaTopics.All` | `bismarck-audit-service` |
| **Notification** | the 7 security topics **plus** `identity-events` (not `resource-events`) | `bismarck-notification-service` |
| **AuthorizationService** | `approval-granted` only | `bismarck-authorization-service` |

### The canonical topics (`BuildingBlocks/Messaging/KafkaTopics.cs`)

```
access-requested
access-granted
access-denied
approval-requested
approval-granted
approval-rejected
permission-revoked
identity-events      (registered by the BUG-001 fix)
resource-events      (registered by the BUG-001 fix)
```

> **Important:** Audit and Notification subscribe to overlapping topics. They
> MUST use **different `Kafka__GroupId` values**, otherwise Kafka would split
> messages between them instead of delivering each event to both.

> **FIXED (was BUG-001):** Identity publishes to `identity-events` and Resource
> to `resource-events`. These two topics are now registered in
> `BuildingBlocks/Messaging/KafkaTopics.cs` (`IdentityEvents`, `ResourceEvents`)
> and included in `KafkaTopics.All`, so they are no longer dropped:
>
> 1. **Audit** now subscribes to `KafkaTopics.All`, so it records
>    `user-created`, `user-updated`, `resource-created` and `resource-updated`
>    events alongside the security events.
> 2. **Notification** additionally subscribes to `identity-events` (the affected
>    user is the actor, so a per-user notification can be produced). It does
>    **not** subscribe to `resource-events`: those events carry a team `Owner`
>    as the actor, not a user GUID, so there is no valid per-user recipient and
>    subscribing would only push them to the dead-letter topic. Resource changes
>    remain visible in the audit log.
>
> Both services build cleanly after the change. No new environment variables are
> required — the two topics are auto-created by the broker on first publish.
>

### Where the gateway fits

The gateway (`gateway/Program.cs`) only does **HTTP reverse proxying + CORS +
a structural JWT check**. It never touches Kafka. Browser → Gateway → service
(HTTP) and service → Kafka → service (async) are two independent paths.


### Kafka for consumers vs producers

- Consumers (Audit, Notification, Authorization) tolerate the broker being
  briefly down — they log and retry every 2 seconds.
- Producers (Identity, Resource, Approval, Authorization) await the send. If the
  broker is unreachable, the failing HTTP request will error. Kafka health is
  therefore on the critical path for write endpoints.

