import { randomUUID } from 'node:crypto';
import { type Locator, type TestInfo } from '@playwright/test';
import { test as base, expect, type Actor } from './fixtures.js';
import { api, json, endpoint, status, type Approval, type Mine, type Jit } from './api.js';
import { asyncTimeout, writeConfig } from './config.js';

export class Workflow {
  readonly marker = `SE3112-E2E-${randomUUID()}`;
  readonly config = writeConfig();
  private submittedReasons = new Set<string>();
  constructor(readonly dev: Actor, readonly admin: Actor, private info: TestInfo) {}

  async fill(reason = `${this.marker} validate temporary access to the dedicated test resource.`, duration = this.config.duration) {
    await this.dev.page.goto('/request-access');
    await expect(this.dev.page.locator('#resourceId')).toBeEnabled();
    await this.dev.page.locator('#resourceId').selectOption(this.config.resourceId);
    await this.dev.page.locator('#requestedLevel').selectOption(String(this.config.level));
    await this.dev.page.locator('#reason').fill(reason);
    await this.dev.page.locator('#durationMinutes').fill(String(duration));
    this.submittedReasons.add(reason.trim());
    return reason;
  }

  async create(reason?: string, duration = this.config.duration): Promise<Approval> {
    const submittedReason = await this.fill(reason, duration);
    const response = this.dev.page.waitForResponse(r => new URL(r.url()).pathname === endpoint.requests && r.request().method() === 'POST');
    await this.dev.page.getByRole('button', { name: 'Submit Request', exact: true }).click();
    const result = await response;
    expect(result.status(), 'Real approval creation').toBe(201);
    const created = await result.json() as Approval;
    expect(created.id).toMatch(/^[0-9a-f-]{36}$/i);
    expect(created.requesterUserId).toBe(this.dev.user.id);
    expect(created.resourceId).toBe(this.config.resourceId);
    expect(created.reason).toBe(submittedReason.trim());
    expect(created.durationMinutes).toBe(duration);
    expect(created.requestedLevel).toBe(this.config.level);
    expect(status(created.status)).toBe('PENDING');
    await expect(this.dev.page.getByTestId('submit-success')).toContainText(created.id);
    await expect(this.dev.page).toHaveURL(/\/my-requests$/);
    await this.info.attach('request-correlation', { body: JSON.stringify({ requestId: created.id, marker: this.marker, resourceId: this.config.resourceId }), contentType: 'application/json' });
    return created;
  }

  async queueRow(id: string): Promise<Locator> {
    await this.admin.page.goto('/approval-queue');
    const row = this.admin.page.getByRole('row').filter({ has: this.admin.page.locator(`td[title="${id}"]`) });
    await expect(row).toBeVisible();
    return row;
  }

  async approve(id: string) {
    const row = await this.queueRow(id);
    await row.getByRole('button', { name: 'Approve', exact: true }).click();
    const modal = this.admin.page.getByRole('dialog', { name: 'Confirm approval' });
    await expect(modal).toContainText(id);
    const response = this.admin.page.waitForResponse(r => new URL(r.url()).pathname === `${endpoint.requests}/${id}/approve` && r.request().method() === 'POST');
    await modal.getByRole('button', { name: 'Confirm Approve', exact: true }).click();
    expect((await response).status()).toBe(200);
    await expect(modal).toHaveCount(0);
    await expect(row).toHaveCount(0);
  }

  async reject(id: string, reason: string) {
    const row = await this.queueRow(id);
    await row.getByRole('button', { name: 'Reject', exact: true }).click();
    const modal = this.admin.page.getByRole('dialog', { name: 'Reject request', exact: true });
    await modal.getByLabel('Rejection reason', { exact: false }).fill(reason);
    const response = this.admin.page.waitForResponse(r => new URL(r.url()).pathname === `${endpoint.requests}/${id}/reject` && r.request().method() === 'POST');
    await modal.getByRole('button', { name: 'Confirm Reject', exact: true }).click();
    expect((await response).status()).toBe(200);
    await expect(modal).toHaveCount(0);
    await expect(row).toHaveCount(0);
  }

  async history(actor: Actor, id: string, expected: string) {
    await actor.page.goto('/my-requests');
    await expect(actor.page.getByRole('textbox', { name: 'Search my requests' })).toBeVisible();
    await actor.page.getByRole('textbox', { name: 'Search my requests' }).fill(id);
    const row = actor.page.getByRole('row').filter({ has: actor.page.locator(`td[title="${id}"]`) });
    await expect(row).toBeVisible();
    await expect(row).toContainText(new RegExp(expected, 'i'));
    return row;
  }

  async details(actor: Actor, id: string) {
    await actor.page.getByRole('button', { name: `View details for request ${id}`, exact: true }).click();
    const dialog = actor.page.getByRole('dialog', { name: 'Request details', exact: true });
    await expect(dialog).toContainText(id);
    return dialog;
  }

  async cleanup() {
    const actions: Array<{ id: string; action: string }> = [];
    // Discover by unique marker even if the original UI call failed after persistence.
    const own = (await json<Mine>(this.dev, endpoint.mine)).items.filter(x => x.reason.includes(this.marker) || this.submittedReasons.has(x.reason));
    for (const request of own) {
      if (status(request.status) === 'PENDING') {
        const response = await api(this.admin, `${endpoint.requests}/${request.id}/reject`, 'POST', { reason: `${this.marker} automated teardown` });
        expect(response.status(), `Cleanup pending request ${request.id}`).toBe(200);
        actions.push({ id: request.id, action: 'REJECTED_TEST_REQUEST' });
      }
      if (status(request.status) === 'APPROVED') {
        // A queued grant may arrive after the browser assertion failed: wait for the
        // record before cleanup so a delayed ACTIVE permission is not left behind.
        let grants: Jit[] = [];
        await expect.poll(async () => {
          grants = (await json<Jit[]>(this.admin, endpoint.jit)).filter(x => x.approvalId === request.id);
          return grants.length;
        }, { timeout: asyncTimeout(), intervals: [500, 1000, 2000], message: `Find grant for teardown ${request.id}` }).toBeGreaterThan(0);
        for (const grant of grants) {
          if (status(grant.status, 'jit') === 'ACTIVE' && Date.parse(grant.expiresAt) > Date.now()) {
            const response = await api(this.admin, `/api/jit/sessions/${grant.id}/revoke`, 'POST', { reason: `${this.marker} automated teardown` });
            expect(response.status(), `Cleanup grant ${grant.id}`).toBe(200);
            actions.push({ id: grant.id, action: 'REVOKED_TEST_GRANT' });
          }
        }
      }
    }
    await this.info.attach('cleanup', { body: JSON.stringify({ marker: this.marker, actions, retained: 'Historical requests and audit records; no supported delete endpoint.' }), contentType: 'application/json' });
  }
}

export const test = base.extend<{ flow: Workflow }>({
  flow: [async ({ dev, admin }, use, info) => {
    const flow = new Workflow(dev, admin, info);
    // Fail on bad setup before submitting anything, rather than silently skipping.
    const resources = await json<Array<{ id: string; type?: string; azureVmName?: string }>>(dev, endpoint.resources);
    const resource = resources.find(x => x.id === flow.config.resourceId);
    expect(resource, 'Dedicated test resource must exist').toBeTruthy();
    expect(resource?.azureVmName, 'Use a non-VM resource without a cloud target').toBeFalsy();
    try { await use(flow); }
    finally {
      // Teardown failures are failures; they are never converted into passing results.
      await flow.cleanup();
    }
  }, { timeout: 180_000 }],
});
export { expect };
