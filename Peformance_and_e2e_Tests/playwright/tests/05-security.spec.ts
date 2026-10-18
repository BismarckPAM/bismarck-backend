import { test, expect } from '../support/workflow.js';
import { api, endpoint, json, approvalState, grantedSession, type Jit, type Mine } from '../support/api.js';

test.setTimeout(180_000);

test('E2E-50 developer cannot fetch approver queue or approve/reject via direct API calls', async ({ flow, dev }) => {
  const request = await flow.create();
  expect((await api(dev, endpoint.requests)).status()).toBe(403);
  expect((await api(dev, `${endpoint.requests}/${request.id}/approve`, 'POST')).status()).toBe(403);
  expect((await api(dev, `${endpoint.requests}/${request.id}/reject`, 'POST', { reason: 'Unauthorized attempt from test developer' })).status()).toBe(403);
  await approvalState(dev, request.id, 'PENDING');
  expect((await json<Jit[]>(dev, endpoint.jit)).filter(x => x.approvalId === request.id)).toHaveLength(0);
});

test('E2E-51 second user cannot see another developer request in personal history or notifications', async ({ flow, dev, other }) => {
  const request = await flow.create();
  const history = await json<Mine>(other, endpoint.mine);
  expect(history.items.every(x => x.requesterUserId === other.user.id)).toBe(true);
  expect(history.items.find(x => x.id === request.id)).toBeUndefined();
  expect((await api(other, `/api/notifications/${dev.user.id}`)).status()).toBe(403);
});

test('E2E-52 SECURITY REGRESSION non-owner request-ID lookup must not disclose another user request', async ({ flow, other }) => {
  const request = await flow.create();
  const response = await api(other, `${endpoint.requests}/${request.id}`);
  // Both denial and existence-hiding are secure contracts. A 200 is NEVER allowed.
  expect([403, 404], 'Non-owner request lookup must enforce ownership server-side').toContain(response.status());
  expect(await response.text()).not.toContain(request.reason);
});

test('E2E-53 ordinary owner cannot revoke a grant; second user cannot list that grant', async ({ flow, dev, other }) => {
  const request = await flow.create();
  await flow.approve(request.id);
  const grant = await grantedSession(dev, request.id);
  expect((await api(dev, `/api/jit/sessions/${grant.id}/revoke`, 'POST', { reason: 'Unauthorized owner revoke test' })).status()).toBe(403);
  expect((await json<Jit[]>(other, endpoint.jit)).every(x => x.userId === other.user.id)).toBe(true);
  expect((await json<Jit[]>(other, endpoint.jit)).find(x => x.id === grant.id)).toBeUndefined();
  await expect(dev.page.getByTestId('forbidden')).toHaveCount(0);
});

test('E2E-54 browser-stored role manipulation does not grant backend approval authority', async ({ flow, dev }) => {
  const request = await flow.create();
  await dev.page.evaluate(() => {
    const user = JSON.parse(localStorage.getItem('bismarck_pam_user')!);
    localStorage.setItem('bismarck_pam_user', JSON.stringify({ ...user, role: 'Admin' }));
  });
  await dev.page.goto('/approval-queue');
  // The presentation layer may trust local role text; the signed JWT must remain authoritative.
  expect((await api(dev, `${endpoint.requests}/${request.id}/approve`, 'POST')).status()).toBe(403);
  await approvalState(dev, request.id, 'PENDING');
});
