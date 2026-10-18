import { test, expect } from '../support/workflow.js';
import { api, json, endpoint, approvalState, grantedSession, status, type Approval, type Mine, type Jit } from '../support/api.js';
import { asyncTimeout, urls } from '../support/config.js';
import { randomUUID } from 'node:crypto';

test.setTimeout(180_000);

test('E2E-40 @demo developer request → Admin approval → real JIT grant → Admin revocation', async ({ flow, dev, admin }) => {
  const request = await test.step('Developer submits through the UI and sees Pending', async () => {
    const created = await flow.create();
    await flow.history(dev, created.id, 'Pending');
    return created;
  });

  await test.step('Admin reviews the exact request and approves through the UI', async () => {
    const row = await flow.queueRow(request.id);
    await row.getByRole('button', { name: `View details for request ${request.id}` }).click();
    const dialog = admin.page.getByRole('dialog', { name: 'Request details', exact: true });
    await expect(dialog).toContainText(request.reason);
    await expect(dialog).toContainText(dev.user.id);
    await dialog.getByRole('button', { name: 'Close dialog' }).click();
    await flow.approve(request.id);
    await approvalState(dev, request.id, 'APPROVED');
    const stored = await json<Approval>(dev, `${endpoint.requests}/${request.id}`);
    expect(stored.reviewedByUserId).toBe(admin.user.id);
    await flow.history(dev, request.id, 'Approved');
  });

  const grant = await test.step('Authorization consumes the event and grants only this developer access', async () => {
    const session = await grantedSession(dev, request.id);
    expect(session.userId).toBe(dev.user.id);
    expect(session.resourceId).toBe(request.resourceId);
    expect(session.requestedLevel).toBe(request.requestedLevel);
    expect(Date.parse(session.expiresAt) - Date.parse(session.grantedAt)).toBe(request.durationMinutes * 60_000);
    await dev.page.goto('/jit-access');
    const row = dev.page.getByRole('table', { name: 'Active JIT sessions', exact: true }).getByRole('row').filter({ hasText: session.id.slice(0, 8) });
    await expect(row).toBeVisible();
    await expect(row.getByRole('button', { name: 'Revoke', exact: true })).toHaveCount(0);
    await expect(row).toContainText('Admin only');
    return session;
  });

  await test.step('Repeat approval is rejected with 409 and creates no second grant', async () => {
    expect((await api(admin, `${endpoint.requests}/${request.id}/approve`, 'POST')).status()).toBe(409);
    expect((await json<Jit[]>(admin, endpoint.jit)).filter(x => x.approvalId === request.id)).toHaveLength(1);
  });

  await test.step('Admin revokes through the UI; developer sees the persisted revocation', async () => {
    await admin.page.goto('/jit-access');
    const row = admin.page.getByRole('table', { name: 'Active JIT sessions', exact: true }).getByRole('row').filter({ hasText: grant.id.slice(0, 8) });
    await row.getByRole('button', { name: 'Revoke', exact: true }).click();
    const dialog = admin.page.getByRole('dialog', { name: 'Revoke temporary permission', exact: true });
    await dialog.getByLabel('Reason (optional, for your records)').fill(`${flow.marker} completed demonstration`);
    const response = admin.page.waitForResponse(r => new URL(r.url()).pathname === `/api/jit/sessions/${grant.id}/revoke` && r.request().method() === 'POST');
    await dialog.getByRole('button', { name: 'Confirm Revoke', exact: true }).click();
    expect((await response).status()).toBe(200);
    await expect.poll(async () => status((await json<Jit[]>(dev, endpoint.jit)).find(x => x.id === grant.id)?.status, 'jit'), { timeout: asyncTimeout() }).toBe('REVOKED');
    await dev.page.reload();
    await expect(dev.page.getByRole('table', { name: 'Active JIT sessions', exact: true }).getByRole('row').filter({ hasText: grant.id.slice(0, 8) })).toHaveCount(0);
    await expect(dev.page.getByRole('row').filter({ hasText: grant.id.slice(0, 8) })).toContainText('REVOKED');
    expect((await api(admin, `/api/jit/sessions/${grant.id}/revoke`, 'POST', { reason: 'Repeated test revoke' })).status()).toBe(409);
  });
});

test('E2E-41 rejection requires a reason; developer sees persisted rejection and receives no grant', async ({ flow, dev, admin }) => {
  const request = await flow.create();
  const row = await flow.queueRow(request.id);
  await row.getByRole('button', { name: 'Reject', exact: true }).click();
  const dialog = admin.page.getByRole('dialog', { name: 'Reject request', exact: true });
  let posts = 0;
  admin.page.on('request', r => { if (new URL(r.url()).pathname === `${endpoint.requests}/${request.id}/reject` && r.method() === 'POST') posts++; });
  await dialog.getByRole('button', { name: 'Confirm Reject', exact: true }).click();
  await expect(dialog.getByRole('alert')).toHaveText('A rejection reason is required.');
  expect(posts).toBe(0);
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  const reason = `${flow.marker} rejected because the requested access is unnecessary.`;
  await flow.reject(request.id, reason);
  await approvalState(dev, request.id, 'REJECTED');
  await flow.history(dev, request.id, 'Rejected');
  const details = await flow.details(dev, request.id);
  await expect(details).toContainText(reason);
  expect((await json<Approval>(dev, `${endpoint.requests}/${request.id}`)).rejectionReason).toBe(reason);
  expect((await json<Jit[]>(dev, endpoint.jit)).filter(x => x.approvalId === request.id)).toHaveLength(0);
  expect((await api(admin, `${endpoint.requests}/${request.id}/approve`, 'POST')).status()).toBe(409);
});

test('E2E-42 cancelling approval and pressing Escape preserve Pending without a decision POST', async ({ flow, admin, dev }) => {
  const request = await flow.create();
  const row = await flow.queueRow(request.id);
  let decisions = 0;
  admin.page.on('request', r => { if (new URL(r.url()).pathname.startsWith(`${endpoint.requests}/${request.id}/`) && r.method() === 'POST') decisions++; });
  await row.getByRole('button', { name: 'Approve', exact: true }).click();
  await admin.page.getByRole('dialog', { name: 'Confirm approval' }).getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(admin.page.getByRole('dialog')).toHaveCount(0);
  await row.getByRole('button', { name: 'Reject', exact: true }).click();
  await admin.page.keyboard.press('Escape');
  await expect(admin.page.getByRole('dialog')).toHaveCount(0);
  expect(decisions).toBe(0);
  await approvalState(dev, request.id, 'PENDING');
});

test('E2E-43 rapid double click creates exactly one request; new browser session restores it', async ({ flow, dev, browser }) => {
  const reason = await flow.fill();
  let posts = 0;
  dev.page.on('request', r => { if (new URL(r.url()).pathname === endpoint.requests && r.method() === 'POST') posts++; });
  const response = dev.page.waitForResponse(r => new URL(r.url()).pathname === endpoint.requests && r.request().method() === 'POST');
  await dev.page.getByRole('button', { name: 'Submit Request', exact: true }).dblclick();
  const result = await response;
  expect(result.status()).toBe(201);
  const request = await result.json() as Approval;
  await expect(dev.page).toHaveURL(/\/my-requests$/);
  expect(posts).toBe(1);
  expect((await json<Mine>(dev, endpoint.mine)).items.filter(x => x.reason === reason)).toHaveLength(1);

  // New context uses a fresh UI login, not the first tab's local state.
  const { loginUI } = await import('../support/fixtures.js');
  const context = await browser.newContext({ baseURL: urls().frontend, serviceWorkers: 'block' });
  try {
    const page = await context.newPage();
    await loginUI(page, 'dev');
    const restored = { ...dev, page, context };
    await flow.history(restored, request.id, 'Pending');
    await expect(await flow.details(restored, request.id)).toContainText(reason);
  } finally { await context.close(); }
});

test('E2E-44 [real write + controlled latency] committed request survives client timeout without duplicate POST', async ({ flow, dev }) => {
  const reason = await flow.fill();
  let posts = 0;
  let committedId = '';
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  const handler = async (route: import('@playwright/test').Route) => {
    if (route.request().method() !== 'POST') return route.continue();
    posts++;
    // Forward to the REAL service exactly once. Only the response delivery is held.
    const result = await route.fetch({ timeout: 30_000 });
    expect(result.status()).toBe(201);
    committedId = (await result.json()).id;
    await gate;
    // Axios has already timed out; finish this intercepted request without a fake body.
    await route.abort('timedout');
  };
  await dev.page.route('**/api/approval/requests', handler);
  try {
    await dev.page.getByRole('button', { name: 'Submit Request', exact: true }).click();
    // Real 15s Axios timeout is deliberately exercised. No arbitrary test sleep.
    await expect(dev.page.getByTestId('submit-success')).toContainText('submitted successfully', { timeout: 40_000 });
    await expect(dev.page).toHaveURL(/\/my-requests$/, { timeout: 15_000 });
    expect(committedId).toBeTruthy();
    const matches = (await json<Mine>(dev, endpoint.mine)).items.filter(x => x.reason === reason);
    expect(matches).toHaveLength(1);
    expect(matches[0].id).toBe(committedId);
    expect(posts).toBe(1);
  } finally {
    release();
    await dev.page.unrouteAll({ behavior: 'wait' });
  }
});

test('E2E-45 approval produces a fresh user notification and an audit event correlated by request ID', async ({ flow, dev, admin }) => {
  type Notification = { id: string; title: string; message: string; eventType: string };
  const notificationPath = `/api/notifications/${dev.user.id}?page=1&pageSize=100`;
  const baseline = new Set((await json<{ items: Notification[] }>(dev, notificationPath)).items.map(x => x.id));
  const request = await flow.create();
  await flow.approve(request.id);
  let notification: Notification | undefined;
  await expect.poll(async () => {
    notification = (await json<{ items: Notification[] }>(dev, notificationPath)).items.find(x => !baseline.has(x.id) && x.eventType === 'ApprovalGranted');
    return Boolean(notification);
  }, { timeout: asyncTimeout(), intervals: [500, 1000, 2000], message: 'New requester approval notification' }).toBe(true);
  await dev.page.goto('/notifications');
  const item = dev.page.getByTestId('feed-item').filter({ hasText: notification!.message });
  await expect(item.filter({ hasText: 'Approval Granted' }).first()).toBeVisible();

  type Audit = { id: string; eventType: string; metadata: unknown; actor: string; outcome: string };
  const query = new URLSearchParams({ resource: request.resourceId, eventType: 'ApprovalGranted', pageSize: '100' });
  let audit: Audit | undefined;
  await expect.poll(async () => {
    audit = (await json<{ items: Audit[] }>(admin, `/api/audit/logs?${query}`)).items.find(x => JSON.stringify(x.metadata).includes(request.id));
    return Boolean(audit);
  }, { timeout: asyncTimeout(), intervals: [500, 1000, 2000], message: 'Audit metadata must contain THIS request ID' }).toBe(true);
  expect(audit!.actor).toBe(admin.user.id);
  expect(audit!.outcome).toBe('APPROVED');
  await test.info().attach('event-evidence', { body: JSON.stringify({ requestId: request.id, auditId: audit!.id, notificationId: notification!.id, notificationCorrelation: 'Fresh ID + requester + event type; notification DTO has no ApprovalId.' }), contentType: 'application/json' });
});

test('E2E-46 exactly 20 justification characters and one-minute duration are accepted by the real service', async ({ flow, dev }) => {
  const reason = randomUUID().replaceAll('-', '').slice(0, 20);
  const request = await flow.create(reason, 1);
  await flow.history(dev, request.id, 'Pending');
  await expect(await flow.details(dev, request.id)).toContainText(reason);
});

test('E2E-47 maximum 1440-minute duration is accepted as Pending without granting access', async ({ flow, dev }) => {
  const request = await flow.create(`${flow.marker} maximum-duration boundary request, never approved.`, 1440);
  expect((await json<Approval>(dev, `${endpoint.requests}/${request.id}`)).durationMinutes).toBe(1440);
  expect((await json<Jit[]>(dev, endpoint.jit)).filter(x => x.approvalId === request.id)).toHaveLength(0);
  // Teardown rejects this Pending request; no 24-hour permission is approved.
});
