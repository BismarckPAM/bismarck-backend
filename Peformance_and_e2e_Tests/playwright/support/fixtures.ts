import { test as base, expect, type Browser, type BrowserContext, type Page, type TestInfo } from '@playwright/test';
import { unlink } from 'node:fs/promises';
import { credentials, urls, type Role } from './config.js';

export interface Actor {
  page: Page;
  context: BrowserContext;
  token: string;
  user: { id: string; email: string; role: string };
}
type State = Awaited<ReturnType<BrowserContext['storageState']>>;
type Session = { state: State; token: string; user: Actor['user'] };
type Sessions = (role: Role) => Promise<Session>;

export async function loginUI(page: Page, role: Role) {
  const account = credentials(role);
  await page.goto(`${urls().frontend}/login`);
  await page.getByLabel('Corporate Email Address').fill(account.email);
  await page.getByLabel('Master Security Password').fill(account.password);
  const response = page.waitForResponse(r =>
    new URL(r.url()).pathname === '/api/identity/auth/login' && r.request().method() === 'POST');
  await page.getByRole('button', { name: 'Authenticate & Enter' }).click();
  expect((await response).status(), 'Real UI login must succeed').toBe(200);
  await expect(page.getByTestId('logout-btn')).toBeVisible();
  await expect(page).not.toHaveURL(/\/login(?:[?#]|$)/);
  const session = await page.evaluate(() => ({
    token: localStorage.getItem('bismarck_pam_token'),
    user: JSON.parse(localStorage.getItem('bismarck_pam_user') || 'null'),
  }));
  expect(session.token).toBeTruthy();
  expect(session.user?.id).toBeTruthy();
  expect(session.user?.email.toLowerCase()).toBe(account.email.toLowerCase());
  const actualRole = String(session.user?.role).trim().toLowerCase();
  if (role === 'admin') expect(actualRole).toBe('admin');
  else expect(['admin', 'manager', 'security admin', 'securityadmin']).not.toContain(actualRole);
  return { token: session.token as string, user: session.user as Actor['user'] };
}

async function actor(browser: Browser, sessions: Sessions, role: Role, info: TestInfo): Promise<Actor> {
  const session = await sessions(role);
  const context = await browser.newContext({
    baseURL: urls().frontend, storageState: session.state, serviceWorkers: 'block',
    recordVideo: { dir: info.outputPath(`${role}-video`) },
  });
  // Playwright Test instruments new contexts and records the configured trace.
  // Do not call tracing.start(): that would conflict with the runner's recorder.
  const page = await context.newPage();
  return { ...session, context, page };
}

async function closeActor(a: Actor, role: Role, info: TestInfo) {
  const failed = info.status !== info.expectedStatus;
  if (failed && !a.page.isClosed()) {
    await info.attach(`${role}-screenshot`, { body: await a.page.screenshot({ fullPage: true }), contentType: 'image/png' });
  }
  const video = a.page.video();
  await a.context.close();
  if (video) {
    const videoPath = await video.path();
    if (failed) await info.attach(`${role}-video`, { path: videoPath, contentType: 'video/webm' });
    else await unlink(videoPath);
  }
}

export const test = base.extend<{ dev: Actor; admin: Actor; other: Actor }, { sessions: Sessions }>({
  sessions: [async ({ browser }, use) => {
    // In-memory worker cache: no JWTs or credential files written to disk.
    const cache = new Map<Role, Session>();
    await use(async role => {
      const existing = cache.get(role);
      if (existing) return existing;
      const context = await browser.newContext({ baseURL: urls().frontend, serviceWorkers: 'block' });
      try {
        const page = await context.newPage();
        const auth = await loginUI(page, role);
        const value = { ...auth, state: await context.storageState() };
        for (const other of cache.values()) expect(other.user.id, 'Accounts must be distinct').not.toBe(value.user.id);
        cache.set(role, value);
        return value;
      } finally { await context.close(); }
    });
  }, { scope: 'worker' }],
  dev: async ({ browser, sessions }, use, info) => { const a = await actor(browser, sessions, 'dev', info); try { await use(a); } finally { await closeActor(a, 'dev', info); } },
  admin: async ({ browser, sessions }, use, info) => { const a = await actor(browser, sessions, 'admin', info); try { await use(a); } finally { await closeActor(a, 'admin', info); } },
  other: async ({ browser, sessions }, use, info) => { const a = await actor(browser, sessions, 'other', info); try { await use(a); } finally { await closeActor(a, 'other', info); } },
});
export { expect };
