# Bismarck PAM — SE3112 Advanced Playwright E2E Contribution

56 discovered test cases across five projects. This is the E2E contribution only;
it deliberately contains no GitHub Actions workflow. Integrate it into your team's
testing branch after the suite has been executed and any genuine defects fixed.

## What is delivered and what is verified

- Implemented: browser tests, isolated role fixtures, real request/approval/JIT workflows,
  controlled failure scenarios, correlation evidence, cleanup and test documentation.
- Verified during preparation: dependency installation, TypeScript compilation, Playwright
  discovery of all 56 cases and source alignment with the inspected `deploy/production` code.
- NOT verified: authenticated browser execution, passing business workflows, live timings,
  or absence of flaky tests. No credentials were supplied. Browser installation in the
  preparation environment failed because the Chromium download was truncated.
- No passing report or runtime results have been invented. A successful `--list` only
  proves discovery, not test success. Run the suite in your environment before submission.

## Install on Windows / PowerShell

Requires Node.js 22 or later.

```powershell
npm ci
npx playwright install chromium
Copy-Item .env.example .env
notepad .env
npm run typecheck
npm run test:list
```

Linux CI later: install Chromium and system libraries with `npx playwright install --with-deps chromium`.

Set FRONTEND_URL and API_URL to the deployed frontend and gateway for your test
environment. They may use separate origins. Use URLs without trailing API paths.

Configure three distinct accounts, all owned by the testing team:

| Variables | Required role | Used by |
| --- | --- | --- |
| E2E_DEV_EMAIL / E2E_DEV_PASSWORD | Ordinary developer; not Admin, Manager or Security Admin | Read, resilience, lifecycle, security |
| E2E_ADMIN_EMAIL / E2E_ADMIN_PASSWORD | Admin, with approval and revoke authority | Read, lifecycle, security |
| E2E_OTHER_EMAIL / E2E_OTHER_PASSWORD | Second ordinary developer | Cross-user security checks |

The public project needs no accounts. Role fixtures verify the signed-in identity and
role; accounts must have different user IDs. Credentials are never committed.
The role fixtures sign in via the real login UI once per worker, cache authentication
in memory, then give each test fresh independent browser contexts. This avoids repeated
logins without sharing mutable browser state. Tests explicitly about login use fresh UI login.

## Prepare test data

1. Use a resettable test deployment with the actual frontend, gateway, Identity,
   Resource, Approval, Authorization, Notification, Audit services and Kafka running.
2. Create at least two resources; at least one must have an environment value.
3. Select a dedicated **non-VM resource without an Azure target**, such as a test database.
   Set its UUID as E2E_RESOURCE_ID. No existing production grant should use this resource.
4. For lifecycle/security, set E2E_ALLOW_WRITES=true. Keep E2E_LEVEL=1 and use a short
   E2E_DURATION_MINUTES value (default 5; allowed 1..10).
5. Keep these test accounts exclusive to this suite while it runs, particularly the
   notification test. Workers=1 prevents this suite from overlapping its own writes.
6. Complete setup before the demo. Do not seed or reset your environment during the two minutes.

Missing prerequisites cause clear failures; they never silently skip protected coverage.
The write guard is an explicit configuration switch, not an interactive approval prompt.

## Execute

```powershell
npm run test:public
npm run test:read
npm run test:resilience
npm run test:lifecycle
npm run test:security
# Everything, after every prerequisite is configured:
npm test
npm run report
```

| Project | Cases | What it proves |
| --- | ---: | --- |
| public | 12 | Entry point, form validation, password controls, anonymous redirects, not-found routing |
| read | 26 | Login/session behaviour, precise route RBAC, API-backed UI filtering, counters, validation, authorization decision rendering |
| resilience | 5 | Browser error handling under explicitly controlled 503/401/network/empty/malformed responses |
| lifecycle | 8 | Real create/approve/reject/grant/revoke, cancel, duplicate prevention, persistence, timeout recovery, asynchronous side effects, positive validation boundaries |
| security | 5 | Real API authorization and ownership checks supporting the browser workflows |

## Meaningful evidence rather than page headings

- Search compares rendered rows with real API results and verifies no-match/clear behaviour.
- Known developer access must show `forbidden`; known Admin access must actually succeed.
- Lifecycle correlation uses full request ID, approval ID and permission ID. Never select the
  first existing request or approve unrelated requests.
- The approval response, reviewer, persisted status and real JIT permission are checked.
- Eventual consistency uses bounded polling, not arbitrary sleeps or unlimited retries.
- The duplicate-click case checks both POST count and persisted count for the unique reason.
- Timeout test forwards exactly one real POST, withholds its response until Axios times out,
  and verifies server reconciliation returns the one committed request. Its latency injection
  is explicitly labelled; it is not a naturally occurring production outage.
- Audit metadata is correlated by request UUID. Notifications lack an ApprovalId in the DTO;
  notification evidence uses a fresh notification ID + requester + event type, with exclusive
  accounts. Do not claim this proves exact request-ID correlation for notifications.
- Direct API security calls supplement the browser journeys. Do not describe those checks
  as UI clicks or as unit tests.

## Cleanup and failure evidence

Each mutating test has a UUID marker in its request justification, except the exact
20-character boundary case which uses a unique random 20-character reason. Teardown queries
the developer's own persisted history for that marker or exact tracked reason, rejects any leftover pending
requests, waits for asynchronous grants on approved requests, and revokes still-active
test grants. This also works if creation persisted but the response was lost.

Only marker-matched data is touched. There is no supported request-delete controller
endpoint in the inspected source, so historical requests and audit records remain for
evidence. Reset the disposable environment between assessment rehearsals as needed.
Cleanup failures fail the test; the suite does not conceal them. If a write causes a
service outage, restore it and inspect the retained marker/IDs before rerunning.

HTML, JSON and JUnit reports go to playwright-report/ and test-results/. Failed custom
developer/Admin/other contexts attach their screenshots, traces and videos. Successful
role-context videos are removed. Traces and videos can contain account details, tokens
and privileged data; keep artifacts private and redact screenshots used in slides.

## Known strict regression 

E2E-52 expects a non-owner request-ID lookup to return 403 or 404, with no request
details. The inspected ApprovalController.GetById / ApprovalService.GetByIdAsync
retrieve by ID without a visible owner-or-approver check. This test is likely to expose
an existing data-disclosure defect. Runtime behaviour may also depend on gateway policy.

If it fails with 200, retain the evidence and fix application authorization with your
team, then rerun it. It is not skipped, marked expected-to-fail, or changed to accept 200.
The suite's existence cannot guarantee an 85–100 mark or a passing application.




