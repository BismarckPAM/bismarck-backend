import { expect, type Page, type APIResponse } from '@playwright/test';
import type { Actor } from './fixtures.js';
import { asyncTimeout, urls } from './config.js';

export const endpoint = {
  resources: '/api/resources', users: '/api/identity/users',
  requests: '/api/approval/requests', mine: '/api/approval/requests/me',
  jit: '/api/jit/sessions?activeOnly=false',
};

export async function api(actor: Actor, path: string, method = 'GET', data?: unknown): Promise<APIResponse> {
  return actor.context.request.fetch(`${urls().api}${path}`, {
    method, headers: { Authorization: `Bearer ${actor.token}` }, data, timeout: 20_000,
  });
}

export async function json<T>(actor: Actor, path: string): Promise<T> {
  const response = await api(actor, path);
  expect(response.status(), `GET ${path}`).toBe(200);
  return response.json() as Promise<T>;
}

export async function gotoData(page: Page, route: string, apiPath: string) {
  const response = page.waitForResponse(r => new URL(r.url()).pathname === apiPath && r.request().method() === 'GET');
  await page.goto(route);
  const result = await response;
  expect(result.status(), `Backend data for ${route}`).toBe(200);
  return result;
}

export function status(value: unknown, kind: 'approval' | 'jit' = 'approval') {
  if (typeof value === 'number') return (kind === 'approval' ? ['PENDING', 'APPROVED', 'REJECTED'] : ['ACTIVE', 'EXPIRED', 'REVOKED'])[value];
  return String(value).toUpperCase();
}

export interface Approval {
  id: string; requesterUserId: string; resourceId: string; requestedLevel: number;
  reason: string; durationMinutes: number; status: string | number;
  reviewedByUserId?: string; rejectionReason?: string;
}
export interface Jit {
  id: string; approvalId: string; userId: string; resourceId: string;
  status: string | number; requestedLevel: number; grantedAt: string; expiresAt: string;
}
export interface Mine { items: Approval[]; counters: { total: number; pending: number; approved: number; rejected: number } }

export async function approvalState(actor: Actor, id: string, expected: string) {
  await expect.poll(async () => status((await json<Approval>(actor, `${endpoint.requests}/${id}`)).status), {
    timeout: asyncTimeout(), intervals: [500, 1000, 2000], message: `Request ${id} must become ${expected}`,
  }).toBe(expected);
}

export async function grantedSession(actor: Actor, approvalId: string): Promise<Jit> {
  let found: Jit | undefined;
  await expect.poll(async () => {
    const matches = (await json<Jit[]>(actor, endpoint.jit)).filter(x => x.approvalId === approvalId);
    expect(matches.length, 'Exactly one permission per approval').toBeLessThanOrEqual(1);
    found = matches[0];
    return found ? status(found.status, 'jit') : 'MISSING';
  }, { timeout: asyncTimeout(), intervals: [500, 1000, 2000], message: `Kafka must create grant for ${approvalId}` }).toBe('ACTIVE');
  return found!;
}
