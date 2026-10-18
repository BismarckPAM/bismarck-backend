# Executable test case catalogue

56 uniquely identified cases. Runtime status for every case: **NOT EXECUTED during package preparation**.

The CSV contains prerequisites, actions, expected results, evidence classification and source filename.

## Public

| ID | Scenario | Action and expected result |
| --- | --- | --- |
| E2E-01 | public landing loads successfully and login link works | Visit home; click its login link. **Expect:** HTTP 200 and actual link reaches usable login form. |
| E2E-02 | login exposes labelled inputs and password visibility works both ways | Open login; toggle visibility twice. **Expect:** Labelled controls; password type password → text → password. |
| E2E-03.1 | both empty is rejected locally without a login POST | Submit the named missing-credential variant. **Expect:** Generic validation alert; remain at login; zero authentication POSTs. |
| E2E-03.2 | email missing is rejected locally without a login POST | Submit the named missing-credential variant. **Expect:** Generic validation alert; remain at login; zero authentication POSTs. |
| E2E-03.3 | password missing is rejected locally without a login POST | Submit the named missing-credential variant. **Expect:** Generic validation alert; remain at login; zero authentication POSTs. |
| E2E-04.1 | anonymous direct navigation to /dashboard returns to login | Navigate anonymously to the specified protected URL. **Expect:** Login redirect; no logout control or stored JWT. |
| E2E-04.2 | anonymous direct navigation to /resources returns to login | Navigate anonymously to the specified protected URL. **Expect:** Login redirect; no logout control or stored JWT. |
| E2E-04.3 | anonymous direct navigation to /my-requests returns to login | Navigate anonymously to the specified protected URL. **Expect:** Login redirect; no logout control or stored JWT. |
| E2E-04.4 | anonymous direct navigation to /approval-queue returns to login | Navigate anonymously to the specified protected URL. **Expect:** Login redirect; no logout control or stored JWT. |
| E2E-04.5 | anonymous direct navigation to /admin/users returns to login | Navigate anonymously to the specified protected URL. **Expect:** Login redirect; no logout control or stored JWT. |
| E2E-04.6 | anonymous direct navigation to /jit-access returns to login | Navigate anonymously to the specified protected URL. **Expect:** Login redirect; no logout control or stored JWT. |
| E2E-05 | unknown route presents an actual not-found screen | Visit a nonexistent route. **Expect:** Visible not-found heading. |

## Read

| ID | Scenario | Action and expected result |
| --- | --- | --- |
| E2E-10 | valid developer login succeeds through the real Identity Service | Sign in as developer through UI. **Expect:** Real Identity 200; correct email/role; Dashboard. |
| E2E-11 | non-default deep link survives login | Open Resources anonymously then sign in. **Expect:** Return to Resources rather than default Dashboard. |
| E2E-12 | refresh retains the real session and loads backend data | Load Resources and refresh. **Expect:** Real inventory 200; session retained. |
| E2E-13 | logout removes all stored session fields and blocks back navigation | Log out; go back; attempt protected history. **Expect:** Token/user/expiry removed; protected navigation returns to login. |
| E2E-14 | expired stored session is cleared on reload | Expire the real stored session and reload. **Expect:** Credentials removed and login displayed. |
| E2E-15 | sidebar links navigate to request and history pages | Click actual request and history sidebar links. **Expect:** Both correct URL and destination heading. |
| E2E-16.1 | ordinary developer is specifically denied /users | Visit specified privileged route as ordinary developer. **Expect:** Specific forbidden state; no confirmation controls. |
| E2E-16.2 | ordinary developer is specifically denied /approval-queue | Visit specified privileged route as ordinary developer. **Expect:** Specific forbidden state; no confirmation controls. |
| E2E-16.3 | ordinary developer is specifically denied /audit | Visit specified privileged route as ordinary developer. **Expect:** Specific forbidden state; no confirmation controls. |
| E2E-16.4 | ordinary developer is specifically denied /policies | Visit specified privileged route as ordinary developer. **Expect:** Specific forbidden state; no confirmation controls. |
| E2E-16.5 | ordinary developer is specifically denied /onboarding | Visit specified privileged route as ordinary developer. **Expect:** Specific forbidden state; no confirmation controls. |
| E2E-16.6 | ordinary developer is specifically denied /admin/users | Visit specified privileged route as ordinary developer. **Expect:** Specific forbidden state; no confirmation controls. |
| E2E-16.7 | ordinary developer is specifically denied /admin/policies | Visit specified privileged route as ordinary developer. **Expect:** Specific forbidden state; no confirmation controls. |
| E2E-16.8 | ordinary developer is specifically denied /admin/permissions | Visit specified privileged route as ordinary developer. **Expect:** Specific forbidden state; no confirmation controls. |
| E2E-17 | Admin can load the actual directory and pending approval queue | Visit directory and pending queue as Admin. **Expect:** Real API 200 and permitted content. |
| E2E-18 | resources match API data; search excludes nonmatches and clear restores rows | Compare inventory with API; search known and nonexistent names; clear. **Expect:** Correct filtered row count; no-match state; all rows restored. |
| E2E-19 | environment filter renders only the API resources in that environment | Select environment from live inventory. **Expect:** Rendered resource names/count equal API records for that environment. |
| E2E-20 | Admin user search produces a real no-match state and restores the directory | Search nonexistent user then clear. **Expect:** No-match state then original row count restored. |
| E2E-21.1 | 19-character justification blocks submission and sends zero POSTs | Fill valid other fields; submit invalid boundary. **Expect:** Exact validation message and zero create POSTs. |
| E2E-21.2 | whitespace justification blocks submission and sends zero POSTs | Fill valid other fields; submit invalid boundary. **Expect:** Exact validation message and zero create POSTs. |
| E2E-21.3 | zero duration blocks submission and sends zero POSTs | Fill valid other fields; submit invalid boundary. **Expect:** Exact validation message and zero create POSTs. |
| E2E-21.4 | negative duration blocks submission and sends zero POSTs | Fill valid other fields; submit invalid boundary. **Expect:** Exact validation message and zero create POSTs. |
| E2E-21.5 | duration above maximum blocks submission and sends zero POSTs | Fill valid other fields; submit invalid boundary. **Expect:** Exact validation message and zero create POSTs. |
| E2E-22 | missing resource and level are both rejected without a POST | Submit empty request form. **Expect:** Resource and level errors; zero create POSTs. |
| E2E-23 | own history counters agree with persisted request states | Read own history and compare Dashboard. **Expect:** Ownership and counter totals agree with persisted backend history. |
| E2E-24 | access simulator displays the exact real authorization decision and reason | Evaluate access in simulator. **Expect:** Real decision/reason response is rendered exactly. |

## Resilience

| ID | Scenario | Action and expected result |
| --- | --- | --- |
| E2E-30 | [controlled] Resource Service 503 shows error; retry loads real data | Inject inventory 503 then remove interception and click retry. **Expect:** Error distinct from table; retry fetches real data. |
| E2E-31 | [controlled] successful empty inventory is distinct from service failure | Inject successful empty inventory. **Expect:** Empty state, not service error. |
| E2E-32 | [controlled] malformed 200 response is rejected instead of masquerading as empty data | Inject malformed inventory object instead of array. **Expect:** Error state; never treated as empty success. |
| E2E-33 | [controlled] network failure renders a recoverable error, not a success screen | Abort inventory request. **Expect:** Recoverable error, no success table. |
| E2E-34 | [controlled] API 401 clears all session fields and returns to login | Inject inventory 401. **Expect:** Session fields cleared; login redirect. |

## Lifecycle

| ID | Scenario | Action and expected result |
| --- | --- | --- |
| E2E-40 | @demo developer request → Admin approval → real JIT grant → Admin revocation | Developer creates; Admin reviews/approves; wait for grant; Admin revokes. **Expect:** Matching request/reviewer/grant/owner/resource/level/TTL; Approved → Revoked UI; repeat transitions 409. |
| E2E-41 | rejection requires a reason; developer sees persisted rejection and receives no grant | Attempt blank rejection; supply reason; inspect requester history. **Expect:** Blank reason blocks POST; persisted Rejected and reason; no JIT; later approval 409. |
| E2E-42 | cancelling approval and pressing Escape preserve Pending without a decision POST | Cancel approval; Escape rejection dialog. **Expect:** No decision POST; persisted Pending. |
| E2E-43 | rapid double click creates exactly one request; new browser session restores it | Double-click submit; inspect persisted data; sign in with a fresh context. **Expect:** Exactly one POST and one request; Pending and reason survive independent UI login. |
| E2E-44 | [real write + controlled latency] committed request survives client timeout without duplicate POST | Forward real create POST but hold response through Axios timeout. **Expect:** Client confirms persisted request; exactly one POST and one matching saved request. |
| E2E-45 | approval produces a fresh user notification and an audit event correlated by request ID | Capture notifications baseline; create and approve; poll Notification/Audit. **Expect:** Fresh requester ApprovalGranted notification shown; audit metadata contains exact request UUID. |
| E2E-46 | exactly 20 justification characters and one-minute duration are accepted by the real service | Submit exactly 20-character unique reason with one-minute duration. **Expect:** Real 201; persisted Pending; full reason in details. |
| E2E-47 | maximum 1440-minute duration is accepted as Pending without granting access | Submit a 1440-minute request without approval. **Expect:** Real 201; duration persisted; no permission granted; teardown rejects request. |

## Security

| ID | Scenario | Action and expected result |
| --- | --- | --- |
| E2E-50 | developer cannot fetch approver queue or approve/reject via direct API calls | Attempt queue read and approve/reject APIs with ordinary JWT. **Expect:** Every operation 403; request remains Pending; no JIT. |
| E2E-51 | second user cannot see another developer request in personal history or notifications | Second user reads own history and tries first user notifications. **Expect:** Own records only; created request absent; other-user notifications 403. |
| E2E-52 | SECURITY REGRESSION non-owner request-ID lookup must not disclose another user request | Second user looks up first user request by UUID. **Expect:** 403 or existence-hiding 404; no reason disclosure; 200 is a security failure. |
| E2E-53 | ordinary owner cannot revoke a grant; second user cannot list that grant | Create/approve grant; owner tries revoke; second user lists sessions. **Expect:** Owner revoke 403; second user receives only own sessions and not this grant. |
| E2E-54 | browser-stored role manipulation does not grant backend approval authority | Change local role text to Admin and try approval using original JWT. **Expect:** Backend 403; request still Pending. |
